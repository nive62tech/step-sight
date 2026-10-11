"""Computer-use coordinates as read-only guide hints, never executable actions.

Adapted from the local Autobots integration: dedicated desktop tool, LOW thinking,
one proposed point, 0..999 normalization and SDK automatic execution disabled.
"""
import math

from google.genai import types

from app.contracts import Bounds, Evidence, Target, TargetLocation


def locator_config():
    return types.GenerateContentConfig(
        tools=[types.Tool(computer_use=types.ComputerUse(
            environment=types.Environment.ENVIRONMENT_DESKTOP,
            excluded_predefined_functions=["key_down", "key_up", "mouse_down", "mouse_up", "take_screenshot"],
            enable_prompt_injection_detection=True))],
        automatic_function_calling=types.AutomaticFunctionCallingConfig(disable=True),
        thinking_config=types.ThinkingConfig(thinking_level=types.ThinkingLevel.LOW),
        max_output_tokens=1024, candidate_count=1,
    )


def location_from_response(response, observation):
    def missing(reason):
        return TargetLocation(source_observation_id=observation.id, target=None,
            evidence=[Evidence(observation_id=observation.id, detail="The locator did not establish a current clickable target.")],
            uncertainty=[reason])

    calls = [part.function_call for candidate in response.candidates or []
             for part in (candidate.content.parts if candidate.content and candidate.content.parts else [])
             if part.function_call is not None]
    if len(calls) != 1:
        return missing("No single point was identified. Recheck the current screen.")
    call = calls[0]
    args = call.args or {}
    if not isinstance(args, dict):
        return missing("The locator returned invalid coordinates.")
    safety = args.get("safety_decision")
    if isinstance(safety, dict) and safety.get("decision") == "require_confirmation":
        return missing("The provider requires the learner to handle this step manually; no target is shown.")
    if call.name not in {"click", "left_click", "click_at", "move", "hover", "hover_at", "mouse_move"}:
        return missing("The current instruction does not have an immediately visible clickable target.")
    values = [args.get("x"), args.get("y")]
    if not all(isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)
               and 0 <= v <= 999 and float(v).is_integer() for v in values):
        return missing("The locator returned invalid coordinates.")
    x, y = (value / 999 for value in values)
    # A point has no inferred button dimensions or guessed label. The desktop can
    # expand it to the live accessible control containing it. Do not invent a box.
    target = Target(kind="rectangle", coordinate_space="normalized_source_image", geometry_id=observation.geometry_id,
                    bounds=Bounds(left=x, top=y, right=x, bottom=y), visible_label="")
    return TargetLocation(source_observation_id=observation.id, target=target,
        evidence=[Evidence(observation_id=observation.id, detail="The computer-use locator proposed this point in the current image; no action was executed.")])
