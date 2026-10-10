import importlib.util
from pathlib import Path


def test_reviewed_schemas_and_fixtures_match():
    path = Path(__file__).resolve().parents[3] / "scripts/check-contracts.py"
    spec = importlib.util.spec_from_file_location("check_contracts", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.check()
