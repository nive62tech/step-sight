from uuid import uuid4

import pytest
from app.config import Settings
from app.contracts import Evidence, Target, TargetLocation
from app.main import create_app
from app.providers import MockProvider, Usage
from conftest import create_session, observation, send_turn
from fastapi.testclient import TestClient


class Locator(MockProvider):
    def __init__(self, invalid=None):
        self.invalid = invalid
        self.calls = 0

    async def locate(self, goal, step, obs, image, *, repair=False):
        self.calls += 1
        return TargetLocation(
            source_observation_id=uuid4() if self.invalid == "observation" else obs.id,
            target=Target(kind="rectangle", coordinate_space="normalized_source_image",
                          geometry_id=uuid4() if self.invalid == "geometry" else obs.geometry_id,
                          bounds={"left": .1, "top": .2, "right": .3, "bottom": .4}, visible_label="Synthetic target"),
            evidence=[Evidence(observation_id=uuid4() if self.invalid == "evidence" else obs.id,
                               detail="Synthetic target is visible")]), Usage(100, 20)


def test_target_search_preserves_current_task_and_never_confirms_or_authorizes_input():
    provider = Locator()
    app = create_app(Settings(app_env="test", database_url="sqlite+pysqlite:///:memory:", _env_file=None), provider)
    with TestClient(app) as client:
        session = create_session(client)
        metadata, image = observation(session)
        first = send_turn(client, session, metadata, image).json()
        session["session_version"] = first["session_version"]
        metadata, image = observation(session, event_kind="locate", previous_step=first["step"])
        response = send_turn(client, session, metadata, image)
        assert response.status_code == 200, response.text
        found = response.json()
        for field in ("id", "instruction", "explanation", "expected_user_action", "postconditions", "hints"):
            assert found["step"][field] == first["step"][field]
        assert found["step"]["target"]["geometry_id"] == metadata["observation"]["geometry_id"]
        assert found["step"]["source_observation_id"] == metadata["observation"]["id"]
        assert found["verification"] is None and found["action_proposal"] is None and not found["completed"]
        state = client.get(f"/v1/sessions/{session['id']}").json()
        assert state["completed_steps"] == 0 and state["session_version"] == 2
        # Idempotent retries do not call the model twice.
        assert send_turn(client, session, metadata, image).json() == found
        assert provider.calls == 1


@pytest.mark.parametrize("invalid", ["observation", "geometry", "evidence"])
def test_target_search_rejects_old_image_binding_without_advancing(invalid):
    app = create_app(Settings(app_env="test", database_url="sqlite+pysqlite:///:memory:", _env_file=None), Locator(invalid))
    with TestClient(app) as client:
        session = create_session(client)
        metadata, image = observation(session)
        first = send_turn(client, session, metadata, image).json()
        session["session_version"] = first["session_version"]
        metadata, image = observation(session, event_kind="locate", previous_step=first["step"])
        assert send_turn(client, session, metadata, image).status_code == 502
        state = client.get(f"/v1/sessions/{session['id']}").json()
        assert state["session_version"] == 1 and state["completed_steps"] == 0


def test_target_search_requires_a_current_instruction(client):
    session = create_session(client)
    metadata, image = observation(session, event_kind="locate")
    assert send_turn(client, session, metadata, image).status_code == 409
