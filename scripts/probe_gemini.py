"""Explicit opt-in synthetic-media capability probe. No real desktop capture or input."""
import argparse
import asyncio
import importlib.metadata
import io
import json
import sys
import time
from datetime import UTC, datetime
from pathlib import Path
from urllib.parse import urlsplit
from uuid import uuid4

from google.genai import errors
from live_budget import LiveBudget
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "services/api"))
from app.config import Settings  # noqa: E402
from app.contracts import GuidanceStep, Observation, TurnRequest  # noqa: E402
from app.providers import GeminiProvider, thinking_config  # noqa: E402


async def probe_intent(project: str, location: str, output: Path, budget: LiveBudget):
    """One synthetic text-only request, no browser launch or screen access."""
    settings = Settings(model_provider="gemini", gcp_project_id=project, gcp_location=location, _env_file=None)
    provider = GeminiProvider(settings)
    report = {"date": datetime.now(UTC).isoformat(), "project": project, "model": settings.gemini_guidance_model,
              "synthetic_only": True, "browser_opened": False, "tests": {}}
    try:
        reservation = budget.reserve("synthetic browser intent planning")
        started = time.monotonic()
        result, usage = await provider.plan_browser("Open Chrome and guide me to book a train ticket on irtc")
        report["elapsed_ms"] = round((time.monotonic() - started) * 1000)
        report["usage"] = {"input_tokens": usage.input_tokens, "output_tokens": usage.output_tokens, "thinking_tokens": usage.thinking_tokens}
        report["estimated_usd"] = budget.reconcile(reservation, usage.input_tokens, usage.output_tokens) if usage.known else budget.RESERVATION_USD
        host = urlsplit(result.address or "").hostname
        report["decision"] = result.decision
        report["browser"] = result.browser
        report["destination_host"] = host
        report["tests"]["intent_typo_to_official_site"] = "passed" if (result.decision == "open_browser"
            and result.browser == "chrome" and host in {"irctc.co.in", "www.irctc.co.in"}) else "failed"
    except Exception as exc:
        report["tests"]["intent_typo_to_official_site"] = "failed"
        report["error_type"] = type(exc).__name__
    finally:
        await provider.close()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if report["tests"]["intent_typo_to_official_site"] == "passed" else 1


