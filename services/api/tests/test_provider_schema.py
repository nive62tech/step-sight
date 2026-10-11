import json

import pytest
from app.contracts import BrowserIntent, ProviderResult, QuestionResponse
from app.providers import generation_schema, thinking_config
from app.vision import VisualLocation, VisualResult
from google import genai
from google.auth.credentials import AnonymousCredentials
from google.genai import _transformers


@pytest.mark.parametrize("model", [ProviderResult, QuestionResponse, VisualResult, VisualLocation, BrowserIntent])
def test_strict_contract_can_be_encoded_by_the_pinned_provider_sdk(model, monkeypatch):
    monkeypatch.delenv("GOOGLE_API_KEY", raising=False)
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    client = genai.Client(vertexai=True, project="synthetic-project", location="global", credentials=AnonymousCredentials())
    try:
        wire = generation_schema(model)
        assert "discriminator" not in json.dumps(wire)
        assert "oneOf" not in json.dumps(wire)
        encoded = _transformers.t_schema(client._api_client, wire)
        assert encoded.properties and encoded.type.value == "OBJECT"
        assert model.model_json_schema().get("additionalProperties") is False
    finally:
        client.close()


@pytest.mark.parametrize("name,expected", [
    ("gemini-2.5-flash", {"thinking_budget": 0}),
    ("gemini-2.5-pro", {"thinking_budget": 1024}),
    ("gemini-3.1-flash-lite", {"thinking_level": "MINIMAL"}),
    ("gemini-3.5-flash-lite", {"thinking_level": "MINIMAL"}),
    ("gemini-3.8-flash", {"thinking_level": "LOW"}),
])
def test_thinking_parameters_match_model_family(name, expected):
    assert thinking_config(name).model_dump(exclude_none=True) == expected


def test_explicit_thinking_override_keeps_compatible_family_parameters():
    assert thinking_config("gemini-3.1-flash-lite", "LOW").thinking_level.value == "LOW"
    assert thinking_config("gemini-2.5-flash", "LOW").thinking_level is None
