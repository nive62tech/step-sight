"""Ephemeral, account-bound relay. Commands never grant native input authority."""
import hashlib
import secrets
import time
from dataclasses import dataclass, field
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Literal
from uuid import UUID, uuid4

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import FileResponse
from pydantic import Field, model_validator

from app.contracts import Contract


class RemoteRegistration(Contract):
    schema_version: Literal[1] = 1
    device_id: UUID
    label: str = Field(min_length=1, max_length=64)
    allow_autonomous: bool = False


class RemoteLease(Contract):
    schema_version: Literal[1] = 1
    connection_id: UUID
    desktop_key: str
    expires_at: datetime


class RemoteStatus(Contract):
    state: str = Field(default="Idle", pattern=r"^[A-Za-z]+$", max_length=40)
    mode: Literal["guide", "autonomous"] = "guide"
    busy: bool = False
    instruction: str = Field(default="", max_length=1200)
    notice: str = Field(default="", max_length=600)
    can_resume: bool = False
    can_check: bool = False


class RemoteAcknowledgement(Contract):
    command_id: UUID
    accepted: bool
    message: str = Field(default="", max_length=300)


class RemotePoll(Contract):
    schema_version: Literal[1] = 1
    status: RemoteStatus
    acknowledgement: RemoteAcknowledgement | None = None


class RemoteCommandRequest(Contract):
    schema_version: Literal[1] = 1
    request_id: UUID
    kind: Literal["start", "pause", "resume", "stop", "check"]
    mode: Literal["guide", "autonomous"] | None = None
    goal: str | None = Field(default=None, min_length=1, max_length=1000)

    @model_validator(mode="after")
    def shape(self):
        if self.kind == "start":
            if self.mode is None or not self.goal or not self.goal.strip():
                raise ValueError("start requires a mode and task")
        elif self.mode is not None or self.goal is not None:
            raise ValueError("control commands cannot replace the task or mode")
        return self


class RemoteCommand(Contract):
    id: UUID
    kind: Literal["start", "pause", "resume", "stop", "check"]
    mode: Literal["guide", "autonomous"] | None
    goal: str | None
    expires_at: datetime


class RemoteReceipt(Contract):
    schema_version: Literal[1] = 1
    command_id: UUID
    kind: str
    status: Literal["queued", "accepted", "rejected", "expired", "cancelled"]
    message: str = ""


class RemotePollResponse(Contract):
    schema_version: Literal[1] = 1
    command: RemoteCommand | None


@dataclass
class Connection:
    owner: str
    registration: RemoteRegistration
    key_digest: bytes
    expires: float
    seen: float
    status: RemoteStatus = field(default_factory=RemoteStatus)
    pending: RemoteCommand | None = None
    pending_until: float = 0
    receipts: dict = field(default_factory=dict)
    last: RemoteReceipt | None = None
    enabled: bool = True


