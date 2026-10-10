import asyncio
import hashlib
import random
import time
from contextlib import asynccontextmanager
from datetime import timedelta
from uuid import UUID

from fastapi import FastAPI, HTTPException, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse, Response
from pydantic import ValidationError
from sqlalchemy import create_engine, delete, func, select, update
from sqlalchemy.exc import IntegrityError, SQLAlchemyError
from sqlalchemy.orm import sessionmaker
from sqlalchemy.pool import StaticPool

from app.auth import Authenticator
from app.config import Settings, get_settings
from app.contracts import (
    LearnerEvent,
    QuestionRequest,
    SessionCreate,
    SessionIntent,
    SessionView,
    SynthesisRequest,
    TurnRequest,
    TurnResponse,
    VerificationStatus,
)
from app.deletions import record_deletion
from app.lessons import LessonRepository
from app.media import bounded_body, observation_upload, validate_audio, validate_image
from app.persistence import (
    Base,
    ConsentRecord,
    DeletionTombstone,
    Device,
    LessonStep,
    ModelCall,
    Session,
    SessionEvent,
    Turn,
    User,
    now,
)
from app.providers import GeminiProvider, MockProvider, ProviderUnavailable


def create_app(settings: Settings | None = None, provider=None):
    settings = settings or get_settings()
    engine_options = {"pool_pre_ping": True, "hide_parameters": True}
    if settings.database_url.startswith("sqlite"):
        engine_options.update(connect_args={"check_same_thread": False})
        if ":memory:" in settings.database_url:
            engine_options["poolclass"] = StaticPool
    engine = create_engine(settings.database_url, **engine_options)
    db_factory = sessionmaker(engine, expire_on_commit=False)
    auth = Authenticator(settings)
    lessons = LessonRepository()
    provider = provider or (MockProvider() if settings.model_provider == "mock" else GeminiProvider(settings))
    provider_slots = asyncio.Semaphore(2)
    budget_lock = asyncio.Lock()
    locks: dict[str, asyncio.Lock] = {}
    contexts: dict[str, dict] = {}
    cache: dict[str, tuple[float, dict]] = {}
    rate: dict[str, list[float]] = {}

    async def purge_retention():
        while True:
            await asyncio.sleep(60)
            cleanup()

    def cleanup():
        cutoff = now() - timedelta(days=settings.session_history_retention_days)
        transient_cutoff = now() - timedelta(hours=1)
        with db_factory() as db:
            old_ids = db.scalars(select(Session.id).where(
                (Session.updated_at < cutoff) | ((Session.saved_progress.is_(False)) & (Session.updated_at < transient_cutoff))
            )).all()
            for sid in old_ids:
                remove_session(db, sid)
            db.execute(delete(DeletionTombstone).where(DeletionTombstone.deleted_at < now() - timedelta(days=30)))
            db.execute(delete(ModelCall).where(ModelCall.created_at < now() - timedelta(days=90)))
            db.commit()
        for sid in old_ids:
            contexts.pop(sid, None)
            if not locks.get(sid, asyncio.Lock()).locked():
                locks.pop(sid, None)
        for key, (expires, _) in list(cache.items()):
            if expires < time.monotonic():
                cache.pop(key, None)
        for key, entries in list(rate.items()):
            if not entries or entries[-1] < time.monotonic() - 60:
                rate.pop(key, None)

    @asynccontextmanager
    async def lifespan(app):
        if settings.app_env == "test":
            Base.metadata.create_all(engine)
        task = asyncio.create_task(purge_retention())
        yield
        task.cancel()
        try:
            await task
        except asyncio.CancelledError:
            pass
        if hasattr(provider, "close"):
            await provider.close()
        engine.dispose()

    app = FastAPI(title="Desktop Guide API", version="0.1.0", lifespan=lifespan)
    app.state.settings = settings
    app.state.engine = engine
    app.state.db_factory = db_factory
    app.state.provider = provider
    app.state.auth = auth
    app.state.contexts = contexts

    @app.middleware("http")
    async def limits_and_headers(request: Request, call_next):
        size = request.headers.get("content-length")
        if size and (not size.isdigit() or int(size) > 6 * 1024 * 1024):
            return JSONResponse({"detail": "request exceeds size limit"}, status_code=413)
        if request.url.path.startswith("/v1/"):
            try:
                identity = await auth.authenticate(request)
                request.state.identity = identity
                entries = rate.setdefault(identity.subject, [])
                threshold = time.monotonic() - 60
                entries[:] = [entry for entry in entries if entry > threshold]
                if len(entries) >= 120:
                    raise HTTPException(429, "request rate limit")
                entries.append(time.monotonic())
                # Count streamed JSON bodies before FastAPI's JSON decoder, not only Content-Length.
                if request.method in {"POST", "PUT", "PATCH"} and "application/json" in request.headers.get("content-type", ""):
                    request._body = await bounded_body(request, 64 * 1024)
            except HTTPException as exc:
                return JSONResponse({"detail": exc.detail}, status_code=exc.status_code)
        response = await call_next(request)
        response.headers["Cache-Control"] = "no-store"
        response.headers["X-Content-Type-Options"] = "nosniff"
        response.headers["Referrer-Policy"] = "no-referrer"
        return response

    @app.exception_handler(RequestValidationError)
    async def validation_error(request, exc):
        # FastAPI's default errors may echo user input; deliberately omit it.
        return JSONResponse({"detail": "invalid request contract"}, status_code=422)

    @app.exception_handler(SQLAlchemyError)
    async def database_error(request, exc):
        return JSONResponse({"detail": "database unavailable"}, status_code=503)

    def owner(db, request):
        subject = request.state.identity.subject
        user = db.scalar(select(User).where(User.subject == subject))
        if user is None:
            user = User(subject=subject)
            db.add(user)
            try:
                db.commit()
            except IntegrityError:
                db.rollback()
                user = db.scalar(select(User).where(User.subject == subject))
        return user.id

    def owned_session(db, request, sid):
        row = db.scalar(select(Session).where(Session.id == str(sid), Session.owner_id == owner(db, request)))
        if row is None:
            raise HTTPException(404, "session not found")
        return row

    def view(row):
        return SessionView(id=row.id, device_id=row.device_id, goal=row.goal, mode=row.mode,
                           language=row.language, detail_level=row.detail_level, scope_id=row.scope_id,
                           session_version=row.version, state=row.state, lesson_id=row.lesson_id,
                           saved_progress=row.saved_progress, completed_steps=row.completed_steps,
                           created_at=row.created_at, provider=settings.model_provider,
                           pending_step=contexts.get(row.id, {}).get("pending_step"))

    def require_version(row, version):
        if row.version != version:
            raise HTTPException(409, "session version changed; reobserve")

    def remove_session(db, sid):
        # Usage records survive with no lesson/media so deletion cannot reset spending limits.
        turn_ids = select(Turn.id).where(Turn.session_id == sid)
        db.execute(update(ModelCall).where(ModelCall.turn_id.in_(turn_ids)).values(turn_id=None))
        for table in (ConsentRecord, SessionEvent, LessonStep, Turn):
            db.execute(delete(table).where(table.session_id == sid))
        db.execute(delete(Session).where(Session.id == sid))
        if db.get(DeletionTombstone, sid) is None:
            db.add(DeletionTombstone(session_id=sid))

    async def accounted_attempt(owner_id, turn_id, call, model, attempt, repair, deadline):
        # A single worker plus this lock serializes reservations, including concurrent sessions.
        reserve = 0.08 if settings.model_provider == "gemini" else 0.0
        async with budget_lock:
            month = now().replace(day=1, hour=0, minute=0, second=0, microsecond=0)
            with db_factory() as db:
                total = db.scalar(select(func.coalesce(func.sum(ModelCall.cost_usd), 0)).where(ModelCall.created_at >= month))
                user_total = db.scalar(select(func.coalesce(func.sum(ModelCall.cost_usd), 0)).where(ModelCall.created_at >= month, ModelCall.owner_id == owner_id))
                if total + reserve > settings.monthly_ai_budget_usd or user_total + reserve > settings.user_monthly_ai_budget_usd:
                    raise HTTPException(429, "monthly AI allowance exhausted; progress preserved")
                record = ModelCall(owner_id=owner_id, turn_id=turn_id, model=model, cost_usd=reserve, attempt=attempt)
                db.add(record)
                db.commit()
                record_id = record.id
        started = time.monotonic()
        try:
            async with provider_slots:
                async with asyncio.timeout(max(.1, min(settings.provider_timeout_seconds, deadline - time.monotonic()))):
                    result, usage = await call(repair)
            with db_factory() as db:
                actual = (usage.input_tokens * settings.input_price_per_million + usage.output_tokens * settings.output_price_per_million) / 1_000_000
                db.execute(update(ModelCall).where(ModelCall.id == record_id).values(
                    status="completed" if usage.known else "completed_usage_unknown",
                    input_tokens=usage.input_tokens, output_tokens=usage.output_tokens,
                    cost_usd=actual if usage.known else reserve, duration_ms=round((time.monotonic() - started) * 1000),
                ))
                db.commit()
            return result
        except (ProviderUnavailable, TimeoutError) as exc:
            with db_factory() as db:
                db.execute(update(ModelCall).where(ModelCall.id == record_id).values(status="unknown"))
                db.commit()
            if isinstance(exc, TimeoutError):
                raise ProviderUnavailable("timeout", retryable=True) from None
            raise

    async def accounted_call(owner_id, turn_id, call, model):
        deadline = time.monotonic() + settings.provider_timeout_seconds + 5
        transient_retries, repaired, attempt = 0, False, 0
        while True:
            try:
                return await accounted_attempt(owner_id, turn_id, call, model, attempt, repaired, deadline)
            except ProviderUnavailable as exc:
                if time.monotonic() >= deadline:
                    break
                if exc.schema_invalid and not repaired:
                    repaired = True
                elif exc.retryable and transient_retries < 2:
                    transient_retries += 1
                    await asyncio.sleep(min(.25 * (2 ** transient_retries) + random.uniform(0, .2), max(0, deadline - time.monotonic())))
                else:
                    break
                attempt += 1
        raise HTTPException(503, "provider unavailable; reobserve or continue manually")

    @app.get("/health/live")
    async def live():
        return {"status": "alive"}

    @app.get("/health/ready")
    async def ready():
        with engine.connect() as db:
            db.execute(select(1))
        return {"status": "ready", "provider": settings.model_provider, "auth": settings.auth_mode}

    @app.get("/v1/lessons")
    async def list_lessons():
        return lessons.list()

    @app.post("/v1/sessions", response_model=SessionView, status_code=201)
    async def create_session(body: SessionCreate, request: Request):
        cleanup()
        if len(contexts) >= 128:
            raise HTTPException(429, "pilot session capacity reached")
        if lessons.get(body.lesson_id) is None:
            raise HTTPException(404, "lesson not found")
        with db_factory() as db:
            oid = owner(db, request)
            device = db.get(Device, str(body.device_id))
            if device and device.owner_id != oid:
                raise HTTPException(409, "device identifier unavailable")
            if device is None:
                db.add(Device(id=str(body.device_id), owner_id=oid))
                db.flush()
            row = Session(owner_id=oid, device_id=str(body.device_id), goal=body.goal if body.save_progress else "Unsaved lesson",
                          mode=body.mode.value, language=body.language, detail_level=body.detail_level,
                          scope_id=str(body.scope_id), lesson_id=body.lesson_id, saved_progress=body.save_progress)
            db.add(row)
            db.flush()
            db.add(ConsentRecord(owner_id=oid, session_id=row.id, save_progress=body.save_progress))
            db.commit()
            contexts[row.id] = {"goal": body.goal, "pending_step": None}
            return view(row)

    @app.get("/v1/sessions/{sid}", response_model=SessionView)
    async def get_session(sid: UUID, request: Request):
        with db_factory() as db:
            return view(owned_session(db, request, sid))

    @app.post("/v1/sessions/{sid}/turns", response_model=TurnResponse)
    async def turn(sid: UUID, request: Request):
        turn_started = time.monotonic()
        metadata, image = await observation_upload(request)
        try:
            body = TurnRequest.model_validate_json(metadata)
        except ValidationError:
            raise HTTPException(422, "invalid turn contract") from None
        obs = body.observation
        validate_image(image, obs.image_width, obs.image_height)
        age = (now() - obs.captured_at).total_seconds()
        upload_validation_ms = round((time.monotonic() - turn_started) * 1000)
        if not -5 <= age <= 60:
            raise HTTPException(409, "observation timestamp requires re-observation")
        digest = hashlib.sha256(metadata + b"\0" + image).hexdigest()
        sid_text = str(sid)
        lock = locks.setdefault(sid_text, asyncio.Lock())
        if lock.locked():
            raise HTTPException(409, "one in-flight request per session")
        async with lock:
            with db_factory() as db:
                row = owned_session(db, request, sid)
                existing = db.scalar(select(Turn).where(Turn.session_id == sid_text, Turn.idempotency_key == str(body.request_id)))
                if existing:
                    if existing.digest != digest:
                        raise HTTPException(409, "idempotency key payload conflict")
                    cached = cache.get(existing.id)
                    if cached and cached[0] > time.monotonic() and row.version == cached[1]["session_version"]:
                        return cached[1]
                    raise HTTPException(409, "response expired or session changed; reobserve with a new request")
                require_version(row, body.session_version)
                if row.state not in {"OBSERVING", "WAITING_FOR_LEARNER"}:
                    raise HTTPException(409, "session is not observing; fresh local resume required")
                if str(obs.scope_id) != row.scope_id:
                    raise HTTPException(409, "capture scope mismatch")
                context = contexts.get(sid_text)
                if context is None:
                    if not row.saved_progress:
                        raise HTTPException(409, "unsaved session lost; start a new session")
                    context = contexts[sid_text] = {"goal": row.goal, "pending_step": None}
                previous = context["pending_step"]
                if (body.previous_step.model_dump(mode="json") if body.previous_step else None) != previous:
                    raise HTTPException(409, "previous step mismatch; reobserve")
                count = db.scalar(select(func.count()).select_from(Turn).where(Turn.session_id == sid_text))
                if count >= settings.max_model_turns:
                    raise HTTPException(429, "lesson turn limit reached; progress preserved")
                claim = Turn(session_id=sid_text, idempotency_key=str(body.request_id), digest=digest)
                db.add(claim)
                db.commit()
                turn_id, owner_id, version, lesson_id = claim.id, row.owner_id, row.version, row.lesson_id
                lesson_context = lessons.get(lesson_id) | {"verified_milestones": row.completed_steps,
                    "language": row.language, "detail_level": row.detail_level}
            try:
                inference_started = time.monotonic()
                result = await accounted_call(owner_id, turn_id,
                    lambda repair: provider.guide(context["goal"], lesson_context, body, image, repair=repair),
                    settings.gemini_verification_model if body.previous_step else settings.gemini_guidance_model)
                inference_ms = round((time.monotonic() - inference_started) * 1000)
                validation_started = time.monotonic()
                # Repeat critical grounding checks independent of model schema success.
                evidence_lists = []
                if result.step:
                    if result.step.source_observation_id != obs.id or (result.step.target and result.step.target.geometry_id != obs.geometry_id):
                        raise HTTPException(502, "provider returned incompatible target")
                    evidence_lists.append(result.step.evidence)
                if result.verification:
                    evidence_lists.append(result.verification.evidence)
                if any(e.observation_id != obs.id for entries in evidence_lists for e in entries):
                    raise HTTPException(502, "provider returned stale evidence")
                if result.action_proposal and (
                    result.action_proposal.source_observation_id != obs.id or result.action_proposal.geometry_id != obs.geometry_id
                ):
                    raise HTTPException(502, "provider returned incompatible proposal")
                confirmed = result.verification and result.verification.status == VerificationStatus.CONFIRMED
                if body.previous_step and not confirmed:
                    result.step, result.action_proposal, result.completed = None, None, False
                if result.completed and (not body.previous_step or not confirmed):
                    raise HTTPException(502, "completion lacks verified final evidence")
                with db_factory() as db:
                    changed = db.execute(update(Session).where(Session.id == sid_text, Session.version == version, Session.state.in_(["OBSERVING", "WAITING_FOR_LEARNER"])).values(
                        version=version + 1, updated_at=now(), state="COMPLETED" if result.completed else "WAITING_FOR_LEARNER",
                    ))
                    if changed.rowcount != 1:
                        raise HTTPException(409, "session interrupted; discard late response")
                    row = db.get(Session, sid_text)
                    if body.previous_step and confirmed:
                        row.completed_steps += 1
                        db.add(LessonStep(session_id=sid_text, step_sequence=row.completed_steps,
                                          verification_status="CONFIRMED", milestone=f"milestone-{row.completed_steps}"))
                    if result.completed and row.completed_steps < len(lessons.get(lesson_id)["milestones"]):
                        raise HTTPException(502, "lesson completion before required milestones")
                    db.execute(update(Turn).where(Turn.id == turn_id).values(status="completed"))
                    db.commit()
                context["pending_step"] = result.step.model_dump(mode="json") if result.step else previous
                response = TurnResponse(**result.model_dump(), session_id=sid, session_version=version + 1,
                                        turn_id=turn_id, observation_id=obs.id, provider=settings.model_provider,
                                        timings_ms={"upload_validation": upload_validation_ms, "inference": inference_ms,
                                            "validation_persistence": round((time.monotonic() - validation_started) * 1000)})
                payload = response.model_dump(mode="json")
                cache[turn_id] = (time.monotonic() + 300, payload)
                if len(cache) > 256:
                    cache.pop(next(iter(cache)))
                return response
            except BaseException:
                with db_factory() as db:
                    db.execute(update(Turn).where(Turn.id == turn_id).values(status="interrupted"))
                    db.commit()
                raise

    @app.post("/v1/sessions/{sid}/questions")
    async def question(sid: UUID, body: QuestionRequest, request: Request):
        lock = locks.setdefault(str(sid), asyncio.Lock())
        if lock.locked():
            raise HTTPException(409, "wait for current turn before asking")
        async with lock:
            with db_factory() as db:
                row = owned_session(db, request, sid)
                require_version(row, body.session_version)
                if row.state == "STOPPED":
                    raise HTTPException(409, "session stopped")
                goal, oid, version = contexts.get(str(sid), {}).get("goal", row.goal), row.owner_id, row.version
            result = await accounted_call(oid, None, lambda repair: provider.question(goal, body.text, repair=repair), settings.gemini_guidance_model)
            with db_factory() as db:
                require_version(owned_session(db, request, sid), version)
            return result

    @app.post("/v1/sessions/{sid}/events")
    async def event(sid: UUID, body: LearnerEvent, request: Request):
        digest = hashlib.sha256(body.model_dump_json().encode()).hexdigest()
        with db_factory() as db:
            row = owned_session(db, request, sid)
            existing = db.scalar(select(SessionEvent).where(SessionEvent.session_id == str(sid), SessionEvent.event_id == str(body.id)))
            if existing:
                if existing.digest != digest:
                    raise HTTPException(409, "event payload conflict")
                return {"accepted": True, "duplicate": True}
            require_version(row, body.session_version)
            previous_sequence = db.scalar(select(func.max(SessionEvent.sequence)).where(SessionEvent.session_id == str(sid)))
            if previous_sequence is not None and body.sequence <= previous_sequence:
                raise HTTPException(409, "stale event sequence")
            db.add(SessionEvent(session_id=str(sid), event_id=str(body.id), sequence=body.sequence, digest=digest, kind=body.kind))
            db.commit()
            return {"accepted": True, "duplicate": False}

    async def intent(sid, body, request, state):
        with db_factory() as db:
            row = owned_session(db, request, sid)
            require_version(row, body.session_version)
            if row.state == "STOPPED" and state != "STOPPED":
                raise HTTPException(409, "stopped sessions cannot resume")
            changed = db.execute(update(Session).where(Session.id == str(sid), Session.version == body.session_version).values(
                state=state, version=body.session_version + 1, updated_at=now()))
            if changed.rowcount != 1:
                raise HTTPException(409, "session version changed")
            db.commit()
            db.refresh(row)
            if state == "STOPPED":
                contexts.pop(str(sid), None)
            return view(row)

    @app.post("/v1/sessions/{sid}/pause", response_model=SessionView)
    async def pause(sid: UUID, body: SessionIntent, request: Request):
        return await intent(sid, body, request, "PAUSED")

    @app.post("/v1/sessions/{sid}/resume", response_model=SessionView)
    async def resume(sid: UUID, body: SessionIntent, request: Request):
        return await intent(sid, body, request, "OBSERVING")

    @app.post("/v1/sessions/{sid}/stop", response_model=SessionView)
    async def stop(sid: UUID, body: SessionIntent, request: Request):
        return await intent(sid, body, request, "STOPPED")

    @app.delete("/v1/sessions/{sid}", status_code=204)
    async def delete_session(sid: UUID, request: Request):
        with db_factory() as db:
            owned_session(db, request, sid)
        await record_deletion(settings.deletion_ledger_bucket, str(sid))
        with db_factory() as db:
            owned_session(db, request, sid)
            remove_session(db, str(sid))
            db.commit()
        contexts.pop(str(sid), None)
        return Response(status_code=204)

    @app.post("/v1/speech/transcribe")
    async def transcribe(request: Request):
        data = await bounded_body(request, 1_000_000)
        validate_audio(data)
        if not hasattr(provider, "transcribe"):
            raise HTTPException(503, "transcription unavailable in mock mode")
        with db_factory() as db:
            oid = owner(db, request)
        text = await accounted_call(oid, None, lambda repair: provider.transcribe(data, repair=repair), settings.gemini_transcription_model)
        return {"text": text, "requires_goal_confirmation": True}

    @app.post("/v1/speech/synthesize")
    async def synthesize(body: SynthesisRequest, request: Request):
        if not settings.speech_output_enabled or not settings.tts_voice_name:
            raise HTTPException(503, "speech output is disabled or not configured")
        from google.cloud import texttospeech

        from app.providers import Usage

        with db_factory() as db:
            oid = owner(db, request)

        async def generate(repair=False):
            client = texttospeech.TextToSpeechAsyncClient()
            try:
                response = await client.synthesize_speech(
                    input=texttospeech.SynthesisInput(text=body.text),
                    voice=texttospeech.VoiceSelectionParams(language_code=body.language, name=settings.tts_voice_name),
                    audio_config=texttospeech.AudioConfig(audio_encoding=texttospeech.AudioEncoding.LINEAR16),
                    timeout=settings.provider_timeout_seconds,
                )
                if len(response.audio_content) > 2_000_000:
                    raise ProviderUnavailable("speech output size")
                # Conservative $30 / million characters represented using the ledger output rate.
                return response.audio_content, Usage(output_tokens=len(body.text) * 4)
            except Exception:
                raise ProviderUnavailable("speech unavailable") from None
            finally:
                await client.transport.close()

        audio = await accounted_call(oid, None, generate, settings.tts_voice_name)
        return Response(audio, media_type="audio/wav")

    return app
