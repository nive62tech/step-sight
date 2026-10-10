import io
import json
from datetime import UTC, datetime
from uuid import uuid4

import pytest
from app.config import Settings
from app.main import create_app
from fastapi.testclient import TestClient
from PIL import Image


@pytest.fixture
def app():
    return create_app(Settings(app_env="test", database_url="sqlite+pysqlite:///:memory:", _env_file=None))


@pytest.fixture
def client(app):
    with TestClient(app) as client:
        yield client


def create_session(client, headers=None, **overrides):
    payload = {"device_id": str(uuid4()), "goal": "Configure a practice workspace", "scope_id": str(uuid4())}
    payload.update(overrides)
    response = client.post("/v1/sessions", json=payload, headers=headers)
    assert response.status_code == 201, response.text
    return response.json()


def observation(session, **overrides):
    metadata = {
        "schema_version": 1, "request_id": str(uuid4()), "session_version": session["session_version"],
        "event_kind": "start", "observation": {
            "id": str(uuid4()), "captured_at": datetime.now(UTC).isoformat(),
            "scope_id": session["scope_id"], "geometry_id": str(uuid4()),
            "image_width": 64, "image_height": 32, "privacy_filtered": True,
        },
    }
    metadata.update(overrides)
    out = io.BytesIO()
    Image.new("RGB", (64, 32), "white").save(out, "PNG")
    return metadata, out.getvalue()


def send_turn(client, session, metadata, image, headers=None):
    return client.post(f"/v1/sessions/{session['id']}/turns", files={
        "metadata": (None, json.dumps(metadata), "application/json"),
        "screenshot": ("frame.png", image, "image/png"),
    }, headers=headers)