class RemoteBroker:
    def __init__(self, clock=time.monotonic):
        self.clock = clock
        self.connections: dict[UUID, Connection] = {}

    def cleanup(self):
        stamp = self.clock()
        for cid, link in list(self.connections.items()):
            if stamp > link.expires + 300:
                del self.connections[cid]
            elif (stamp >= link.expires or stamp - link.seen > 20) and link.enabled:
                self.disable(link)
            elif link.pending and stamp >= link.pending_until:
                self.finish(link, "expired", "This command expired before the PC acknowledged it.")

    def finish(self, link, status, message=""):
        if link.pending:
            receipt = link.receipts[link.pending.id][1]
            receipt.status, receipt.message = status, message
            link.last = receipt
            link.pending = None  # Drop task text as soon as delivery ends.

    def disable(self, link):
        link.enabled = False
        self.finish(link, "cancelled", "Phone control was disconnected.")
        link.status = RemoteStatus(state="Offline", notice="Enable phone control again on your PC.")

    def owned(self, owner, cid):
        self.cleanup()
        link = self.connections.get(cid)
        if link is None or link.owner != owner:
            raise HTTPException(404, "computer connection not found")
        return link

    def desktop(self, owner, cid, key):
        link = self.owned(owner, cid)
        if not key or not secrets.compare_digest(link.key_digest, hashlib.sha256(key.encode()).digest()):
            raise HTTPException(403, "desktop connection key required")
        if not link.enabled:
            raise HTTPException(410, "phone control expired or disconnected; enable it again on the PC")
        return link

    def register(self, owner, body):
        self.cleanup()
        active = [x for x in self.connections.values() if x.owner == owner and x.enabled]
        if any(x.registration.device_id == body.device_id for x in active):
            raise HTTPException(409, "this computer is already connected")
        if len(active) >= 3 or len(self.connections) >= 1000:
            raise HTTPException(429, "computer connection limit reached")
        # Replace only an expired connection for this device. Old commands are never inherited.
        for cid, link in list(self.connections.items()):
            if link.owner == owner and link.registration.device_id == body.device_id:
                del self.connections[cid]
        key, cid, stamp = secrets.token_urlsafe(32), uuid4(), self.clock()
        self.connections[cid] = Connection(owner, body, hashlib.sha256(key.encode()).digest(), stamp + 3600, stamp)
        return RemoteLease(connection_id=cid, desktop_key=key, expires_at=datetime.now(UTC) + timedelta(hours=1))

    def list_for(self, owner):
        self.cleanup()
        return {"schema_version": 1, "devices": [
            {"connection_id": cid, "label": link.registration.label, "online": link.enabled,
             "allow_autonomous": link.registration.allow_autonomous, "status": link.status,
             "pending_command": link.pending.kind if link.pending else None, "last_command": link.last}
            for cid, link in self.connections.items() if link.owner == owner]}

    def submit(self, owner, cid, body):
        link = self.owned(owner, cid)
        if not link.enabled:
            raise HTTPException(409, "computer is offline; enable phone control on the PC")
        digest = hashlib.sha256(body.model_dump_json().encode()).digest()
        if body.request_id in link.receipts:
            prior, receipt = link.receipts[body.request_id]
            if prior != digest:
                raise HTTPException(409, "command id was already used for a different request")
            return receipt
        if body.kind == "start":
            if link.status.busy or link.status.state not in {"Idle", "Stopped", "Completed"} or link.pending:
                raise HTTPException(409, "stop the current task before sending another")
            if body.mode == "autonomous" and not link.registration.allow_autonomous:
                raise HTTPException(403, "allow autonomous phone tasks on the PC first")
        elif body.kind == "resume":
            if not link.status.can_resume or link.status.busy or link.pending:
                raise HTTPException(409, "the agent cannot resume yet")
            if link.status.mode == "autonomous" and not link.registration.allow_autonomous:
                raise HTTPException(403, "autonomous phone tasks are not allowed")
        elif body.kind == "check" and (not link.status.can_check or link.pending):
            raise HTTPException(409, "screen check is unavailable")
        elif body.kind == "pause" and link.status.state in {"Idle", "Stopped", "Completed"} and not link.pending:
            raise HTTPException(409, "there is no active task to pause")
        if link.pending:
            if body.kind not in {"stop", "pause"}:
                raise HTTPException(409, "a command is already pending")
            self.finish(link, "cancelled", "Replaced by a pause or stop request.")
        receipt = RemoteReceipt(command_id=body.request_id, kind=body.kind, status="queued")
        link.pending = RemoteCommand(id=body.request_id, kind=body.kind, mode=body.mode, goal=body.goal,
                                     expires_at=datetime.now(UTC) + timedelta(seconds=30))
        link.pending_until = self.clock() + 30
        link.receipts[body.request_id] = (digest, receipt)
        link.last = receipt
        while len(link.receipts) > 100:
            del link.receipts[next(iter(link.receipts))]
        return receipt

    def poll(self, owner, cid, key, body):
        link = self.desktop(owner, cid, key)
        link.seen, link.status = self.clock(), body.status
        if body.acknowledgement and link.pending and body.acknowledgement.command_id == link.pending.id:
            self.finish(link, "accepted" if body.acknowledgement.accepted else "rejected", body.acknowledgement.message)
        return RemotePollResponse(command=link.pending)


def install_remote(app: FastAPI, settings):
    broker = RemoteBroker()
    app.state.remote = broker

    @app.post("/v1/remote/connections", response_model=RemoteLease, status_code=201)
    async def register(body: RemoteRegistration, request: Request):
        return broker.register(request.state.identity.subject, body)

    @app.get("/v1/remote/connections")
    async def devices(request: Request):
        return broker.list_for(request.state.identity.subject)

    @app.post("/v1/remote/connections/{cid}/commands", response_model=RemoteReceipt)
    async def command(cid: UUID, body: RemoteCommandRequest, request: Request):
        return broker.submit(request.state.identity.subject, cid, body)

    @app.post("/v1/remote/connections/{cid}/poll", response_model=RemotePollResponse)
    async def poll(cid: UUID, body: RemotePoll, request: Request):
        return broker.poll(request.state.identity.subject, cid, request.headers.get("x-desktop-lease"), body)

    @app.delete("/v1/remote/connections/{cid}")
    async def disconnect(cid: UUID, request: Request):
        link = broker.owned(request.state.identity.subject, cid)
        # Either the signed-in user or its desktop may revoke the connection.
        broker.disable(link)
        return {"schema_version": 1, "ok": True}

    web = Path(__file__).resolve().parents[3] / "apps/control"

    @app.get("/control/config.json")
    async def web_config():
        return {"client_id": settings.cognito_client_id, "domain": settings.cognito_domain,
                "scope": "openid " + settings.cognito_scopes, "configured": bool(settings.cognito_domain and settings.cognito_client_id)}

    @app.get("/control/")
    @app.get("/control")
    async def web_app():
        return FileResponse(web / "index.html")

    @app.get("/control/{name}")
    async def web_asset(name: str):
        if name not in {"app.js", "auth.js", "styles.css", "manrope.ttf", "Manrope-LICENSE.txt", "manifest.webmanifest", "icon.svg"}:
            raise HTTPException(404, "file not found")
        return FileResponse(web / name)
