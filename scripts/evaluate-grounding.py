"""Generate a held-out synthetic set or evaluate it with explicit live-cost consent.

Synthetic cases do not qualify actual Notepad, VS Code, or Railway interfaces.
The expected-target oracle is read only by this evaluation harness.
"""
import argparse
import asyncio
import io
import json
import math
import random
import sys
from datetime import UTC, datetime
from pathlib import Path
from uuid import uuid4

from live_budget import LiveBudget
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "services/api"))
from app.config import Settings  # noqa: E402
from app.contracts import Observation, TurnRequest  # noqa: E402
from app.providers import GeminiProvider  # noqa: E402


def generate(directory: Path):
    directory.mkdir(parents=True, exist_ok=True)
    rng = random.Random(90471831)
    cases = []
    for index in range(200):
        workflow = ["sandbox", "notepad", "vscode", "railway"][index % 4]
        width, height = rng.choice([(800, 500), (1200, 700), (1600, 900)])
        label = {"sandbox": "Settings", "notepad": "Save practice note", "vscode": "Open practice file", "railway": "View project settings"}[workflow]
        left, top = rng.randrange(40, width - 260), rng.randrange(100, height - 100)
        right, bottom = left + 220, top + 55
        image = Image.new("RGB", (width, height), rng.choice(["#f1f6f4", "#e8eff9"]))
        draw = ImageDraw.Draw(image)
        draw.text((30, 25), f"SYNTHETIC {workflow} practice — no real application", fill="black")
        draw.rectangle((left, top, right, bottom), fill="#286654")
        draw.text((left + 14, top + 18), label, fill="white")
        draw.text((30, height - 30), "Help notice is unrelated to task completion", fill="black")
        name = f"case-{index:03d}.png"
        image.save(directory / name)
        cases.append({"image": name, "workflow": workflow, "goal": f"Show where to {label.lower()}", "width": width, "height": height,
                      "expected": [left / (width - 1), top / (height - 1), right / (width - 1), bottom / (height - 1)]})
    (directory / "manifest.json").write_text(json.dumps({"synthetic": True, "seed": 90471831, "cases": cases}, indent=2), encoding="utf-8")
    print(f"Generated {len(cases)} synthetic held-out cases. No actual workflow is qualified.")


async def evaluate(directory: Path, project: str, limit: int, budget: LiveBudget):
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    provider = GeminiProvider(Settings(model_provider="gemini", gcp_project_id=project, _env_file=None))
    outcomes = []
    try:
        for case in manifest["cases"][:limit]:
            data = (directory / case["image"]).read_bytes()
            with Image.open(io.BytesIO(data)) as image:
                width, height = image.size
            observation = Observation(id=uuid4(), captured_at=datetime.now(UTC), scope_id=uuid4(), geometry_id=uuid4(), image_width=width, image_height=height, privacy_filtered=True)
            try:
                reservation = budget.reserve("synthetic target selection")
            except ValueError:
                break
            try:
                result, usage = await provider.guide(case["goal"], {"milestones": [case["goal"]]}, TurnRequest(request_id=uuid4(), session_version=0, observation=observation, event_kind="start"), data)
                target = result.step.target if result.step else None
                cost = budget.reconcile(reservation, usage.input_tokens, usage.output_tokens) if usage.known else budget.RESERVATION_USD
                binding_valid = bool(result.step and result.step.source_observation_id == observation.id
                    and all(item.observation_id == observation.id for item in result.step.evidence)
                    and target and target.geometry_id == observation.geometry_id and not result.completed)
                if target:
                    x = (target.bounds.left + target.bounds.right) / 2
                    y = (target.bounds.top + target.bounds.bottom) / 2
                    left, top, right, bottom = case["expected"]
                    correct = binding_valid and left <= x <= right and top <= y <= bottom
                else:
                    correct = False
                outcomes.append({"image": case["image"], "correct": correct, "binding_valid": binding_valid, "input_tokens": usage.input_tokens, "output_tokens": usage.output_tokens, "estimated_usd": cost})
            except Exception:
                outcomes.append({"image": case["image"], "correct": False, "error": "provider unavailable or invalid output"})
    finally:
        await provider.close()
    report = {"synthetic_only": manifest["synthetic"], "sample_size": len(outcomes), "correct": sum(case["correct"] for case in outcomes), "outcomes": outcomes,
              "real_application_qualification": "not established", "model": "gemini-3.8-flash", "location": "global"}
    report["requested_sample_size"] = limit
    report["estimated_reported_cost_usd"] = sum(case.get("estimated_usd", 0) for case in outcomes)
    n = len(outcomes)
    if n:
        p, z = report["correct"] / n, 1.96
        center = (p + z * z / (2 * n)) / (1 + z * z / n)
        margin = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / (1 + z * z / n)
        report.update(accuracy=p, wilson_95_interval=[center - margin, center + margin], initial_95_percent_target_met=p >= .95)
    (directory / "evaluation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "outcomes"}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--generate", action="store_true")
    parser.add_argument("--directory", type=Path, default=ROOT / "artifacts/evaluation")
    parser.add_argument("--allow-live", action="store_true")
    parser.add_argument("--project")
    parser.add_argument("--limit", type=int, default=20)
    parser.add_argument("--budget-ledger", type=Path, default=ROOT / "artifacts/live-validation-budget.json")
    args = parser.parse_args()
    if args.generate:
        generate(args.directory)
    elif args.allow_live and args.project and 1 <= args.limit <= 200:
        asyncio.run(evaluate(args.directory, args.project, args.limit, LiveBudget(args.budget_ledger, args.project)))
    else:
        parser.error("Use --generate, or authorize billable inference with --allow-live --project ID --limit 1..200")
