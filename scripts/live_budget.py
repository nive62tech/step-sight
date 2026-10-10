"""Shared conservative spend ledger for the explicitly approved synthetic validation.

Unknown outcomes keep their reservation. This is an application estimate, not a
provider billing cutoff. Never place requests, responses, media or tokens here.
"""
import json
import os
from contextlib import contextmanager
from datetime import UTC, datetime
from pathlib import Path
from uuid import uuid4


class LiveBudget:
    RESERVATION_USD = 0.08

    def __init__(self, path: Path, project: str, maximum_usd: float = 1.0):
        if not 0 < maximum_usd <= 1:
            raise ValueError("Synthetic validation authorization is limited to $1")
        self.path, self.project, self.maximum_usd = path, project, maximum_usd
        path.parent.mkdir(parents=True, exist_ok=True)

    @contextmanager
    def _ledger(self):
        lock = self.path.with_suffix(".lock")
        # Fail closed on concurrent use or a stale lock; do not steal it.
        descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
        try:
            ledger = json.loads(self.path.read_text(encoding="utf-8")) if self.path.exists() else {
                "version": 1, "project": self.project, "maximum_usd": self.maximum_usd, "calls": []}
            if ledger["version"] != 1 or ledger["project"] != self.project or ledger["maximum_usd"] > 1:
                raise ValueError("Incompatible synthetic validation ledger")
            yield ledger
            temporary = self.path.with_suffix(".tmp")
            temporary.write_text(json.dumps(ledger, indent=2) + "\n", encoding="utf-8")
            os.replace(temporary, self.path)
        finally:
            os.close(descriptor)
            lock.unlink()

    def reserve(self, purpose: str) -> str:
        with self._ledger() as ledger:
            maximum = min(ledger["maximum_usd"], self.maximum_usd)
            total = sum(call["estimated_usd"] for call in ledger["calls"])
            if total + self.RESERVATION_USD > maximum + 1e-9:
                raise ValueError("Approved synthetic validation budget exhausted")
            identifier = str(uuid4())
            ledger["calls"].append({"id": identifier, "purpose": purpose[:64],
                "date": datetime.now(UTC).isoformat(), "status": "reserved_or_unknown",
                "estimated_usd": self.RESERVATION_USD})
            return identifier

    def reconcile(self, identifier: str, input_tokens: int, output_tokens: int) -> float:
        if input_tokens < 0 or output_tokens < 0:
            raise ValueError("Invalid reported usage")
        cost = round((input_tokens * 1.5 + output_tokens * 7.5) / 1_000_000, 7)
        with self._ledger() as ledger:
            call = next(call for call in ledger["calls"] if call["id"] == identifier)
            if call["status"] != "reserved_or_unknown":
                raise ValueError("Usage was already reconciled")
            call.update(status="reported", input_tokens=input_tokens, output_tokens=output_tokens,
                        estimated_usd=cost)
        return cost
