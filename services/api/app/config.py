from functools import lru_cache
from typing import Literal

from pydantic import Field, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")
    app_env: Literal["development", "test", "production"] = "development"
    auth_mode: Literal["mock", "cognito"] = "mock"
    model_provider: Literal["mock", "gemini"] = "mock"
    database_url: str = "postgresql+psycopg://desktopguide:development-only@127.0.0.1:5432/desktopguide"
    gcp_project_id: str = ""
    gcp_location: str = "global"
    gemini_guidance_model: str = "gemini-3.1-flash-lite"
    gemini_verification_model: str = "gemini-3.1-flash-lite"
    gemini_transcription_model: str = "gemini-3.1-flash-lite"
    gemini_thinking_level: Literal["AUTO", "MINIMAL", "LOW", "MEDIUM", "HIGH"] = "AUTO"
    gemini_computer_use_enabled: bool = False
    gemini_computer_locator_enabled: bool = False
    gemini_locator_model: Literal["gemini-3.5-flash-lite"] = "gemini-3.5-flash-lite"
    cognito_issuer: str = ""
    cognito_client_id: str = ""
    cognito_scopes: str = "desktop-guide/lessons"
    speech_output_enabled: bool = False
    tts_voice_name: str = ""
    deletion_ledger_bucket: str = ""
    raw_media_persistence: bool = False
    debug_media_enabled: bool = False
    session_history_retention_days: int = Field(default=30, ge=1, le=90)
    operational_log_retention_days: int = Field(default=7, ge=1, le=30)
    monthly_ai_budget_usd: float = Field(default=20, gt=0, le=1000)
    user_monthly_ai_budget_usd: float = Field(default=4, gt=0, le=100)
    max_model_turns: int = Field(default=60, ge=1, le=100)
    provider_timeout_seconds: int = Field(default=30, ge=1, le=60)
    # Flash-Lite global rates: audio input is $0.50/M, image/text $0.25/M.
    # Use the higher audio rate for the shared estimate; include thinking in output.
    input_price_per_million: float = 0.5
    output_price_per_million: float = 1.5
    max_output_tokens: int = 4096
    max_context_chars: int = 14000

    @model_validator(mode="after")
    def safe_configuration(self):
        if self.raw_media_persistence or self.debug_media_enabled or self.gemini_computer_use_enabled:
            raise ValueError("raw media storage and native computer use are not supported in this pilot build")
        if self.app_env == "production":
            if self.auth_mode != "cognito" or self.model_provider != "gemini":
                raise ValueError("production refuses mock auth/provider")
            if "development-only" in self.database_url or self.database_url.startswith("sqlite"):
                raise ValueError("production requires configured PostgreSQL credentials")
            if not self.deletion_ledger_bucket:
                raise ValueError("production requires independent deletion ledger")
        if self.model_provider == "gemini" and not self.gcp_project_id:
            raise ValueError("managed GCP project is required")
        if self.auth_mode == "cognito" and (
            not self.cognito_issuer.startswith("https://cognito-idp.") or not self.cognito_client_id
        ):
            raise ValueError("Cognito issuer and public client ID required")
        return self


@lru_cache
def get_settings():
    return Settings()
