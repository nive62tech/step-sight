import json
from types import SimpleNamespace as NS

import pytest
from app.config import Settings
from app.contracts import BrowserIntent
from app.main import create_app
from app.persistence import ModelCall, Session
from app.providers import GeminiProvider, MockProvider, Usage
from fastapi.testclient import TestClient
from pydantic import ValidationError
from sqlalchemy import select


class Planner(MockProvider):
    def __init__(self, clarify=False):
        self.goals = []
        self.clarify = clarify

    async def plan_browser(self, goal, *, repair=False):
        self.goals.append(goal)
        return BrowserIntent(decision="clarify" if self.clarify else "open_browser", browser="firefox",
            address=None if self.clarify else "https://example.org/model-selected-site",
            message="Which website?" if self.clarify else "Open the selected site."), Usage(100, 20)


def settings(**kwargs):
    return Settings(app_env="test", database_url="sqlite+pysqlite:///:memory:", model_provider="gemini",
        gcp_project_id="synthetic-project", _env_file=None, **kwargs)


@pytest.mark.parametrize("clarify", [False, True])
def test_intent_is_chosen_by_provider_without_session_capture_or_goal_persistence(clarify):
    provider = Planner(clarify)
    app = create_app(settings(), provider)
    with TestClient(app) as client:
        goal = "A novel task with typos and no website URL"
        response = client.post("/v1/browser/plan", json={"schema_version": 1, "goal": goal})
        assert response.status_code == 200, response.text
        result = response.json()
        assert provider.goals == [goal] and result["provider"] == "gemini"
        assert result["decision"] == ("clarify" if clarify else "open_browser")
        assert result["address"] == (None if clarify else "https://example.org/model-selected-site")
        assert set(result) == {"schema_version", "decision", "browser", "address", "message", "provider"}
        with app.state.db_factory() as db:
            assert db.scalars(select(Session)).all() == []
            calls = db.scalars(select(ModelCall)).all()
            assert len(calls) == 1 and calls[0].turn_id is None and calls[0].status == "completed"
            assert calls[0].cost_usd == pytest.approx(.00008)
        assert not app.state.contexts


def test_planning_is_budgeted_and_invalid_requests_never_reach_provider():
    provider = Planner()
    app = create_app(settings(monthly_ai_budget_usd=.01), provider)
    with TestClient(app) as client:
        for body in ({"goal": " "}, {"goal": "x" * 1001}, {"schema_version": 2, "goal": "Open a site"}):
            assert client.post("/v1/browser/plan", json=body).status_code == 422
        assert client.post("/v1/browser/plan", json={"goal": "Open a site"}).status_code == 429
        assert provider.goals == []


@pytest.mark.parametrize("address", ["http://example.org", "file:///C:/Windows", "https://user:pass@example.org",
    "https://127.0.0.1", "https://localhost", "https://private.internal", "https://example.org:1234",
    "https://example.org/\nargument", "https://example.org\\private", None])
def test_model_destination_is_checked_without_site_specific_routing(address):
    with pytest.raises(ValidationError):
        BrowserIntent(decision="open_browser", browser="chrome", address=address, message="Start here.")


def test_clarification_cannot_also_supply_a_launch():
    with pytest.raises(ValidationError):
        BrowserIntent(decision="clarify", browser="chrome", address="https://example.org", message="Which site?")


@pytest.mark.asyncio
async def test_gemini_gets_the_whole_goal_without_a_local_website_catalog():
    provider = object.__new__(GeminiProvider)
    provider.settings = settings()
    calls = []

    async def generate(**kwargs):
        calls.append(kwargs)
        return NS(text=json.dumps({"decision": "open_browser", "browser": "chrome",
            "address": "https://example.org/", "message": "Open the selected website."}),
            usage_metadata=NS(prompt_token_count=100, candidates_token_count=20, thoughts_token_count=0))

    provider.client = NS(aio=NS(models=NS(generate_content=generate)))
    goal = "Help me find train seats on irtc"
    result, _ = await provider.plan_browser(goal)
    assert json.loads(calls[0]["contents"]) == {"goal": goal}
    assert calls[0]["model"] == "gemini-3.1-flash-lite"
    assert calls[0]["config"].automatic_function_calling.disable
    assert result.address == "https://example.org/"  # No keyword override of the provider's choice.
