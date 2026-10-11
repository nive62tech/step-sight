import importlib.util
import json
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


def test_unused_reservation_release_requires_evidence_and_preserves_unknowns(tmp_path):
    path = tmp_path / "budget.json"
    budget = LiveBudget(path, "approved-project", .17)
    first = budget.reserve("potential retry, never attempted")
    budget.reserve("unknown attempt")
    with pytest.raises(ValueError, match="evidence"):
        budget.release_unused(first, "")
    budget.release_unused(first, "Checked synthetic owner call ledger: one completed call, no retries")
    with pytest.raises(ValueError, match="already reconciled"):
        budget.reconcile(first, 1, 1)
    calls = json.loads(path.read_text())["calls"]
    assert calls[0]["status"] == "not_attempted" and calls[0]["estimated_usd"] == 0
    assert calls[1]["status"] == "reserved_or_unknown" and calls[1]["estimated_usd"] == .08
    budget.reserve("new attempt within unchanged ceiling")
