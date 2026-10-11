"""Opt-in synthetic loading/verification checks. No real screenshots or input.

Expected visual outcomes stay in this harness; only the goal, prior step and
generated image reach the provider. Uses the existing shared authorization ledger.
"""
import argparse
import asyncio
import io
import json
import sys
import time
from datetime import UTC, datetime
from pathlib import Path
from uuid import uuid4

from live_budget import LiveBudget
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "services/api"))
from app.config import Settings  # noqa: E402
from app.contracts import GuidanceStep, Observation, TurnRequest  # noqa: E402
from app.providers import GeminiProvider  # noqa: E402


async def check(project: str, output: Path, budget: LiveBudget):
    provider = GeminiProvider(Settings(model_provider="gemini", gcp_project_id=project, _env_file=None))
    outcomes = []
    try:
        for name in ("loading", "ready_after_loading", "unchanged_after_click"):
            image = Image.new("RGB", (800, 450), "white")
            draw = ImageDraw.Draw(image)
            draw.text((30, 30), "Synthetic practice settings", fill="black")
            if name == "loading":
                draw.text((250, 200), "Loading settings... Please wait", fill="black")
            else:
                draw.rectangle((520, 330, 750, 410), fill="#286654")
                draw.text((550, 360), "Save practice settings", fill="white")
                draw.text((30, 90), "Changes are NOT saved", fill="black")
            stream = io.BytesIO()
            image.save(stream, "PNG")
            observation = Observation(id=uuid4(), captured_at=datetime.now(UTC), scope_id=uuid4(),
                geometry_id=uuid4(), image_width=800, image_height=450, privacy_filtered=True)
            previous = None
            if name != "loading":
                previous_id = uuid4()
                waiting = name == "ready_after_loading"
                previous = GuidanceStep(id=uuid4(), source_observation_id=previous_id,
                    instruction="Wait for settings to load" if waiting else "Click Save practice settings",
                    explanation="Finish loading" if waiting else "Save the changes",
                    expected_user_action="wait" if waiting else "click", target=None,
                    postconditions=[{"kind": "visible_text", "value": "Save practice settings" if waiting else "Settings saved successfully"}],
                    evidence=[{"observation_id": previous_id, "detail": "Previous synthetic screen"}])
            turn = TurnRequest(request_id=uuid4(), session_version=0, observation=observation,
                event_kind="start" if previous is None else "reobserve", previous_step=previous)
            try:
                reservation = budget.reserve("synthetic visual state: " + name)
            except ValueError:
                outcomes.append({"case": name, "passed": False, "error_type": "BudgetExhausted", "request_sent": False})
                break
            started = time.monotonic()
            outcome = {"case": name}
            try:
                result, usage = await provider.guide("Save practice settings", {"milestones": []},
                    turn, stream.getvalue())
                outcome["estimated_usd"] = budget.reconcile(reservation, usage.input_tokens, usage.output_tokens) if usage.known else .08
                step, verification = result.step, result.verification
                binding = (not step or (step.source_observation_id == observation.id and bool(step.evidence)
                    and all(e.observation_id == observation.id for e in step.evidence)
                    and (not step.target or step.target.geometry_id == observation.geometry_id)))
                binding = binding and (not verification or (bool(verification.evidence)
                    and all(e.observation_id == observation.id for e in verification.evidence)))
                if name == "loading":
                    correct = bool(step and step.expected_user_action == "wait" and step.target is None and not result.completed)
                elif name == "ready_after_loading":
                    target = step.target if step else None
                    correct = bool(verification and verification.status.value == "CONFIRMED" and target
                        and .65 <= (target.bounds.left + target.bounds.right) / 2 <= .9375
                        and .733 <= (target.bounds.top + target.bounds.bottom) / 2 <= .912 and not result.completed)
                else:
                    correct = bool(verification and verification.status.value != "CONFIRMED"
                        and step is None and result.action_proposal is None and not result.completed)
                outcome.update(passed=bool(binding and correct), binding_valid=bool(binding),
                    state_decision_valid=bool(correct), verification_status=verification.status.value if verification else None,
                    has_next_step=step is not None, completed=result.completed,
                    input_tokens=usage.input_tokens, output_tokens=usage.output_tokens, thinking_tokens=usage.thinking_tokens)
            except Exception as exc:
                outcome.update(passed=False, error_type=type(exc).__name__,
                    retryable=getattr(exc, "retryable", False), schema_invalid=getattr(exc, "schema_invalid", False))
            outcome["elapsed_ms"] = round((time.monotonic() - started) * 1000)
            outcomes.append(outcome)
    finally:
        await provider.close()
    report = {"synthetic_only": True, "model": provider.settings.gemini_guidance_model,
        "thinking_setting": provider.settings.gemini_thinking_level, "outcomes": outcomes,
        "passed": len(outcomes) == 3 and all(o["passed"] for o in outcomes),
        "real_application_qualification": "not established"}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--allow-live", action="store_true")
    parser.add_argument("--project", required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--budget-ledger", required=True, type=Path)
    args = parser.parse_args()
    if not args.allow_live:
        parser.error("live inference requires --allow-live and the explicitly approved project")
    raise SystemExit(asyncio.run(check(args.project, args.output, LiveBudget(args.budget_ledger, args.project))))
