"""Check frozen JSON schemas and language fixtures without rewriting them."""
import json
import sys
from pathlib import Path

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "services/api"))
from app import contracts, remote  # noqa: E402


def check():
    for path in (ROOT / "packages/contracts").glob("*.schema.json"):
        schema = json.loads(path.read_text(encoding="utf-8"))
        Draft202012Validator.check_schema(schema)
        name = path.stem.split(".")[0]
        generated = getattr(remote if name.startswith("Remote") else contracts, name).model_json_schema()
        assert {key: value for key, value in schema.items() if key not in {"$id", "$schema"}} == generated, f"Contract drift: {path.name}"
    for path in (ROOT / "packages/contracts/fixtures").glob("*.json"):
        fixture = json.loads(path.read_text(encoding="utf-8"))
        schema = json.loads((ROOT / f"packages/contracts/{path.stem}.schema.json").read_text(encoding="utf-8"))
        Draft202012Validator(schema).validate(fixture)
        getattr(remote if path.stem.startswith("Remote") else contracts, path.stem).model_validate(fixture)


if __name__ == "__main__":
    check()
    print("Shared schemas and fixtures match the Python contract models.")
