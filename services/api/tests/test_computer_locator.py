from types import SimpleNamespace as NS
from uuid import uuid4

import pytest
from app.computer_locator import location_from_response, locator_config
from app.config import Settings
from app.providers import GeminiProvider


def response(name="click", **args):
    return NS(candidates=[NS(content=NS(parts=[NS(function_call=NS(name=name, args=args))]))])


def observation():
    return NS(id=uuid4(), geometry_id=uuid4())


@pytest.mark.parametrize("x,y", [(0, 0), (999, 999), (520.0, 130.0)])
def test_native_coordinates_bind_to_exact_image_without_inventing_label_or_box(x, y):
    obs = observation()
    result = location_from_response(response(x=x, y=y), obs)
    assert result.source_observation_id == obs.id
    assert result.target.geometry_id == obs.geometry_id
    assert result.target.bounds.left == result.target.bounds.right == x / 999
    assert result.target.bounds.top == result.target.bounds.bottom == y / 999
    assert result.target.visible_label == ""
    assert all(e.observation_id == obs.id for e in result.evidence)


@pytest.mark.parametrize("x", [True, -1, 1000, 1.3, float("nan"), "400", None])
def test_invalid_native_coordinates_cannot_become_targets(x):
    assert location_from_response(response(x=x, y=20), observation()).target is None


def test_confirmation_batches_keyboard_and_text_responses_never_execute_or_become_targets():
    assert location_from_response(response(x=100, y=200, safety_decision={"decision": "require_confirmation"}), observation()).target is None
    assert location_from_response(response("press_key", key="enter"), observation()).target is None
    batch = response(x=100, y=200)
    batch.candidates *= 2
    assert location_from_response(batch, observation()).target is None
    assert location_from_response(NS(candidates=[]), observation()).target is None


def test_dedicated_tool_uses_desktop_low_thinking_with_sdk_execution_disabled():
    config = locator_config()
    assert config.tools[0].computer_use.environment.name == "ENVIRONMENT_DESKTOP"
    assert config.tools[0].computer_use.enable_prompt_injection_detection
    assert config.automatic_function_calling.disable
    assert config.thinking_config.thinking_level.name == "LOW"
    assert config.max_output_tokens == 1024 and config.candidate_count == 1


@pytest.mark.asyncio
async def test_locator_routes_one_call_without_json_schema_or_executable_action():
    provider = object.__new__(GeminiProvider)
    provider.settings = Settings(gemini_computer_locator_enabled=True, _env_file=None)
    calls = []

    async def generate(**kwargs):
        calls.append(kwargs)
        result = response(x=800, y=150)
        result.usage_metadata = NS(prompt_token_count=120, candidates_token_count=25, thoughts_token_count=5)
        return result

    provider.client = NS(aio=NS(models=NS(generate_content=generate)))
    result, usage = await provider.locate("Create a project", NS(instruction="Click New Project", expected_user_action="click"), observation(), b"\x89PNGfixture")
    assert len(calls) == 1 and calls[0]["model"] == "gemini-3.5-flash-lite"
    assert calls[0]["config"].response_schema is None
    assert result.target is not None and not hasattr(result, "action_proposal")
    assert usage.input_tokens == 120 and usage.output_tokens == 30 and usage.thinking_tokens == 5
