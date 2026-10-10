import importlib.util
from pathlib import Path

import pytest

spec = importlib.util.spec_from_file_location("live_budget", Path(__file__).resolve().parents[3] / "scripts/live_budget.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
LiveBudget = module.LiveBudget


def test_unknown_attempts_remain_reserved_and_stop_at_authorization(tmp_path):
    budget = LiveBudget(tmp_path / "budget.json", "approved-project", .17)
    first = budget.reserve("synthetic probe")
    budget.reserve("unknown outcome")
    with pytest.raises(ValueError, match="exhausted"):
        budget.reserve("unauthorized extra call")
    assert budget.reconcile(first, 1000, 200) == .003
    budget.reserve("after reconciled usage")


def test_budget_cannot_be_reset_with_another_project_or_concurrent_lock(tmp_path):
    path = tmp_path / "budget.json"
    budget = LiveBudget(path, "approved-project")
    budget.reserve("probe")
    with pytest.raises(ValueError, match="Incompatible"):
        LiveBudget(path, "another-project").reserve("probe")
    path.with_suffix(".lock").touch()
    with pytest.raises(FileExistsError):
        budget.reserve("concurrent probe")
    with pytest.raises(ValueError, match="limited"):
        LiveBudget(path, "approved-project", 2)
