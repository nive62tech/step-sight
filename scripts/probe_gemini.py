"""Explicit opt-in synthetic-media capability probe. No real desktop capture or input."""
import argparse
import asyncio
import importlib.metadata
import io
import json
import sys
from datetime import UTC, datetime
from pathlib import Path
from uuid import uuid4

from google.genai import errors
from live_budget import LiveBudget
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "services/api"))
from app.config import Settings  # noqa: E402
from app.contracts import Observation, TurnRequest  # noqa: E402
from app.providers import GeminiProvider  # noqa: E402


async def probe(project: str, location: str, output: Path, budget: LiveBudget):
    settings = Settings(model_provider="gemini", gcp_project_id=project, gcp_location=location, _env_file=None)
    provider = GeminiProvider(settings)
    diagnostics = {}
    generate = provider.client.aio.models.generate_content

    async def diagnosed_generate(*args, **kwargs):
        try:
            return await generate(*args, **kwargs)
        except errors.APIError as exc:
            diagnostics.update(http_status=exc.code, provider_status=exc.status)
            raise
        except ValueError:
            diagnostics["local_request_configuration_error"] = True
            raise

    provider.client.aio.models.generate_content = diagnosed_generate
    image = Image.new("RGB", (800, 450), "white")
    draw = ImageDraw.Draw(image)
    draw.text((30, 30), "Synthetic practice settings", fill="black")
    draw.rectangle((520, 330, 750, 410), fill="#286654")
    draw.text((550, 360), "Save practice settings", fill="white")
    stream = io.BytesIO()
    image.save(stream, "PNG")
    observation = Observation(id=uuid4(), captured_at=datetime.now(UTC), scope_id=uuid4(), geometry_id=uuid4(), image_width=800, image_height=450, privacy_filtered=True)
    report = {"date": datetime.now(UTC).isoformat(), "project": project, "location": location,
              "model": settings.gemini_guidance_model, "sdk": importlib.metadata.version("google-genai"),
              "synthetic_media_only": True, "native_computer_use": "disabled", "tests": {}}
    try:
        reservation = budget.reserve("image and structured output capability probe")
        result, usage = await provider.guide("Show where to save practice settings", {"milestones": ["Save practice settings"]},
                                            TurnRequest(request_id=uuid4(), session_version=0, observation=observation, event_kind="start"), stream.getvalue())
        report["tests"]["authentication_image_and_structured_output"] = "passed"
        report["tests"]["grounding"] = "passed" if result.step and result.step.target and result.step.target.bounds.left >= .5 else "not established"
        report["usage"] = {"input_tokens": usage.input_tokens, "output_tokens": usage.output_tokens}
        report["estimated_usd"] = budget.reconcile(reservation, usage.input_tokens, usage.output_tokens) if usage.known else budget.RESERVATION_USD
        report["usage_reported"] = usage.known
        # Local malformed-output rejection and cancellation do not prove provider-side behavior.
        report["tests"]["provider_safety_refusal"] = "not run"
        report["tests"]["credential_refresh"] = "not run; requires runtime test spanning token expiry"
        report["tests"]["provider_cancellation"] = "not run"
    except Exception as exc:
        report["tests"]["authentication_image_and_structured_output"] = "failed; inspect configuration without logging raw requests"
        report["error_type"] = type(exc).__name__
        report["diagnostics"] = diagnostics
    finally:
        await provider.close()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if report["tests"]["authentication_image_and_structured_output"] == "passed" else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--allow-live", action="store_true", help="authorizes a billable synthetic GCP request")
    parser.add_argument("--project", required=True)
    parser.add_argument("--location", default="global")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/model-probe.json")
    parser.add_argument("--budget-ledger", type=Path, default=ROOT / "artifacts/live-validation-budget.json")
    arguments = parser.parse_args()
    if not arguments.allow_live:
        parser.error("live inference requires --allow-live and the explicitly approved project")
    raise SystemExit(asyncio.run(probe(arguments.project, arguments.location, arguments.output,
        LiveBudget(arguments.budget_ledger, arguments.project))))
