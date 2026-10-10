import json

import pytest
from app.contracts import ProviderResult, QuestionResponse
from app.providers import generation_schema
from google import genai
from google.auth.credentials import AnonymousCredentials
from google.genai import _transformers


@pytest.mark.parametrize("model", [ProviderResult, QuestionResponse])
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
