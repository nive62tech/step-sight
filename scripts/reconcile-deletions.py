"""Restore-time application of an independent S3 deletion ledger; metadata only."""
import argparse
import json
import sys
from datetime import timedelta
from pathlib import Path
from uuid import UUID

from sqlalchemy import create_engine, delete, select, update
from sqlalchemy.orm import Session as DatabaseSession

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "services/api"))
from app.config import Settings  # noqa: E402
from app.persistence import (  # noqa: E402
    ConsentRecord,
    DeletionTombstone,
    LessonStep,
    ModelCall,
    Session,
    SessionEvent,
    Turn,
    now,
)


def reconcile(directory):
    settings = Settings()
    entries = []
    for path in directory.glob("*.json"):
        item = json.loads(path.read_text(encoding="utf-8"))
        if item.get("schema_version") != 1 or set(item) != {"schema_version", "session_id", "deleted_at"}:
            raise ValueError("invalid independent deletion record")
        entries.append(str(UUID(item["session_id"])))
    engine = create_engine(settings.database_url, hide_parameters=True)
    try:
        with DatabaseSession(engine) as db:
            expired = db.scalars(select(Session.id).where((Session.updated_at < now() - timedelta(days=settings.session_history_retention_days))
                | ((Session.saved_progress.is_(False)) & (Session.updated_at < now() - timedelta(hours=1))))).all()
            for sid in set(entries + expired):
                turn_ids = select(Turn.id).where(Turn.session_id == sid)
                db.execute(update(ModelCall).where(ModelCall.turn_id.in_(turn_ids)).values(turn_id=None))
                for table in (ConsentRecord, SessionEvent, LessonStep, Turn, Session):
                    column = table.id if table is Session else table.session_id
                    db.execute(delete(table).where(column == sid))
                if db.get(DeletionTombstone, sid) is None:
                    db.add(DeletionTombstone(session_id=sid))
            db.commit()
        print(f"Reconciled {len(set(entries + expired))} deleted or expired lesson identifiers before API exposure.")
    finally:
        engine.dispose()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--ledger-directory", type=Path, required=True)
    parser.add_argument("--confirm-restored-target", action="store_true")
    args = parser.parse_args()
    if not args.confirm_restored_target:
        parser.error("requires --confirm-restored-target against a private restored database")
    reconcile(args.ledger_directory)
