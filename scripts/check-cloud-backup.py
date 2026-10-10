"""Restore the approved synthetic pilot snapshot into a new local-only database.

Reads encrypted S3 metadata backups and independent deletion records. It never
restores into, drops or changes an existing user database.
"""
import argparse
import importlib.util
import json
import os
import subprocess
from datetime import UTC, datetime, timedelta
from pathlib import Path
from uuid import UUID, uuid4

import boto3
import psycopg
from psycopg import sql

ROOT = Path(__file__).resolve().parents[1]


def check(key: str):
    if not key.startswith("backups/desktop-guide-"):
        raise ValueError("Only the approved backup prefix is accepted")
    aws = boto3.Session(profile_name="personal", region_name="ap-south-1")
    if aws.client("sts").get_caller_identity()["Account"] != "214422569127":
        raise ValueError("Outside approved AWS account")
    s3, bucket = aws.client("s3"), "dg-pilot-data-214422569127"
    snapshot = s3.get_object(Bucket=bucket, Key=key)
    if snapshot["ServerSideEncryption"] != "AES256" or snapshot["LastModified"] < datetime.now(UTC) - timedelta(days=14):
        raise ValueError("Snapshot is unencrypted or outside the supported recovery window")
    try:
        dump = snapshot["Body"].read(16 * 1024 * 1024 + 1)
    finally:
        snapshot["Body"].close()
    if len(dump) > 16 * 1024 * 1024:
        raise ValueError("Synthetic restore check exceeds its bounded size")
    database = "dg_cloud_restore_" + uuid4().hex
    admin_url = "postgresql://desktopguide:development-only@127.0.0.1:5432/postgres"
    ledger = ROOT / ".local" / database
    ledger.mkdir(parents=True)
    created = False
    previous = os.environ.get("DATABASE_URL")
    try:
        pages = s3.get_paginator("list_objects_v2").paginate(Bucket=bucket, Prefix="deletions/")
        for page in pages:
            for item in page.get("Contents", []):
                identifier = str(UUID(Path(item["Key"]).stem))
                response = s3.get_object(Bucket=bucket, Key=item["Key"])
                try:
                    data = response["Body"].read(16385)
                finally:
                    response["Body"].close()
                if len(data) > 16384:
                    raise ValueError("Deletion record exceeds its bound")
                (ledger / (identifier + ".json")).write_bytes(data)
        with psycopg.connect(admin_url, autocommit=True) as db:
            db.execute(sql.SQL("CREATE DATABASE {}").format(sql.Identifier(database)))
        created = True
        subprocess.run(["docker", "compose", "exec", "-T", "db", "pg_restore", "--exit-on-error", "-U", "desktopguide", "-d", database],
                       input=dump, cwd=ROOT, capture_output=True, check=True)
        os.environ["DATABASE_URL"] = f"postgresql+psycopg://desktopguide:development-only@127.0.0.1:5432/{database}"
        spec = importlib.util.spec_from_file_location("reconcile", ROOT / "scripts/reconcile-deletions.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        module.reconcile(ledger)
        with psycopg.connect(admin_url.replace("/postgres", "/" + database)) as db:
            assert db.execute("SELECT version_num FROM alembic_version").fetchone()[0] == "0001"
            assert db.execute("SELECT count(*) FROM information_schema.tables WHERE table_schema='public'").fetchone()[0] == 12
            for path in ledger.glob("*.json"):
                identifier = json.loads(path.read_text())["session_id"]
                assert db.execute("SELECT count(*) FROM sessions WHERE id=%s", (identifier,)).fetchone()[0] == 0
        report = {"date": datetime.now(UTC).isoformat(), "encrypted_s3_backup_read": "passed", "new_local_target_restore": "passed",
                  "independent_deletion_reconciliation": "passed", "snapshot_bytes": len(dump), "result": "passed",
                  "disaster_recovery_objectives": "not measured"}
        (ROOT / "artifacts/cloud-backup-restore-check.json").write_text(json.dumps(report, indent=2) + "\n")
        print(json.dumps(report, indent=2))
    finally:
        if previous is None:
            os.environ.pop("DATABASE_URL", None)
        else:
            os.environ["DATABASE_URL"] = previous
        if created:
            with psycopg.connect(admin_url, autocommit=True) as db:
                db.execute(sql.SQL("DROP DATABASE {}").format(sql.Identifier(database)))
        for path in ledger.glob("*.json"):
            path.unlink()
        ledger.rmdir()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--allow-cloud", action="store_true")
    parser.add_argument("--backup-key", required=True)
    args = parser.parse_args()
    if not args.allow_cloud:
        parser.error("Cloud snapshot validation requires deployment/synthetic-data authorization")
    check(args.backup_key)
