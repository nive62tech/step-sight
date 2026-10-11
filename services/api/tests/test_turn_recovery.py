from concurrent.futures import ThreadPoolExecutor
from uuid import uuid4

from app.config import Settings
from app.main import create_app
from conftest import create_session, observation, send_turn
from fastapi.testclient import TestClient
from test_failures import BlockingProvider


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
