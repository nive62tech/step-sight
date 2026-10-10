import importlib.util
import json
from pathlib import Path

import pytest
from app.persistence import Session
from conftest import create_session, observation, send_turn
from fastapi import HTTPException


def test_failed_independent_ledger_write_preserves_deletable_session(client, monkeypatch):
    async def failure(*args):
        raise HTTPException(503, "ledger unavailable")

    monkeypatch.setattr("app.main.record_deletion", failure)
    session = create_session(client)
    assert client.delete(f"/v1/sessions/{session['id']}").status_code == 503
    assert client.get(f"/v1/sessions/{session['id']}").status_code == 200


def test_ledger_not_written_for_cross_owner_identifier(client, monkeypatch):
    async def forbidden(*args):
        pytest.fail("cross-owner deletion must not write a ledger event")

    monkeypatch.setattr("app.main.record_deletion", forbidden)
    session = create_session(client, headers={"x-dev-user": "alice"})
    assert client.delete(f"/v1/sessions/{session['id']}", headers={"x-dev-user": "bob"}).status_code == 404


def test_restore_reconciliation_removes_deleted_lesson_keeps_other_owner(tmp_path, monkeypatch):
    from app.config import Settings
    from app.main import create_app
    from fastapi.testclient import TestClient

    database = tmp_path / "restore-test.db"
    url = "sqlite+pysqlite:///" + str(database)
    app = create_app(Settings(app_env="test", database_url=url, _env_file=None))
    with TestClient(app) as client:
        deleted = create_session(client)
        preserved = create_session(client, headers={"x-dev-user": "bob"})
        metadata, image = observation(deleted)
        assert send_turn(client, deleted, metadata, image).status_code == 200
        ledger = tmp_path / "ledger"
        ledger.mkdir()
        (ledger / "entry.json").write_text(json.dumps({"schema_version": 1, "session_id": deleted["id"], "deleted_at": "2026-10-11T00:00:00Z"}), encoding="utf-8")
        module_path = Path(__file__).resolve().parents[3] / "scripts/reconcile-deletions.py"
        spec = importlib.util.spec_from_file_location("reconcile_deletions", module_path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        monkeypatch.setattr(module, "Settings", lambda: Settings(app_env="test", database_url=url, _env_file=None))
        module.reconcile(ledger)
        with app.state.db_factory() as db:
            assert db.get(Session, deleted["id"]) is None
            assert db.get(Session, preserved["id"]) is not None
