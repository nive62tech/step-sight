from datetime import UTC, datetime
from uuid import uuid4

import pytest
from app.contracts import Observation
from app.vision import VisualLocation, VisualResult
from pydantic import ValidationError


def observation():
    return Observation(id=uuid4(), captured_at=datetime.now(UTC), scope_id=uuid4(), geometry_id=uuid4(),
        image_width=800, image_height=450, privacy_filtered=True)


def prediction(**overrides):
    value = {"step": {"instruction": "Click Save", "explanation": "Save settings", "expected_user_action": "click",
        "target": {"bounds": {"left": .6, "top": .7, "right": .9, "bottom": .9}, "visible_label": "Save"},
        "postconditions": [{"kind": "visible_text", "value": "Saved"}], "evidence": ["Save is visible"]}}
    value.update(overrides)
    return value


def test_predictions_bind_only_to_the_call_image_and_keep_strict_wire_geometry():
    raw = VisualResult.model_validate(prediction())
    first, second = observation(), observation()
    a, b = raw.bind(first, has_previous=False), raw.bind(second, has_previous=False)
    assert a.step.source_observation_id == first.id and b.step.source_observation_id == second.id
    assert a.step.id != b.step.id
    assert a.step.target.geometry_id == first.geometry_id and b.step.target.geometry_id == second.geometry_id
    assert a.step.evidence[0].observation_id == first.id and b.step.evidence[0].observation_id == second.id
    assert a.step.target.bounds == b.step.target.bounds and not a.completed


def test_prediction_cannot_supply_an_old_observation_id():
    value = prediction()
    value["step"]["source_observation_id"] = str(uuid4())
    with pytest.raises(ValidationError):
        VisualResult.model_validate(value)


@pytest.mark.parametrize("status", ["NOT_YET", "UNCERTAIN", "DIFFERENT_STATE"])
def test_unverified_previous_step_never_advances_or_arms_a_proposal(status):
    raw = VisualResult.model_validate(prediction(completed=True,
        verification={"status": status, "evidence": ["Still shows unsaved changes"]},
        action_proposal={"action": {"kind": "click", "x": .7, "y": .8}, "effect": "Save", "risk": "low"}))
    obs = observation()
    result = raw.bind(obs, has_previous=True)
    assert result.step is None and result.action_proposal is None and not result.completed
    assert result.verification.status == status and result.verification.evidence[0].observation_id == obs.id


def test_missing_verification_and_false_initial_completion_are_rejected():
    with pytest.raises(ValueError, match="verification"):
        VisualResult.model_validate(prediction()).bind(observation(), has_previous=True)
    with pytest.raises(ValueError, match="completion"):
        VisualResult.model_validate(prediction(completed=True)).bind(observation(), has_previous=False)
    with pytest.raises(ValidationError):
        VisualResult.model_validate(prediction(verification={"status": "CONFIRMED", "evidence": []}))


def test_confirmed_completion_requires_current_evidence_and_no_unmet_conditions():
    raw = VisualResult(step=None, completed=True,
        verification={"status": "CONFIRMED", "evidence": ["Settings saved successfully"]})
    obs = observation()
    result = raw.bind(obs, has_previous=True)
    assert result.completed and result.verification.evidence[0].observation_id == obs.id
    with pytest.raises(ValidationError):
        VisualResult(step=None, verification={"status": "CONFIRMED", "evidence": ["Saved"], "unmet_postconditions": ["Still saving"]})


def test_locator_binds_only_geometry_and_evidence_without_creating_steps():
    obs = observation()
    located = VisualLocation(target=prediction()["step"]["target"], evidence=["Save is visible"]).bind(obs)
    assert located.source_observation_id == obs.id and located.target.geometry_id == obs.geometry_id
    assert located.evidence[0].observation_id == obs.id
