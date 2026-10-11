import time
from concurrent.futures import ThreadPoolExecutor
from uuid import uuid4

import pytest
from app.config import Settings
from app.contracts import ActionProposal
from app.main import create_app
from conftest import create_session, observation, send_turn
from fastapi.testclient import TestClient
from test_autonomous import AutonomousProvider, app_for
from test_failures import BlockingProvider


@pytest.mark.parametrize("mode", ["guide", "autonomous"])
def test_resume_replans_past_old_english_manual_step_without_claiming_progress(mode):
    class EnglishBannerProvider(AutonomousProvider):
        async def guide(self, goal, lesson, turn, image, *, repair=False):
            result, usage = await super().guide(goal, lesson, turn, image, repair=repair)
            if turn.event_kind == "resume":
                assert turn.previous_step is None
                assert lesson["interaction_mode"] == mode
                assert lesson["resume_context"]["earlier_instruction"] == "Click English yourself"
                assert not lesson["resume_context"]["progress_claimed"]
                result.step.instruction = "Scroll down inside the notice to see the next control"
                result.step.expected_user_action = "scroll"
                result.action_proposal = ActionProposal(id=uuid4(), source_observation_id=turn.observation.id,
                    geometry_id=turn.observation.geometry_id, action={"kind": "scroll", "x": .5, "y": .5, "ticks": -2},
                    effect="Reveal the bottom of the notice", risk="low")
            else:
                result.step.instruction = "Click English yourself"
                result.step.expected_user_action = "manual"
                result.action_proposal = None
            return result, usage

    with TestClient(app_for(EnglishBannerProvider(status="UNCERTAIN"))) as client:
        session = create_session(client, mode=mode, lesson_id="browser")
        metadata, image = observation(session)
        first = send_turn(client, session, metadata, image).json()
        url = f"/v1/sessions/{session['id']}"
        paused = client.post(url + "/pause", json={"session_version": first["session_version"]}).json()
        resumed = client.post(url + "/resume", json={"session_version": paused["session_version"]}).json()
        metadata, image = observation(resumed, event_kind="resume", previous_step=resumed["pending_step"])
        response = send_turn(client, resumed, metadata, image)
        assert response.status_code == 200, response.text
        result = response.json()
        assert result["verification"] is None and not result["completed"]
        assert result["step"]["expected_user_action"] == "scroll"
        assert result["action_proposal"]["action"]["kind"] == "scroll"
        latest = client.get(url).json()
        assert latest["completed_steps"] == 0 and latest["mode"] == mode
        assert latest["pending_step"] == result["step"]


def test_resume_waits_for_old_inference_and_returns_synchronized_context():
    provider = BlockingProvider()
    app = create_app(Settings(app_env="test", database_url="sqlite+pysqlite:///:memory:", _env_file=None), provider)
    with TestClient(app) as client, ThreadPoolExecutor(2) as pool:
        session = create_session(client)
        metadata, image = observation(session)
        pending = pool.submit(send_turn, client, session, metadata, image)
        try:
            assert provider.entered.wait(3)
            url = f"/v1/sessions/{session['id']}"
            paused = client.post(url + "/pause", json={"session_version": session["session_version"]}).json()
            resuming = pool.submit(client.post, url + "/resume", json={"session_version": paused["session_version"]})
            deadline = time.monotonic() + 3
            while client.get(url).json()["session_version"] == paused["session_version"] and time.monotonic() < deadline:
                time.sleep(.01)
            assert not resuming.done()  # No new turn can collide with the old provider call.
        finally:
            provider.release.set()
        assert pending.result(3).status_code == 409
        resumed = resuming.result(3)
        assert resumed.status_code == 200, resumed.text
        session = resumed.json()
        assert session["pending_step"] is None
        metadata, image = observation(session, event_kind="resume", previous_step=session["pending_step"])
        assert send_turn(client, session, metadata, image).status_code == 200


def test_recover_response_without_resubmitting_image_and_reject_after_stop(client):
    session = create_session(client, headers={"x-dev-user": "alice"})
    metadata, image = observation(session)
    headers = {"x-dev-user": "alice"}
    url = f"/v1/sessions/{session['id']}/turns/{metadata['request_id']}"
    assert client.get(url, headers=headers).status_code == 404
    first = send_turn(client, session, metadata, image, headers).json()
    assert client.get(url, headers=headers).json() == first
    assert client.get(url, headers={"x-dev-user": "bob"}).status_code == 404
    assert client.get(f"/v1/sessions/{session['id']}/turns/{uuid4()}", headers=headers).status_code == 404
    assert client.post(f"/v1/sessions/{session['id']}/stop", headers=headers,
                       json={"session_version": first["session_version"]}).status_code == 200
    assert client.get(url, headers=headers).status_code == 409


def test_pending_result_can_be_polled_without_starting_another_model_call():
    provider = BlockingProvider()
    app = create_app(Settings(app_env="test", database_url="sqlite+pysqlite:///:memory:", _env_file=None), provider)
    with TestClient(app) as client, ThreadPoolExecutor(1) as pool:
        session = create_session(client)
        metadata, image = observation(session)
        url = f"/v1/sessions/{session['id']}/turns/{metadata['request_id']}"
        pending = pool.submit(send_turn, client, session, metadata, image)
        try:
            assert provider.entered.wait(3)
            assert client.get(url).status_code == 202
        finally:
            provider.release.set()
        result = pending.result(3)
        assert client.get(url).json() == result.json()
