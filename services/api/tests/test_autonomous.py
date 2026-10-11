from uuid import uuid4

import pytest
from app.config import Settings
from app.contracts import (
    ActionProposal,
    Evidence,
    GuidanceStep,
    ProviderResult,
    Target,
    TargetLocation,
    VerificationResult,
)
from app.main import create_app
from app.persistence import ModelCall
from app.providers import MockProvider, Usage
from conftest import create_session, observation, send_turn
from fastapi.testclient import TestClient
from sqlalchemy import select


class AutonomousProvider(MockProvider):
    def __init__(self, issue=None, status="CONFIRMED"):
        self.issue, self.status = issue, status
        self.locates = 0
        self.modes = []

    async def guide(self, goal, lesson, turn, image, *, repair=False):
        self.modes.append(lesson["interaction_mode"])
        obs = turn.observation
        step = GuidanceStep(id=uuid4(), source_observation_id=obs.id, instruction="Open preferences",
            explanation="Change the display", expected_user_action="click",
            target=Target(geometry_id=obs.geometry_id, bounds={"left": .1, "top": .1, "right": .2, "bottom": .2}, visible_label="Preferences"),
            postconditions=[{"kind": "visible_text", "value": "Preferences"}],
            evidence=[Evidence(observation_id=obs.id, detail="Preferences is visible")])
        proposal = ActionProposal(id=uuid4(), source_observation_id=obs.id, geometry_id=obs.geometry_id,
            action={"kind": "click", "x": .15, "y": .15}, effect="Open preferences", risk="low")
        if self.issue == "manual":
            proposal.risk = "manual"
        if self.issue == "uncertain":
            step.uncertainty = ["Target is covered"]
        verification = VerificationResult(status=self.status, evidence=step.evidence, provenance="mock") if turn.previous_step else None
        return ProviderResult(step=step, action_proposal=proposal, verification=verification), Usage(100, 20)

    async def locate(self, goal, step, obs, image, *, repair=False):
        self.locates += 1
        return TargetLocation(source_observation_id=uuid4() if self.issue == "stale" else obs.id,
            target=None if self.issue == "missing" else Target(geometry_id=obs.geometry_id,
                bounds={"left": .7, "top": .6, "right": .7, "bottom": .6}, visible_label=""),
            evidence=[Evidence(observation_id=obs.id, detail="Synthetic coordinate tool point")]), Usage(200, 30)


def app_for(provider, enabled=True):
    return create_app(Settings(app_env="test", database_url="sqlite+pysqlite:///:memory:",
        gemini_computer_locator_enabled=enabled, _env_file=None), provider)


def test_autonomous_click_is_replaced_by_current_computer_use_point_and_accounted_once():
    provider = AutonomousProvider()
    app = app_for(provider)
    with TestClient(app) as client:
        session = create_session(client, mode="autonomous", lesson_id="browser")
        metadata, image = observation(session)
        response = send_turn(client, session, metadata, image)
        assert response.status_code == 200, response.text
        result = response.json()
        assert result["action_proposal"]["action"] == {"kind": "click", "x": .7, "y": .6, "button": "left"}
        assert result["step"]["target"]["geometry_id"] == metadata["observation"]["geometry_id"]
        assert send_turn(client, session, metadata, image).json() == result
        assert provider.locates == 1 and provider.modes == ["autonomous"]
        with app.state.db_factory() as db:
            calls = db.scalars(select(ModelCall)).all()
            assert len(calls) == 2 and all(c.status == "completed" for c in calls)
            assert sum(c.input_tokens for c in calls) == 300


@pytest.mark.parametrize("issue,enabled,expected_calls", [("manual", True, 0), ("uncertain", True, 0), ("missing", True, 1), (None, False, 0)])
def test_autonomous_click_without_safe_grounding_stays_disarmed(issue, enabled, expected_calls):
    provider = AutonomousProvider(issue)
    with TestClient(app_for(provider, enabled)) as client:
        session = create_session(client, mode="autonomous")
        metadata, image = observation(session)
        response = send_turn(client, session, metadata, image)
        assert response.status_code == 200, response.text
        assert response.json()["action_proposal"] is None
        assert provider.locates == expected_calls


def test_stale_coordinate_tool_output_is_rejected_without_progress():
    with TestClient(app_for(AutonomousProvider("stale"))) as client:
        session = create_session(client, mode="autonomous")
        metadata, image = observation(session)
        assert send_turn(client, session, metadata, image).status_code == 502
        assert client.get(f"/v1/sessions/{session['id']}").json()["completed_steps"] == 0


@pytest.mark.parametrize("status", ["NOT_YET", "DIFFERENT_STATE", "UNCERTAIN"])
def test_autonomous_recovery_never_counts_unconfirmed_progress(status):
    with TestClient(app_for(AutonomousProvider(status=status))) as client:
        session = create_session(client, mode="autonomous")
        metadata, image = observation(session)
        first = send_turn(client, session, metadata, image).json()
        session["session_version"] = first["session_version"]
        metadata, image = observation(session, event_kind="autonomous_result", previous_step=first["step"])
        response = send_turn(client, session, metadata, image)
        assert response.status_code == 200, response.text
        result = response.json()
        assert (result["action_proposal"] is None) == (status == "UNCERTAIN")
        assert not result["completed"]
        assert client.get(f"/v1/sessions/{session['id']}").json()["completed_steps"] == 0


def test_guided_session_cannot_post_autonomous_results(client):
    session = create_session(client)
    metadata, image = observation(session, event_kind="autonomous_result")
    assert send_turn(client, session, metadata, image).status_code == 409


def test_banner_recovery_can_scroll_inside_panel_without_claiming_hidden_click_succeeded():
    class BannerProvider(AutonomousProvider):
        async def guide(self, goal, lesson, turn, image, *, repair=False):
            result, usage = await super().guide(goal, lesson, turn, image, repair=repair)
            if turn.event_kind == "recover":
                assert turn.previous_step.uncertainty  # Locator failure reaches the next visual decision.
                result.step.instruction = "Scroll down within the information banner"
                result.step.expected_user_action = "scroll"
                result.action_proposal = ActionProposal(id=uuid4(), source_observation_id=turn.observation.id,
                    geometry_id=turn.observation.geometry_id, action={"kind": "scroll", "x": .15, "y": .15, "ticks": -2},
                    effect="Reveal the bottom of the visible banner", risk="low")
            return result, usage

    provider = BannerProvider(issue="missing", status="NOT_YET")
    with TestClient(app_for(provider)) as client:
        session = create_session(client, mode="autonomous", lesson_id="browser")
        metadata, image = observation(session)
        first = send_turn(client, session, metadata, image).json()
        assert first["action_proposal"] is None
        session["session_version"] = first["session_version"]
        metadata, image = observation(session, event_kind="recover", previous_step=first["step"])
        response = send_turn(client, session, metadata, image)
        assert response.status_code == 200, response.text
        assert response.json()["action_proposal"]["action"]["kind"] == "scroll"
        assert response.json()["verification"]["status"] == "NOT_YET"
        assert provider.locates == 1
        assert client.get(f"/v1/sessions/{session['id']}").json()["completed_steps"] == 0
