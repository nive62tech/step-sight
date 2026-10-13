import ipaddress
import re
from datetime import datetime
from enum import StrEnum
from typing import Annotated, Literal
from urllib.parse import urlsplit
from uuid import UUID

from pydantic import AwareDatetime, BaseModel, ConfigDict, Field, model_validator


class Contract(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)


class Mode(StrEnum):
    GUIDE = "guide"
    DEMONSTRATE = "demonstrate"
    PRACTICE = "practice"
    AUTONOMOUS = "autonomous"


class VerificationStatus(StrEnum):
    CONFIRMED = "CONFIRMED"
    NOT_YET = "NOT_YET"
    DIFFERENT_STATE = "DIFFERENT_STATE"
    UNCERTAIN = "UNCERTAIN"


class Bounds(Contract):
    left: float = Field(ge=0, le=1)
    top: float = Field(ge=0, le=1)
    right: float = Field(ge=0, le=1)
    bottom: float = Field(ge=0, le=1)

    @model_validator(mode="after")
    def ordered(self):
        if self.left > self.right or self.top > self.bottom:
            raise ValueError("inverted rectangle")
        return self


class Target(Contract):
    kind: Literal["rectangle"] = "rectangle"
    coordinate_space: Literal["normalized_source_image"] = "normalized_source_image"
    geometry_id: UUID
    bounds: Bounds
    visible_label: str = Field(max_length=200)


class Observation(Contract):
    id: UUID
    captured_at: AwareDatetime
    scope_id: UUID
    geometry_id: UUID
    image_width: int = Field(ge=1, le=8192)
    image_height: int = Field(ge=1, le=8192)
    privacy_filtered: Literal[True]


class Evidence(Contract):
    observation_id: UUID
    detail: str = Field(min_length=1, max_length=500)


class Postcondition(Contract):
    kind: Literal["visible_text", "visible_state"]
    value: str = Field(min_length=1, max_length=500)


class GuidanceStep(Contract):
    id: UUID
    source_observation_id: UUID
    instruction: str = Field(min_length=1, max_length=1000)
    explanation: str = Field(max_length=1500)
    target: Target | None = None
    expected_user_action: Literal["click", "type", "scroll", "key_chord", "open_app", "manual", "wait"]
    postconditions: list[Postcondition] = Field(min_length=1, max_length=5)
    hints: list[Annotated[str, Field(max_length=500)]] = Field(default_factory=list, max_length=5)
    evidence: list[Evidence] = Field(min_length=1, max_length=5)
    uncertainty: list[Annotated[str, Field(max_length=500)]] = Field(default_factory=list, max_length=5)


class ClickAction(Contract):
    kind: Literal["click"]
    x: float = Field(ge=0, le=1)
    y: float = Field(ge=0, le=1)
    button: Literal["left"] = "left"


class ScrollAction(Contract):
    kind: Literal["scroll"]
    x: float = Field(ge=0, le=1)
    y: float = Field(ge=0, le=1)
    ticks: int = Field(ge=-3, le=3)


class TypeAction(Contract):
    kind: Literal["type"]
    text: str = Field(min_length=1, max_length=256)


class KeyAction(Contract):
    kind: Literal["key_chord"]
    chord: Literal["Ctrl+A", "Ctrl+C", "Ctrl+S", "Escape", "Tab", "Enter", "Backspace", "Home", "End", "Left", "Right", "Up", "Down"]


class OpenAppAction(Contract):
    kind: Literal["open_app"]
    application: Literal["whatsapp"]


Action = Annotated[ClickAction | ScrollAction | TypeAction | KeyAction | OpenAppAction, Field(discriminator="kind")]


class ActionProposal(Contract):
    id: UUID
    source_observation_id: UUID
    geometry_id: UUID
    action: Action
    effect: str = Field(min_length=1, max_length=500)
    risk: Literal["low", "manual"]
    provider_confirmation_required: bool = False


class VerificationResult(Contract):
    status: VerificationStatus
    evidence: list[Evidence] = Field(default_factory=list, max_length=5)
    unmet_postconditions: list[Annotated[str, Field(max_length=500)]] = Field(default_factory=list, max_length=5)
    recovery_suggestion: str | None = Field(default=None, max_length=1000)
    provenance: Literal["mock", "gemini", "deterministic"]

    @model_validator(mode="after")
    def confirmation_evidence(self):
        if self.status == VerificationStatus.CONFIRMED and (not self.evidence or self.unmet_postconditions):
            raise ValueError("confirmation requires positive evidence and no unmet conditions")
        return self


