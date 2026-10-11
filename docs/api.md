# API contract

The generated JSON schemas are in `packages/contracts`; synthetic fixtures round-trip through Python and C#. All versioned bodies use `schema_version: 1`, snake_case, bounded fields and forbidden unknown members. Coordinates are normalized to the source image and must carry its geometry identifier.

| Method / route | Behavior |
|---|---|
| GET `/health/live` | Process liveness; no model request |
| GET `/health/ready` | Database connectivity plus configured provider/auth labels |
| GET `/v1/lessons` | Bundled versioned lessons and qualification labels |
| POST `/v1/browser/plan` | Bounded goal text → model-selected browser/HTTPS starting site or clarification; usage-accounted, no session/capture/input grant, no persisted goal |
| POST `/v1/sessions` | Create owner/device/scope-bound lesson; save-progress opt-in |
| GET `/v1/sessions/{id}` | Current owner-scoped version and ephemeral pending step |
| POST `/v1/sessions/{id}/turns` | Multipart `metadata` JSON and `screenshot` PNG/JPEG; guidance/verification |
| POST `/v1/sessions/{id}/questions` | Text question, preserving lesson; no input proposal |
| POST `/v1/sessions/{id}/events` | Sequence-bound idempotent event metadata |
| POST `/v1/sessions/{id}/pause` | Optimistic pause intent; local revoke has already occurred |
| POST `/v1/sessions/{id}/resume` | Return to observing; desktop requires fresh local consent |
| POST `/v1/sessions/{id}/stop` | Terminal Stop intent; late results rejected |
| DELETE `/v1/sessions/{id}` | Delete lesson metadata; retain usage-only records and deletion tombstone |
| POST `/v1/speech/transcribe` | In-memory mono 16kHz 16-bit WAV, at most 30s; transcript needs confirmation |
| POST `/v1/speech/synthesize` | Optional configured TTS WAV; text remains available on failure |

Production requires a signed Cognito access token with RS256, expected issuer, expiry, `token_use=access`, expected public client and `desktop-guide/lessons` scope. ID tokens are not API authorization. Every session is scoped by subject, including Get/delete/events/questions/intents. Mock identities are loopback-only and production startup refuses them.

Turns carry a random request UUID, current session version, observation timestamp, opaque scope/geometry identifiers, event kind and exact previous step. Images are at most 5MiB/16 million pixels; complete request at most 6MiB; JSON metadata at most 64KiB. Multipart parsing stays in memory and never uses upload spooling. Normal clients resize to a maximum dimension of 1600. Future/stale observations outside −5..60s are rejected.

Responses separate `step`, `verification` and `action_proposal`. Verification is one of CONFIRMED, NOT_YET, DIFFERENT_STATE or UNCERTAIN. Confirmation needs current positive evidence and no unmet postconditions; lesson completion also requires the previous step and minimum milestone count. Returned evidence and proposals must bind the current observation. Proposals cannot contain permissions or executable scripts.

Errors contain generic messages without request values/media/provider prompts. 401 requires sign-in; 404 does not disclose another owner's identifiers; 409 requires current state/fresh observation; 429 preserves progress and requires waiting/manual continuation; 503 disarms the client and requires explicit resume. The client does not retry input as a consequence of any HTTP response.

FastAPI `/openapi.json` and `/docs` expose the runnable API specification. `scripts/export-contracts.py` intentionally updates checked-in schemas; `scripts/check-contracts.py` detects drift.
