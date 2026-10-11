"""Publish reviewed schemas/fixtures. CI checks them without rewriting tracked files."""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "services/api"))
from app import contracts  # noqa: E402

NAMES = ["SessionCreate", "SessionView", "TurnRequest", "TurnResponse", "LearnerEvent", "ActionProposal", "QuestionRequest", "QuestionResponse", "SessionIntent", "BrowserPlanRequest", "BrowserPlanResponse"]

if __name__ == "__main__":
    for name in NAMES:
        schema = getattr(contracts, name).model_json_schema()
        schema["$schema"] = "https://json-schema.org/draft/2020-12/schema"
        schema["$id"] = f"https://desktop-guide.invalid/contracts/v1/{name}.schema.json"
        (ROOT / f"packages/contracts/{name}.schema.json").write_text(json.dumps(schema, indent=2) + "\n", encoding="utf-8")
