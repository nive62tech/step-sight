import json
from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from app.config import Settings
from app.contracts import Bounds, VerificationResult
from app.persistence import ModelCall, Session, SessionEvent, Turn
from conftest import create_session, observation, send_turn
from sqlalchemy import select


def test_mock_never_fabricates_grounding_or_success(client):
    session = create_session(client)
    metadata, image = observation(session)
    first = send_turn(client, session, metadata, image)
    assert first.status_code == 200, first.text
    result = first.json()
    assert result["step"]["target"] is None and result["provider"] == "mock"
    session["session_version"] = result["session_version"]
    metadata, image = observation(session, previous_step=result["step"], event_kind="i_did_this")
    second = send_turn(client, session, metadata, image)
    assert second.status_code == 200, second.text
    assert second.json()["verification"]["status"] == "UNCERTAIN"
    assert second.json()["step"] is None and not second.json()["completed"]


def test_owner_isolation_and_device_binding(client):
    session = create_session(client, headers={"x-dev-user": "alice"})
    for method, suffix in [("get", ""), ("delete", ""), ("post", "/stop")]:
        kwargs = {"headers": {"x-dev-user": "bob"}}
        if method == "post":
            kwargs["json"] = {"session_version": 0}
        response = getattr(client, method)(f"/v1/sessions/{session['id']}{suffix}", **kwargs)
        assert response.status_code == 404
    response = client.post("/v1/sessions", headers={"x-dev-user": "bob"}, json={
        "device_id": session["device_id"], "goal": "test", "scope_id": str(uuid4()),
    })
    assert response.status_code == 409


def test_idempotency_replay_and_payload_collision(client):
    session = create_session(client)
    metadata, image = observation(session)
    first = send_turn(client, session, metadata, image)
    assert first.status_code == 200
    assert send_turn(client, session, metadata, image).json() == first.json()
    metadata["event_kind"] = "activity"
    assert send_turn(client, session, metadata, image).status_code == 409


@pytest.mark.parametrize("change", ["scope", "stale", "future", "version", "dimensions", "schema"])
def test_incompatible_observations(client, change):
    session = create_session(client)
    metadata, image = observation(session)
    if change == "scope":
        metadata["observation"]["scope_id"] = str(uuid4())
    elif change in {"stale", "future"}:
        metadata["observation"]["captured_at"] = (datetime.now(UTC) + timedelta(seconds=120 if change == "future" else -120)).isoformat()
    elif change == "version":
        metadata["session_version"] = 9
    elif change == "dimensions":
        metadata["observation"]["image_width"] = 65
    else:
        metadata["schema_version"] = 99
    assert send_turn(client, session, metadata, image).status_code in {400, 409, 422}


def test_stop_cannot_resume_and_rejects_old_turn(client):
    session = create_session(client)
    metadata, image = observation(session)
    stopped = client.post(f"/v1/sessions/{session['id']}/stop", json={"session_version": 0})
    assert stopped.status_code == 200
    assert send_turn(client, session, metadata, image).status_code == 409
    assert client.post(f"/v1/sessions/{session['id']}/resume", json={"session_version": 1}).status_code == 409


def test_events_deduplicate_and_reject_stale_sequence(client):
    session = create_session(client)
    event = {"id": str(uuid4()), "sequence": 1, "local_epoch": 1, "session_version": 0, "kind": "attempt"}
    url = f"/v1/sessions/{session['id']}/events"
    assert client.post(url, json=event).status_code == 200
    assert client.post(url, json=event).json()["duplicate"]
    event["kind"] = "skipped"
    assert client.post(url, json=event).status_code == 409
    event["id"] = str(uuid4())
    assert client.post(url, json=event).status_code == 409


def test_no_raw_media_or_unsaved_goal_in_database(client, app):
    session = create_session(client, goal="private test goal")
    metadata, image = observation(session)
    assert send_turn(client, session, metadata, image).status_code == 200
    with app.state.db_factory() as db:
        row = db.get(Session, session["id"])
        assert row.goal == "Unsaved lesson" and row.pending_step is None
        rows = [row.__dict__, *[r.__dict__ for r in db.scalars(select(Turn)).all()]]
        text = json.dumps(rows, default=str)
        assert "private test goal" not in text and "Mock mode: screen" not in text
    assert image not in text.encode()


def test_delete_retains_spend_but_removes_lesson(client, app):
    session = create_session(client)
    metadata, image = observation(session)
    assert send_turn(client, session, metadata, image).status_code == 200
    assert client.delete(f"/v1/sessions/{session['id']}").status_code == 204
    with app.state.db_factory() as db:
        assert db.get(Session, session["id"]) is None
        assert db.scalars(select(Turn)).all() == []
        assert db.scalars(select(SessionEvent)).all() == []
        assert db.scalars(select(ModelCall)).first().turn_id is None


def test_validation_does_not_echo_sensitive_request(client):
    response = client.post("/v1/sessions", json={"goal": "secret-example", "schema_version": 99})
    assert response.status_code == 422 and "secret-example" not in response.text


def test_upload_limits_and_bad_media(client):
    session = create_session(client)
    metadata, _ = observation(session)
    assert send_turn(client, session, metadata, b"not an image").status_code == 400
    assert send_turn(client, session, metadata, b"x" * (5 * 1024 * 1024 + 1)).status_code == 413


def test_production_refuses_insecure_modes():
    with pytest.raises(ValueError):
        Settings(app_env="production", _env_file=None)
    with pytest.raises(ValueError):
        Settings(raw_media_persistence=True, _env_file=None)


def test_no_clamping_or_evidenceless_confirmation():
    for values in [{"left": -0.1}, {"left": float("nan")}, {"right": 1.1}, {"left": 0.9, "right": 0.1}]:
        with pytest.raises(ValueError):
            Bounds(**({"left": 0, "top": 0, "right": 1, "bottom": 1} | values))
    with pytest.raises(ValueError):
        VerificationResult(status="CONFIRMED", provenance="gemini")
