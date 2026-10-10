"""Verify migrations and backup/restore in a new local-only PostgreSQL database.

Uses only metadata and synthetic records. Never drops an existing database.
"""
import os
import subprocess
from pathlib import Path
from uuid import uuid4

import psycopg
from psycopg import sql

ROOT = Path(__file__).resolve().parents[1]


def run(*args):
    return subprocess.run(args, cwd=ROOT, check=True, capture_output=True)


def main():
    url = "postgresql://desktopguide:development-only@127.0.0.1:5432/postgres"
    database = "dg_check_" + uuid4().hex
    restored = database + "_restored"
    with psycopg.connect(url, autocommit=True) as db:
        for name in (database, restored):
            db.execute(sql.SQL("CREATE DATABASE {}").format(sql.Identifier(name)))
    previous = os.environ.get("DATABASE_URL")
    try:
        os.environ["DATABASE_URL"] = f"postgresql+psycopg://desktopguide:development-only@127.0.0.1:5432/{database}"
        run("uv", "run", "alembic", "-c", "services/api/alembic.ini", "upgrade", "head")
        run("uv", "run", "alembic", "-c", "services/api/alembic.ini", "check")
        # Docker exec performs the binary stream entirely inside the development container.
        dump = run("docker", "compose", "exec", "-T", "db", "pg_dump", "-U", "desktopguide", "-d", database, "-Fc").stdout
        subprocess.run(["docker", "compose", "exec", "-T", "db", "pg_restore", "--exit-on-error", "-U", "desktopguide", "-d", restored],
            input=dump, cwd=ROOT, check=True, capture_output=True)
        with psycopg.connect(url.replace("/postgres", "/" + restored)) as db:
            assert db.execute("SELECT version_num FROM alembic_version").fetchone()[0] == "0001"
            assert db.execute("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'").fetchone()[0] == 12
        print("Fresh PostgreSQL migrations, schema drift check, binary backup and fresh-target restore passed.")
    finally:
        if previous is None:
            os.environ.pop("DATABASE_URL", None)
        else:
            os.environ["DATABASE_URL"] = previous
        # Both names are generated in this invocation; they cannot name existing user databases.
        with psycopg.connect(url, autocommit=True) as db:
            for name in (database, restored):
                db.execute(sql.SQL("DROP DATABASE {}").format(sql.Identifier(name)))


if __name__ == "__main__":
    main()
