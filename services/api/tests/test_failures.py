import asyncio
import logging
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from datetime import UTC, datetime, timedelta
from uuid import uuid4

import jwt
import pytest
from app.auth import Authenticator
from app.config import Settings
from app.main import create_app
from app.persistence import ModelCall
from app.providers import MockProvider, ProviderUnavailable, Usage
from conftest import create_session, observation, send_turn
from cryptography.hazmat.primitives.asymmetric import rsa
from fastapi import HTTPException
from fastapi.testclient import TestClient
from sqlalchemy import select
from starlette.requests import Request


class ScriptedProvider(MockProvider):
    def __init__(self, failures=()):
        self.failures = list(failures)
        self.calls = []

    async def guide(self, *args, repair=False):
        self.calls.append(repair)
        if self.failures:
            raise self.failures.pop(0)
        return await super().guide(*args, repair=repair)


@pytest.mark.parametrize("failures,expected_calls,expected_status", [
    ([ProviderUnavailable("refused")], [False], 503),
    ([ProviderUnavailable("schema", schema_invalid=True)], [False, True], 200),
    ([ProviderUnavailable("schema", schema_invalid=True)] * 2, [False, True], 503),
    ([ProviderUnavailable("busy", retryable=True)] * 3, [False] * 3, 503),
])
def test_bounded_accounted_retries(failures, expected_calls, expected_status):
    provider = ScriptedProvider(failures)
    app = create_app(Settings(app_env="test", database_url="sqlite+pysqlite:///:memory:", _env_file=None), provider)
    with TestClient(app) as client:
        session = create_session(client)
        metadata, image = observation(session)
        assert send_turn(client, session, metadata, image).status_code == expected_status
        assert provider.calls == expected_calls
        with app.state.db_factory() as db:
            calls = db.scalars(select(ModelCall).order_by(ModelCall.attempt)).all()
            assert [c.attempt for c in calls] == list(range(len(expected_calls)))
            assert all(c.status in {"unknown", "completed"} for c in calls)


def test_failed_calls_cannot_exhaust_budget_then_retry_free():
    provider = ScriptedProvider([ProviderUnavailable("busy", retryable=True)] * 3)
    app = create_app(Settings(app_env="test", model_provider="gemini", gcp_project_id="synthetic-test-project",
        monthly_ai_budget_usd=.09, user_monthly_ai_budget_usd=.09,
        database_url="sqlite+pysqlite:///:memory:", _env_file=None), provider)
    with TestClient(app) as client:
        session = create_session(client)
        metadata, image = observation(session)
        assert send_turn(client, session, metadata, image).status_code == 429
        assert len(provider.calls) == 1
        with app.state.db_factory() as db:
            assert sum(c.cost_usd for c in db.scalars(select(ModelCall))) == .08


class BlockingProvider(MockProvider):
    def __init__(self):
        self.entered = threading.Event()
        self.release = threading.Event()

    async def guide(self, *args, **kwargs):
        self.entered.set()
        while not self.release.is_set():
            await asyncio.sleep(.01)
        return await super().guide(*args, **kwargs)


def test_success_without_usage_metadata_retains_its_spend_reservation():
    class MissingUsageProvider(MockProvider):
        async def guide(self, *args, **kwargs):
            result, _ = await super().guide(*args, **kwargs)
            return result, Usage(known=False)

    app = create_app(Settings(app_env="test", model_provider="gemini", gcp_project_id="synthetic-test-project",
        monthly_ai_budget_usd=.09, user_monthly_ai_budget_usd=.09,
        database_url="sqlite+pysqlite:///:memory:", _env_file=None), MissingUsageProvider())
    with TestClient(app) as client:
        session = create_session(client)
        metadata, image = observation(session)
        assert send_turn(client, session, metadata, image).status_code == 200
        with app.state.db_factory() as db:
            call = db.scalar(select(ModelCall))
            assert call.cost_usd == .08 and call.status == "completed_usage_unknown"


def test_one_inflight_and_stop_rejects_late_model_response():
    provider = BlockingProvider()
    app = create_app(Settings(app_env="test", database_url="sqlite+pysqlite:///:memory:", _env_file=None), provider)
    with TestClient(app) as client, ThreadPoolExecutor(1) as pool:
        session = create_session(client)
        metadata, image = observation(session)
        pending = pool.submit(send_turn, client, session, metadata, image)
        try:
            assert provider.entered.wait(3)
            assert send_turn(client, session, metadata, image).status_code == 409
            assert client.post(f"/v1/sessions/{session['id']}/stop", json={"session_version": 0}).status_code == 200
        finally:
            provider.release.set()
        assert pending.result(3).status_code == 409
        assert client.get(f"/v1/sessions/{session['id']}").json()["state"] == "STOPPED"


def test_screenshot_never_spools_or_appears_in_logs(client, monkeypatch, caplog):
    import tempfile

    def forbidden(*args, **kwargs):
        raise AssertionError("learner media must never spool")

    monkeypatch.setattr(tempfile, "SpooledTemporaryFile", forbidden)
    session = create_session(client, goal="privacy-canary-" + str(uuid4()))
    metadata, image = observation(session)
    with caplog.at_level(logging.DEBUG):
        assert send_turn(client, session, metadata, image).status_code == 200
    assert "privacy-canary" not in caplog.text and image.hex() not in caplog.text


@pytest.fixture
def signing_auth():
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    settings = Settings(app_env="test", auth_mode="cognito", cognito_issuer="https://cognito-idp.ap-south-1.amazonaws.com/ap-south-1_Test",
        cognito_client_id="test-client", cognito_scopes="desktop-guide/lessons", _env_file=None)
    auth = Authenticator(settings)
    auth.keys = {"test-key": key.public_key()}
    auth.expires = time.monotonic() + 100
    auth.last_fetch = time.monotonic()
    claims = {"iss": settings.cognito_issuer, "sub": "alice", "iat": datetime.now(UTC), "exp": datetime.now(UTC) + timedelta(minutes=5),
        "token_use": "access", "client_id": settings.cognito_client_id, "scope": settings.cognito_scopes}
    return auth, key, claims


def token_request(token):
    return Request({"type": "http", "headers": [(b"authorization", ("Bearer " + token).encode())], "client": ("127.0.0.1", 12345)})


async def test_signed_access_token(signing_auth):
    auth, key, claims = signing_auth
    token = jwt.encode(claims, key, algorithm="RS256", headers={"kid": "test-key"})
    assert (await auth.authenticate(token_request(token))).subject == "alice"


@pytest.mark.parametrize("change", ["expiry", "issuer", "client", "id-token", "scope", "signature", "kid", "algorithm"])
async def test_auth_rejects_invalid_binding(signing_auth, change):
    auth, key, claims = signing_auth
    if change == "expiry":
        claims["exp"] = datetime.now(UTC) - timedelta(minutes=1)
    elif change == "issuer":
        claims["iss"] = "https://example.invalid"
    elif change == "client":
        claims["client_id"] = "different-client"
    elif change == "id-token":
        claims["token_use"] = "id"
    elif change == "scope":
        claims["scope"] = "openid"
    elif change == "signature":
        key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    token = jwt.encode(claims, "synthetic-secret-padding-to-32-bytes" if change == "algorithm" else key,
        algorithm="HS256" if change == "algorithm" else "RS256", headers={"kid": "unknown" if change == "kid" else "test-key"})
    with pytest.raises(HTTPException) as error:
        await auth.authenticate(token_request(token))
    assert error.value.status_code == 401