async def probe(project: str, location: str, output: Path, budget: LiveBudget, locate: bool = False, browser_controls: bool = False):
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
    if browser_controls:
        # Synthetic browser-like layout with distractors; no real page or learner data.
        image = Image.new("RGB", (1600, 900), "#111827")
        draw = ImageDraw.Draw(image)
        font = ImageFont.load_default(size=22)
        for rect, fill, text in [
            ((0, 0, 1600, 70), "#263244", "Synthetic browser | demo.invalid/dashboard"),
            ((30, 120, 260, 180), "#334155", "Workspace settings"),
            ((1260, 120, 1510, 180), "#16785d", "New Project"),
            ((30, 260, 750, 500), "#263244", "Existing project: Atlas"),
            ((70, 380, 280, 440), "#334155", "Deploy"),
            ((850, 260, 1500, 500), "#263244", "Project templates"),
            ((900, 380, 1220, 440), "#334155", "View templates"),
        ]:
            draw.rectangle(rect, fill=fill)
            draw.text((rect[0] + 16, rect[1] + 18), text, fill="white", font=font)
    stream = io.BytesIO()
    image.save(stream, "PNG")
    observation = Observation(id=uuid4(), captured_at=datetime.now(UTC), scope_id=uuid4(), geometry_id=uuid4(), image_width=image.width, image_height=image.height, privacy_filtered=True)
    report = {"date": datetime.now(UTC).isoformat(), "project": project, "location": location,
              "model": settings.gemini_locator_model if locate and settings.gemini_computer_locator_enabled else settings.gemini_guidance_model,
              "locator_mode": "computer_use_proposal_only" if locate and settings.gemini_computer_locator_enabled else "structured_image",
              "sdk": importlib.metadata.version("google-genai"),
              "thinking": thinking_config(settings.gemini_locator_model, "LOW").model_dump(exclude_none=True) if locate and settings.gemini_computer_locator_enabled else thinking_config(settings.gemini_guidance_model, settings.gemini_thinking_level).model_dump(exclude_none=True),
              "synthetic_media_only": True,
              "native_computer_use": "proposal_only_no_execution" if locate and settings.gemini_computer_locator_enabled else "disabled", "tests": {}}
    try:
        reservation = budget.reserve("target locator probe" if locate else "image and structured output capability probe")
        started = time.monotonic()
        if browser_controls:
            if locate:
                step = GuidanceStep(id=uuid4(), source_observation_id=observation.id, instruction="Click New Project to begin a new empty project",
                    explanation="Open project creation", expected_user_action="click", postconditions=[{"kind": "visible_text", "value": "Create project"}],
                    evidence=[{"observation_id": observation.id, "detail": "Synthetic fixture"}])
                location, usage = await provider.locate("Create a new empty project", step, observation, stream.getvalue())
                target = location.target
                bound = location.source_observation_id == observation.id and all(e.observation_id == observation.id for e in location.evidence)
                label_valid = True  # Native point proposals deliberately have no inferred label.
            else:
                result, usage = await provider.guide("Help me create a new empty project. I will do the clicks.", {},
                    TurnRequest(request_id=uuid4(), session_version=0, observation=observation, event_kind="start"), stream.getvalue())
                target = result.step.target if result.step else None
                bound = result.step is not None and result.step.source_observation_id == observation.id and not result.completed and all(e.observation_id == observation.id for e in result.step.evidence)
                label_valid = target is not None and target.visible_label == "New Project"
            valid = (bound and label_valid
                     and target is not None and target.geometry_id == observation.geometry_id
                     and 1260 / 1600 <= (target.bounds.left + target.bounds.right) / 2 <= 1510 / 1600
                     and 120 / 900 <= (target.bounds.top + target.bounds.bottom) / 2 <= 180 / 900)
            report["tests"]["browser_distractors_exact_label_and_current_target"] = "passed" if valid else "failed"
        elif locate:
            step = GuidanceStep(id=uuid4(), source_observation_id=observation.id,
                instruction="Click Save practice settings", explanation="Save the synthetic practice values",
                expected_user_action="click", postconditions=[{"kind": "visible_text", "value": "Saved"}],
                evidence=[{"observation_id": observation.id, "detail": "Synthetic fixture"}])
            result, usage = await provider.locate("Save practice settings", step, observation, stream.getvalue())
            target = result.target
            valid = (result.source_observation_id == observation.id and target is not None
                     and target.geometry_id == observation.geometry_id
                     and all(e.observation_id == observation.id for e in result.evidence)
                     and .65 <= (target.bounds.left + target.bounds.right) / 2 <= .9375
                     and .733 <= (target.bounds.top + target.bounds.bottom) / 2 <= .912)
            report["tests"]["locator_fresh_image_binding_and_button_center"] = "passed" if valid else "failed"
        else:
            result, usage = await provider.guide("Show where to save practice settings", {"milestones": ["Save practice settings"]},
                                                TurnRequest(request_id=uuid4(), session_version=0, observation=observation, event_kind="start"), stream.getvalue())
            target = result.step.target if result.step else None
            valid = (result.step is not None and result.step.source_observation_id == observation.id
                     and not result.completed and target is not None
                     and target.geometry_id == observation.geometry_id
                     and bool(result.step.evidence)
                     and all(e.observation_id == observation.id for e in result.step.evidence)
                     and .65 <= (target.bounds.left + target.bounds.right) / 2 <= .9375
                     and .733 <= (target.bounds.top + target.bounds.bottom) / 2 <= .912)
            report["tests"]["guidance_fresh_image_binding_and_button_center"] = "passed" if valid else "failed"
        report["elapsed_ms"] = round((time.monotonic() - started) * 1000)
        report["tests"]["authentication_image_and_structured_output"] = "passed"
        report["tests"]["grounding"] = "passed" if target and target.bounds.left >= .5 else "not established"
        report["usage"] = {"input_tokens": usage.input_tokens, "output_tokens": usage.output_tokens,
                           "thinking_tokens": usage.thinking_tokens}
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
    return 0 if report["tests"]["authentication_image_and_structured_output"] == "passed" and "failed" not in report["tests"].values() else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--allow-live", action="store_true", help="authorizes a billable synthetic GCP request")
    parser.add_argument("--project", required=True)
    parser.add_argument("--location", default="global")
    parser.add_argument("--locate", action="store_true", help="probe current-step target location on a synthetic image")
    parser.add_argument("--browser-controls", action="store_true", help="probe exact control labels on a synthetic browser layout with distractors")
    parser.add_argument("--plan-intent", action="store_true", help="probe one synthetic text goal without launching a browser")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/model-probe.json")
    parser.add_argument("--budget-ledger", type=Path, default=ROOT / "artifacts/live-validation-budget.json")
    arguments = parser.parse_args()
    if not arguments.allow_live:
        parser.error("live inference requires --allow-live and the explicitly approved project")
    if arguments.plan_intent:
        if arguments.locate or arguments.browser_controls:
            parser.error("intent planning cannot be combined with image probes")
        raise SystemExit(asyncio.run(probe_intent(arguments.project, arguments.location, arguments.output,
            LiveBudget(arguments.budget_ledger, arguments.project))))
    raise SystemExit(asyncio.run(probe(arguments.project, arguments.location, arguments.output,
        LiveBudget(arguments.budget_ledger, arguments.project), arguments.locate, arguments.browser_controls)))