class SessionCreate(Contract):
    schema_version: Literal[1] = 1
    device_id: UUID
    goal: str = Field(min_length=1, max_length=1000)
    mode: Mode = Mode.GUIDE
    language: Literal["en", "ml"] = "en"
    detail_level: Literal["brief", "standard", "detailed"] = "standard"
    scope_id: UUID
    save_progress: bool = False
    lesson_id: str = Field(default="sandbox", pattern=r"^[a-z0-9-]{1,40}$")


class SessionView(Contract):
    schema_version: Literal[1] = 1
    id: UUID
    device_id: UUID
    goal: str
    mode: Mode
    language: str
    detail_level: str
    scope_id: UUID
    session_version: int
    state: str
    lesson_id: str
    saved_progress: bool
    completed_steps: int
    created_at: datetime
    provider: Literal["mock", "gemini"]
    pending_step: GuidanceStep | None = None


class TaskContext(Contract):
    current_application: str = Field(min_length=1, max_length=100)
    allowed_applications: list[Literal["whatsapp"]] = Field(default_factory=list, max_length=1)
    handoff_from_scope_id: UUID | None = None


class TurnRequest(Contract):
    schema_version: Literal[1] = 1
    request_id: UUID
    session_version: int = Field(ge=0)
    observation: Observation
    event_kind: Literal["start", "activity", "i_did_this", "reobserve", "resume", "recover", "replan", "app_handoff", "demonstration_result", "autonomous_result", "locate"]
    previous_step: GuidanceStep | None = None
    task_context: TaskContext | None = None


class ProviderResult(Contract):
    step: GuidanceStep | None
    verification: VerificationResult | None = None
    action_proposal: ActionProposal | None = None
    completed: bool = False


class TargetLocation(Contract):
    source_observation_id: UUID
    target: Target | None
    evidence: list[Evidence] = Field(min_length=1, max_length=5)
    uncertainty: list[Annotated[str, Field(max_length=500)]] = Field(default_factory=list, max_length=5)


class TurnResponse(ProviderResult):
    schema_version: Literal[1] = 1
    session_id: UUID
    session_version: int
    turn_id: UUID
    observation_id: UUID
    provider: Literal["mock", "gemini"]
    timings_ms: dict[str, int] = Field(default_factory=dict)


class SessionIntent(Contract):
    schema_version: Literal[1] = 1
    session_version: int = Field(ge=0)


class LearnerEvent(Contract):
    schema_version: Literal[1] = 1
    id: UUID
    sequence: int = Field(ge=0)
    local_epoch: int = Field(ge=0)
    session_version: int = Field(ge=0)
    kind: Literal["attempt", "skipped", "takeover", "privacy_pause", "dispatch", "unknown_outcome"]
    proposal_id: UUID | None = None
    outcome: Literal["not_dispatched", "dispatched", "unknown"] | None = None


class QuestionRequest(Contract):
    schema_version: Literal[1] = 1
    session_version: int = Field(ge=0)
    text: str = Field(min_length=1, max_length=2000)


class QuestionResponse(Contract):
    answer: str = Field(min_length=1, max_length=3000)
    provider: Literal["mock", "gemini"]


class BrowserPlanRequest(Contract):
    schema_version: Literal[1] = 1
    goal: str = Field(min_length=1, max_length=1000)


class BrowserIntent(Contract):
    decision: Literal["open_browser", "clarify"]
    browser: Literal["chrome", "edge", "firefox"]
    address: str | None = Field(default=None, max_length=2048)
    message: str = Field(min_length=1, max_length=500)

    @model_validator(mode="after")
    def valid_destination(self):
        if self.decision == "clarify":
            if self.address is not None:
                raise ValueError("clarification cannot open a website")
            return self
        if not self.address or re.search(r"[\s\\\x00-\x1f]", self.address):
            raise ValueError("invalid browser destination")
        url = urlsplit(self.address)
        host = url.hostname or ""
        if (url.scheme != "https" or url.username is not None or url.password is not None or url.port not in (None, 443)
                or not re.fullmatch(r"(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}", host)
                or host.lower().endswith((".localhost", ".local", ".internal", ".lan", ".home", ".invalid", ".test"))):
            raise ValueError("destination must be a public HTTPS website")
        try:
            ipaddress.ip_address(host)
        except ValueError:
            return self
        raise ValueError("IP addresses cannot be browser destinations")


class BrowserPlanResponse(BrowserIntent):
    schema_version: Literal[1] = 1
    provider: Literal["mock", "gemini"]


class SynthesisRequest(Contract):
    text: str = Field(min_length=1, max_length=2000)
    language: Literal["en-US"] = "en-US"
