# Desktop Guide — Complete Codex Build Specification

Specification date: 10 October 2026.
Working product label: Desktop Guide. Keep branding configurable.

## 1. Your assignment and working rules

Build a Windows desktop application that teaches users how to use software and helps them complete tasks through screen understanding, visible movement of the real cursor, step-by-step explanations, outcome verification, and optional voice assistance.

Implement the application, tests, setup scripts, packaging, and infrastructure definitions. Do not stop at architecture documentation or a polished interface backed only by simulated behavior.

Use the implementation defaults below. Record assumptions in `docs/decisions.md`; do not block implementation on optional product choices. Ask only for genuinely missing credentials, account identifiers, deployment approval, or permission for a real desktop test. Continue independent local work when those are unavailable.

Inspect the current workspace and its instructions before changing anything. Preserve unrelated files and uncommitted work. Use a dedicated branch when possible. Do not push, provision paid infrastructure, change DNS, alter existing deployments, or perform destructive operations without explicit approval. Never claim a test, deployment, or desktop interaction succeeded unless it actually ran and its outcome was checked.

Create a concise `AGENTS.md` covering build commands, architecture boundaries, safety invariants, and testing rules. Save this specification as `docs/build-spec.md`. Maintain `docs/implementation-status.md` with completed, in-progress, blocked, and unverified items. Do not put the entire specification in `AGENTS.md`.

Deliver working vertical slices. Keep mock mode explicit and visibly labelled. Missing cloud credentials must not prevent deterministic local testing, but mock tests must never be presented as proof of live integration.

## 2. Product scope and defaults

### Product experience

A user can say, “Teach me to deploy this project using Railway.” The assistant establishes prerequisites, observes the permitted screen, explains the next step, moves the real cursor to the appropriate visible control, and waits for the user to act. It then verifies the resulting state and either advances or helps recover.

This is a teaching assistant, not merely a chat window, prerecorded tutorial, or unattended automation agent. It should answer “Why?”, “What does this mean?”, “Show me where”, and “I did something different” without losing the lesson.

### Defaults

| Decision | Implementation default |
|---|---|
| Platform | Windows 11 x64; qualify a supported Windows 11 build first. |
| Application type | Native desktop application with a floating lesson panel and target overlay. |
| Audience | Individuals learning developer tools and general desktop software. |
| Primary control method | Screenshots and real, visible mouse/keyboard input. |
| Model hosting | Managed Gemini APIs through the user's GCP project and Model Garden. |
| Backend hosting | AWS. Desktop capture and input always remain on the user's machine. |
| Guidance | One meaningful learner action at a time. |
| Default language | English; make localization and optional Malayalam/mixed-language support extensible. |
| Voice | Optional narration and push-to-talk questions. No always-on microphone. |
| Access | Invite-only pilot initially. |
| Capture | Explicitly selected monitor or window; no silent scope expansion. |
| Data | No application-side persistence of raw screenshots or microphone audio by default. |
| Deployment | Generate deployment code and runbooks; applying infrastructure needs approval. |

Do not include payments, organization administration, mobile remote control, unrestricted autopilot, macOS/Linux adapters, fine-tuning, a vector database, or a multi-agent framework in the initial release.

### Interaction modes

| Mode | Allowed behavior | Boundary |
|---|---|---|
| Guide me | Observe, explain, highlight, and point using the real cursor. | No clicks, typing, scrolling, or task-changing key commands. |
| Show me this action | Perform one precisely described, locally approved action, then verify. | Approval is single-use and cannot authorize the rest of the task. |
| Let me try | Observe and provide requested hints or feedback. | No pointer movement unless separately requested. |

Implement Guide me first, then one-action demonstrations, then practice behavior. A teaching step requiring multiple input actions must be decomposed into individually approved actions for demonstration. Never silently switch modes.

## 3. Technology choices and architecture

Use C# with .NET 10 LTS and a compatible stable Avalonia release. Use MVVM and dependency injection. Pin tested SDK/package versions and commit lockfiles. Use the open-source framework; do not introduce paid controls or mandatory developer tooling without approval. Review dependency licensing and telemetry before distribution. [R1, R2]

Use Python 3.12, FastAPI, Pydantic v2, SQLAlchemy, Alembic, and PostgreSQL for the backend. Use `uv` for reproducible Python dependencies. Start with ordinary HTTPS requests. Add SSE only for useful progress/narration streaming, not as a prerequisite to input safety.

Use the official Google Gen AI Python SDK for Gemini, and Google Cloud Text-to-Speech behind a separate optional speech-output interface. Use Cognito for pilot authentication. Use Docker Compose for local backend/database development and the initial single-instance AWS pilot.

```text
Windows desktop application
  UI / lesson panel / captions
  Local session state machine
  Capture + privacy filtering
  User activity observer
  Real cursor + target overlay
  Local permission supervisor
  Serialized input executor
  Local Stop / pause / microphone controls
          |
          | outbound authenticated HTTPS
          v
AWS FastAPI backend
  Authentication and owner-scoped authorization
  Lesson orchestration and validation
  Gemini adapter and optional speech adapters
  Lesson/document retrieval
  Usage ledger and redacted observability
  PostgreSQL metadata
          |
          | short-lived GCP credentials
          v
GCP managed Gemini inference + optional speech synthesis
```

The backend returns recommendations and untrusted action proposals. It cannot authorize local execution. Do not expose an inbound remote-control port on the desktop.

Use one orchestration service with separate planning, guidance, verification, and question-answering functions. These are logical responsibilities, not four mandatory model calls for each step.

## 4. Repository layout and interfaces

Create this structure, adapting naming to existing workspace conventions without weakening module boundaries:

```text
apps/
  desktop/
    DesktopGuide.App/           # Avalonia views, viewmodels, startup
    DesktopGuide.Core/          # sessions, policy, state machine
    DesktopGuide.Platform/      # OS interfaces
    DesktopGuide.Windows/       # capture, input, hotkeys, window tracking
    DesktopGuide.Tests/
  training-sandbox/             # deterministic local practice web app
services/
  api/
    app/
      auth/
      sessions/
      guidance/
      providers/
      verification/
      lessons/
      speech/
      persistence/
      observability/
    tests/
    migrations/
packages/
  contracts/                   # JSON schemas + cross-language fixtures
lessons/
  sandbox/
  notepad/
  vscode/
  railway/
infra/
  aws/pilot/
  gcp/inference/
scripts/
docs/
```

Desktop interfaces: `IScreenCapture`, `IWindowTracker`, `IUserActivityObserver`, `IPointerGuide`, `IInputExecutor`, `ILocalPermissionSupervisor`, `IGuidanceApiClient`, `IAudioCapture`, `ISpeechPlayback`, and `ILocalSessionStore`.

Backend interfaces: `GuidanceProvider`, `LessonPlanner`, `StepVerifier`, `LessonRepository`, `UsageLedger`, `TranscriptionProvider`, and `SpeechSynthesisProvider`.

Use JSON Schema/OpenAPI as the contract authority. Generate types where practical; otherwise enforce matching serialization through shared fixtures in C# and Python. Unknown schema versions must fail safely, not be interpreted optimistically.

No UI viewmodel may call a Win32 input primitive directly. All execution goes through the local supervisor and executor.

## 5. Desktop UI and onboarding

Build a compact floating panel with the task, lesson milestone, current instruction, short explanation, status, optional captions, and a question box. Provide “Show me where”, “Explain more”, “I did this”, “Show me this action”, Pause, and a persistent Stop control. Less-used actions can be in a secondary menu.

The visual target overlay should highlight a point or rectangle and be click-through. It must not intercept the learner's clicks, cover important controls, or steal focus while pointing. The lesson panel remains separately interactive. Provide reduced-motion and high-contrast options, keyboard navigation, scalable text, and text equivalents for narration.

Use calm status messages: looking at your screen, preparing a step, waiting for you, checking the result, waiting for the application, paused for privacy, and stopped. Do not display a fabricated percentage when lesson length can change.

Onboarding must explain capture scope, AWS/GCP processing, pointer permission, demonstration permission, privacy limitations, and Stop. Show a capture preview with the exact permitted region before the first upload.

Provide an offline local practice walkthrough before connecting cloud inference. Do not activate observation or input on login, installation, startup, or microphone permission alone.

## 6. Capture, geometry, and cursor behavior

### Capture pipeline

Implement `Windows.Graphics.Capture` with supported Win32 interop and explicit source selection. Runtime-check support and handle access denial or unusable frames. A GDI/`CopyFromScreen` fallback is acceptable for a clearly identified, consented monitor-capture mode; it must not masquerade as privacy-isolated window capture. [R3]

Keep capture buffers bounded. Capture one selected source, apply local privacy masks, remove the assistant's overlays where supported, then resize/encode for inference. Do not upload every captured frame. Prefer PNG for text-heavy screens and configurable image resizing; initial long-edge target may be around 1600 pixels, with higher-detail crops when needed.

Try supported capture-exclusion mechanisms for the assistant's own windows. Test actual behavior. Capture exclusion is not a general security or DRM guarantee. If a frame contains an overlay or obstruction that prevents targeting, relocate the panel and re-observe rather than inventing hidden content. [R4]

For window capture, never execute against content that is minimized, covered, outside the chosen source, or not the actual foreground target. New out-of-scope dialogs or applications require scope consent. Monitor capture can support multiple applications only within the scope the user accepted.

### Coordinate contract

Use one internal coordinate convention: floating-point positions normalized to `[0,1]` relative to the transmitted source image. Convert any provider-native convention once inside its adapter.

Record image size, source physical rectangle, virtual desktop origin, monitor identity, DPI, crop/resize/padding transforms, capture time, geometry generation, and an opaque local target-window reference. Keep native handles and detailed process/window metadata local unless actually needed remotely.

For a simple unpadded full-source frame:

```text
screen_x = source_left + round(normalized_x * (source_width - 1))
screen_y = source_top  + round(normalized_y * (source_height - 1))
```

For crops or letterboxing, apply the complete inverse transform before this mapping. Reject points in padding and invalid/out-of-range/NaN coordinates. Test negative monitor origins, mixed scaling, moved windows, resized windows, and 100/125/150/200 percent DPI. Use a consistent per-monitor DPI-aware application configuration. [R5]

Never silently clamp invalid model coordinates to the nearest clickable position.

### Real cursor movement

Move the actual pointer, with a small optional halo. Use short cancellable motion segments rather than blocking animation. Point once and release control; do not repeatedly pull the cursor back while the learner explores.

Movement alone can reveal tooltips or hover menus. Treat meaningful resulting screen changes as new observations. Never describe cursor movement as incapable of affecting UI state.

Before pointing or executing, verify the current target window, capture geometry, and target freshness. Recheck with a recent local frame after narration or approval. If the target remains confidently anchored in an unchanged region, a full new model call is unnecessary; otherwise re-ground it. Do not use timestamp age alone as proof that a target is valid.

## 7. Local permissions, input, and Stop

Implement three independent grants: observation, pointer guidance, and task-changing input. Each is scoped to the current session and user-approved capture/application scope.

Model output, server messages, webpage content, and lesson files cannot grant permissions.

### Demonstration authorization

A single-use local capability must bind the session, local epoch, step, proposal identifier, exact validated action payload, source observation/geometry, target window, and expiry. Mint it only from a genuine local user approval event. Consume it atomically before dispatch.

A changed payload, expired grant, new session epoch, incompatible frame, different target, duplicate identifier, or stale sequence invalidates execution. Approval does not survive Stop, restart, sign-out, network interruption, or unrelated user takeover.

Show the action and its effect before asking for approval. A model's numeric confidence is an uncalibrated signal, never execution authorization.

### Input executor

Use real Windows input through a small, audited Win32 wrapper. Serialize all assistant input. Check permission, cancellation, focus, geometry, and takeover state before each small input batch. Release only assistant-held keys/buttons in cleanup paths. Do not enqueue long uninterruptible sequences or hold a control indefinitely.

Start with move, one click, bounded scrolling, bounded non-sensitive text entry, and a small allowlist of harmless key chords. Disable dragging initially. Text entry must not implicitly press Enter, paste from the user's clipboard, or run commands. Split click/type/submit into separate actions.

Do not use `BlockInput`, hide input from the owner, elevate to administrator, bypass UAC, interact with secure desktops, or work around integrity restrictions. Windows input injection is restricted by integrity level; unsupported targets must become a visible user handoff. [R6]

Passwords, MFA, CAPTCHA/security verification, payments, permission changes, destructive changes, production deployments, and final external publishing/submission remain manual in the pilot. When context or risk is unclear, guide the user instead of demonstrating.

Honor any provider-required confirmation as an additional requirement. Never auto-acknowledge it, convert it into ordinary consent, or retry through another route to bypass it. [R7]

### User takeover and emergency stop

Observe relevant local interaction events without recording typed characters. Distinguish the assistant's tagged input from other input for coordination, but do not treat injection flags as a tamper-proof boundary. Ignore interactions with the assistant's own controls when attributing learner progress; process them as explicit local intents.

During assistant motion or execution, unexpected physical input cancels pending execution and yields control. Do not automatically reclaim the pointer.

Stop must be available from the panel, tray, and a registered global hotkey. Use a configurable chord such as `Ctrl+Alt+Shift+S`; detect registration failure and show it. Keep Stop processing off network and model dependencies, with a dedicated input/message path and short executor batches.

On Stop: invalidate the local epoch, revoke grants, cancel outstanding local work, discard late model responses, clear queued actions, stop capture/upload and microphone recording, stop narration, and perform best-effort release of assistant-held controls. In-flight bytes or input already submitted to external systems cannot be recalled; do not promise otherwise.

App crash, lock, sleep, user/session switch, loss of an allowed target, or executor heartbeat failure must leave input disarmed. Resumption always requires local user intent and fresh observation.

## 8. Session state machine and orchestration

Implement a deterministic, unit-testable state machine:

```text
IDLE -> CONSENT -> OBSERVING -> PLANNING -> PRESENTING
     -> POINTING -> WAITING_FOR_LEARNER -> VERIFYING
     -> PRESENTING_NEXT_STEP or COMPLETED

Optional branches:
ANSWERING_QUESTION
WAITING_FOR_APPLICATION
RECOVERING
AWAITING_DEMONSTRATION_APPROVAL -> DEMONSTRATING -> VERIFYING
PRIVACY_PAUSED
PAUSED
NETWORK_INTERRUPTED
STOPPED
```

Only the local application may enter an input-enabled state. A server response cannot transition directly into demonstration.

Waiting for the learner is normal. Do not count ordinary lesson steps as exceptional handoffs or end a session because the user needed several explanations. Use a configurable inactivity privacy-pause, not a model-call loop or unexplained task failure.

At each turn, send the goal, language/detail preference, compact verified progress, current step, relevant user event, current permitted screenshot, and small relevant lesson/document excerpts. Do not resend an unlimited screenshot history or the whole conversation.

Use event-driven verification: learner click, typing activity ending, relevant window change, meaningful screen change, or explicit “I did this”. Debounce events. Clicking the expected point is evidence of an attempt, not proof of success.

Allow a combined response that verifies the previous action and proposes the next teaching step. Validate the verification result first; do not display the next instruction when the previous step is uncertain.

A learner's question temporarily suspends pointing/execution. Answer it, re-observe if needed, and resume the same lesson context. A new task requires an explicit switch; changing the goal invalidates pending proposals.

## 9. Data contracts and verification

Define these versioned contracts:

| Contract | Required content |
|---|---|
| Session | Owner, device, goal, mode, language, detail level, allowed scope, session version, state. |
| Observation | Identifier, capture timestamp, opaque scope, geometry identifier, transform metadata, privacy status, image attachment. |
| GuidanceStep | Identifier, source observation, instruction, short explanation, target or null, expected learner action, observable postconditions, hints, supporting evidence, uncertainty. |
| ActionProposal | Identifier, source observation, exact primitive/payload, target, proposed risk, provider confirmation information. |
| VerificationResult | Status, observed evidence, unmet postconditions, next recovery suggestion, verifier provenance. |
| LearnerEvent | Identifier, sequence, local epoch/session version, kind, timestamp, minimal non-sensitive event data. |
| ActionReceipt | Proposal identifier, local authorization outcome, dispatch outcome, and subsequent verification reference. |

Example guidance payload, using a synthetic practice screen rather than a fixed live-site layout:

```json
{
  "schema_version": 1,
  "session_id": "11111111-1111-4111-8111-111111111111",
  "session_version": 4,
  "turn_id": "22222222-2222-4222-8222-222222222222",
  "observation_id": "33333333-3333-4333-8333-333333333333",
  "step": {
    "id": "44444444-4444-4444-8444-444444444444",
    "instruction": "Open the Settings tab.",
    "explanation": "This is where you change this project's configuration.",
    "target": {
      "kind": "rectangle",
      "coordinate_space": "normalized_source_image",
      "geometry_id": "55555555-5555-4555-8555-555555555555",
      "bounds": {"left": 0.71, "top": 0.12, "right": 0.81, "bottom": 0.18},
      "visible_label": "Settings"
    },
    "expected_user_action": "click",
    "postconditions": [{"kind": "visible_text", "value": "Project settings"}],
    "hints": ["Look for Settings near the top of the project view."],
    "evidence": [{"kind": "screen", "observation_id": "33333333-3333-4333-8333-333333333333", "detail": "Settings tab is visible."}],
    "uncertainty": []
  }
}
```

Guidance is not an executable action. Keep action proposals separate. Server validation must cover schemas, target bounds, image references, output size, unexpected actions, and malformed data; repeat critical validation locally.

The verifier must distinguish:

```text
CONFIRMED
NOT_YET
DIFFERENT_STATE
UNCERTAIN
```

Track user-attested completion separately from verified completion. “I did this” triggers a check; it does not automatically produce `CONFIRMED`. A user may explicitly skip a step, but it must be recorded as skipped rather than successful.

Use deterministic observable checks when available, plus Gemini for visual interpretation. The model must state observable evidence rather than merely saying “done”. A finished spinner, a clicked button, or the absence of an error is not automatically proof of the intended result.

Wrong turns should produce a short explanation and a recovery step based on the actual screen. Do not keep replaying the original path. Never automatically replay an uncertain send, submit, save-overwrite, deletion, or deployment action.

## 10. Gemini integration and grounding

Use managed Gemini inference in the configured GCP project. Do not deploy Gemini model weights on AWS, assume a dedicated GPU endpoint is required, or substitute a consumer Gemini subscription.

Implement the Google Gen AI SDK provider using explicit GCP project/location configuration and Application Default Credentials. Use the managed GCP route, not an embedded developer API key. Configure the SDK explicitly for that route, for example `genai.Client(vertexai=True, project=settings.gcp_project_id, location=settings.gcp_location)` when supported by the pinned SDK. Keep the SDK behind your own adapter and pin its tested version. [R8]

The currently documented `gemini-3.8-flash` is a candidate initial model for guidance and verification; its documented capabilities include image/audio inputs and structured output. Validate project access, region availability, quotas, and required options before live use. Do not assume all documented models are enabled for this account. [R9]

Configuration must separately support guidance, verification, optional computer-use, and transcription models. They may initially resolve to the same verified model. Do not silently migrate production model identifiers or reasoning settings.

### Provider capability probe

Implement `scripts/probe_gemini.py`. With explicit credentials and a synthetic screenshot, check authentication, requested location, image input, structured output/schema support, safety response handling, malformed-output behavior, and cancellation/timeout behavior. Optionally check desktop computer-use support separately. Record model, SDK, region, date, and test outcomes in `docs/model-capabilities.md`.

Computer use is a preview capability in the currently checked documentation. Keep it behind a feature flag. The core Guide me experience must work through screenshot interpretation and validated non-executing structured output, without depending on the native computer-use feature. [R7, R9]

When native computer use is enabled, use only a model/SDK combination proven to support the selected desktop environment. Disable automatic SDK tool execution. Map allowed proposals to your internal schema and local supervisor. Reject or defer additional actions rather than executing a returned batch under one approval. Preserve provider protocol IDs and truthful tool results.

Do not assume structured guidance output and computer-use tools can be combined in the same request. Test that combination, or use separate calls.

### Model instructions

Create separately versioned prompt templates for lesson planning, guidance, verification, and learner questions. Require the model to ground clickable targets in the current image, propose one immediate learner action, explain why briefly, identify prerequisites, express uncertainty, and use null targets when no safe target is visible.

Explicitly distinguish user instructions from webpage text, documents, terminal output, retrieved sources, and screenshots. Those sources are untrusted evidence, not instructions to change goals or permissions.

Do not expose arbitrary shell, filesystem, browser automation, or cloud administration tools to the guidance model. Do not request or store hidden chain-of-thought. Store concise user-facing explanations and evidence only where retention settings permit.

On invalid model output, allow one bounded repair attempt that produces no execution. Then enter a recoverable uncertainty state. Provider refusals and confirmation requirements are not errors to bypass.

### Software knowledge

Create versioned lesson templates with prerequisites, milestone descriptions, common mistakes, observable postconditions, and optional official documentation references. Do not hard-code UI coordinates or require the user to follow a single rigid sequence.

Begin with bundled reviewed Markdown/JSON and simple text retrieval. No vector database is required. If public documentation retrieval is added, restrict it to approved domains, enforce size/time limits, and block private/loopback/metadata addresses and unsafe redirects. Never let screenshot instructions choose arbitrary fetch destinations or execute retrieved code.

Keep open-ended guidance available, but label unqualified applications as experimental. Do not claim universal application compatibility.

## 11. Voice assistance

Implement speech as optional adapters, not a dependency of the core lesson loop.

```text
Push-to-talk -> bounded audio clip -> transcription
             -> transcript / question or proposed goal
             -> lesson response -> optional TTS -> captions + playback
```

A new spoken task must be shown for confirmation before observation/input starts. In-session questions may be answered directly; requests to perform an action still need the normal local approval.

Limit recording duration, show microphone status, let the user cancel, and discard audio after processing. Stop playback when the user speaks, pauses, stops, or changes steps. Do not let old narration continue after its target becomes stale.

Use Google Cloud Text-to-Speech for the first cloud speech-output adapter. Discover/configure a supported voice rather than hard-coding an assumed voice name. Malayalam voices are documented, but technical vocabulary and mixed-language quality must be evaluated before claiming support. [R10]

Ship English first. Keep language settings and Unicode handling ready for Malayalam/mixed-language testing. Provide text-only fallback for missing microphone permission, unavailable voice, provider failure, or poor transcription. Voice “Stop” is a convenience; local button/hotkey Stop remains independent.

## 12. Backend API, consistency, and persistence

Implement authenticated, versioned endpoints:

| Endpoint | Responsibility |
|---|---|
| `GET /health/live` | Process health; no sensitive details. |
| `GET /health/ready` | Dependency readiness; do not spend model tokens on every health check. |
| `POST /v1/sessions` | Create a lesson session owned by the authenticated user/device. |
| `GET /v1/sessions/{id}` | Return permitted sanitized session metadata. |
| `POST /v1/sessions/{id}/turns` | Accept one multipart observation + bounded context and return validated guidance/verification. |
| `POST /v1/sessions/{id}/questions` | Answer a learner question using the current authorized lesson context. |
| `POST /v1/sessions/{id}/events` | Record deduplicated minimal learner/state/action receipts. |
| `POST /v1/sessions/{id}/pause` | Record pause; actual local pause happens before this request. |
| `POST /v1/sessions/{id}/resume` | Coordinate resumption; never arm local input. |
| `POST /v1/sessions/{id}/stop` | Record Stop; actual local Stop happens first. |
| `DELETE /v1/sessions/{id}` | Delete retained lesson data subject to documented backup retention. |
| `GET /v1/lessons` | List available qualified lesson templates. |
| `POST /v1/speech/transcribe` | Transcribe bounded audio. |
| `POST /v1/speech/synthesize` | Produce optional narration. |

No cloud endpoint may directly execute local input.

Use one in-flight model turn per session and optimistic session-version checks. Include idempotency keys and payload digests. A conflicting payload using the same key must be rejected. Never hold a database transaction open throughout a model request.

Retain retryable response bodies only in bounded per-session memory initially. Persist identifiers/status/digests and sanitized outcomes, not raw image/audio payloads. If an idempotent response body expired or the process restarted, return a clear re-observation requirement rather than inventing a response or replaying input.

Action execution remains at-most-once at the local dispatch boundary, not “exactly once” across crashes and external applications. An unknown outcome requires verification, not automatic repetition.

Database tables: users, devices, sessions, session_turns, lesson_steps, session_events, model_calls, lesson_templates, documentation_sources, and consent_records. Include UUID primary keys, owner scope where applicable, timestamps, foreign keys, and migrations.

Important constraints/indexes: unique identity subject; session ownership/device indexes; `(session_id, step_sequence)`; unique `(session_id, event_id)`; unique `(session_id, idempotency_key)` on session_turns; model_calls linked to a turn with attempt numbers; model-usage indexes by owner and timestamp. Derive owner identity from validated authentication, never a caller-supplied owner field.

Persist completed milestones and user preferences with appropriate consent. Never persist an armed capability. After restart, recover summaries but create a fresh local epoch and observe again.

## 13. Authentication, privacy, and security

Use a Cognito public app client, external system browser, authorization code flow, and PKCE. No client secret belongs in the desktop executable. Validate state/nonce as appropriate, exact redirect configuration, token issuer, signature, token use, client/audience binding, expiry, and scopes. Use a supported registered native callback or loopback pattern and test it end to end. [R11]

Keep access tokens in memory. If “remember me” is enabled, protect refresh tokens using Windows current-user secure storage and revoke/clear them on sign-out as supported. Do not store tokens in plain JSON, logs, or Git.

Provide a loopback-only development auth mode that is visibly labelled. Production must refuse to start with mock auth/provider enabled unless explicitly built as a non-public demonstration environment. Fail configuration validation rather than silently bypassing authentication.

### Retention defaults

| Data | Application default |
|---|---|
| Raw screenshots | Request/session memory only; bounded, not in databases, logs, backups, or object storage. |
| Microphone clips | Transient processing only. |
| Raw model prompts/responses | Not logged by default; sanitize anything deliberately retained. |
| Lesson progress and summaries | Optional saved progress with configurable retention; default 30 days. |
| Operational diagnostics | Minimal metadata; initial retention 7 days. |
| Debug screenshots | Explicit separate consent, preview, access control, and short TTL. Disabled by default. |

Inspect reverse-proxy buffering, framework multipart spooling, tracing, crash dumps, exception handlers, temporary files, and provider request logging. “We do not save screenshots” must be true across those paths, not merely the main database. Use bounded memory processing or explicitly configured ephemeral storage where necessary, with accurate disclosure.

Separate application retention from provider retention. Google documents additional retention conditions, including abuse monitoring and optional logging/caching features; do not advertise unconditional zero retention or country-only processing without verifying the actual configuration and applicable terms. [R12]

Pause capture for sensitive entry and offer manual privacy pause/masks. Automated password or secret detection is best effort, not a guarantee. Do not ingest clipboard contents, credential stores, browsing history, or unrelated local files.

Enforce ownership on every endpoint, request/image/audio limits, decoded-image dimension limits, rate limits, safe parsing, HTTPS, and least-privilege roles. Initial upload limits: one screenshot per ordinary turn, at most 5 MiB per image, and at most 30 seconds of microphone audio; bound decoded pixels and audio bytes independently. Any optional extra evidence image needs an explicit contract limit. Protect against prompt injection using local permissions, constrained tools, untrusted-source separation, and negative tests—not a claim that a system prompt alone solves it.

## 14. AWS and GCP infrastructure

### Initial AWS pilot

Create a separate Terraform root for a new pilot deployment. Use configurable region `ap-south-1` as the initial proposal. Do not modify unrelated cloud resources.

Provision one configurable EC2 instance running the API, reverse proxy, and PostgreSQL containers; encrypted EBS; an instance role; Cognito; a private encrypted/versioned S3 bucket for releases and encrypted database backups; CloudWatch logs/alarms; and restricted security groups.

Expose HTTPS only, plus HTTP solely when required for certificate issuance/redirect. Never expose PostgreSQL. Prefer Systems Manager for administration rather than a public SSH rule. No Docker socket in the API container. Run the API as a non-root user and restrict container filesystems/capabilities where practical.

Use a configured domain and valid TLS certificate before public testing. Missing domain/account details should block deployment, not local development. Generate the required configuration/runbook rather than inventing identifiers.

This is a single-instance pilot, not high availability. Add daily encrypted database backups, lifecycle rules, a tested restore script, and a clear deletion/backup-retention policy. Before a larger public release, move persistence to managed PostgreSQL and validate multi-instance coordination. ECS/Fargate is a later migration, not a prerequisite to the first working lesson.

Generate cost estimates from current account/region information before approval. Include compute, disk, public IP/network, database, logging, backups, and inference usage. Budget alerts are not hard spending guarantees.

### GCP inference

Create a separate Terraform root for project API enablement, a least-privilege inference service account, Workload Identity Federation, and bindings restricted to the intended AWS account/runtime role.

Use AWS temporary role credentials exchanged for short-lived GCP credentials; do not create or distribute a long-lived service-account key. Use an external-account credential configuration and verify refresh in the actual runtime container. Google documents EC2 federation and IMDSv2 configuration. [R13]

Require IMDSv2 and test metadata access carefully with the chosen container networking. Do not weaken host credential exposure casually to fix an authentication issue. Any later move to a different AWS runtime requires revalidation of its credential supplier and refresh behavior.

Select the model location explicitly. `global` is only a configurable development candidate; do not infer India-only processing from the AWS region. For customer pilots, resolve residency and provider availability before sending real data.

A desktop package contains only public configuration, such as API origin and public Cognito identifiers. No AWS/GCP private keys or deployment credentials may be bundled.

### Deployment workflow

CI may build, test, scan, and produce artifacts. Deployment is a separate, approval-controlled job. Run Terraform format/validate and a reviewed plan; never use unconditional `apply -auto-approve` as part of ordinary coding.

Provide environment-specific variables, migration/backup steps, smoke tests, rollback instructions, and deletion protection for retained data. Distinguish “infrastructure code generated”, “plan checked”, “resources applied”, and “live smoke test passed”.

## 15. Performance and failure handling

Use event-triggered uploads rather than continuous remote video analysis. Keep local change detection cheap. Start with configurable debounce around 500 milliseconds and bounded observation frequency while waiting for application changes; tune using measured behavior rather than universal fixed sleeps.

Proposed engineering targets, not guarantees: local Stop invalidation within 100 milliseconds after handling the event; input cessation within 250 milliseconds in controlled stress tests; responsive UI throughout model calls; and an initial guidance-turn target of under 3 seconds median and under 8 seconds p95 on the qualified environment. Publish measured values and limitations.

Keep at most a few recent frames in memory. Send the current frame and only necessary earlier evidence. Use target crops for detail instead of repeatedly sending large full-screen histories. Do not buy low latency by weakening safety or using an unqualified model.

Implement a configurable usage ledger with per-session/user limits, bounded output size, timeouts, and bounded retries. Initial configurable limits should include 60 model turns per lesson, at most two transient retries per request, one structured-output repair attempt, and one in-flight turn per session. Track reading/waiting separately from model activity. A learner reading a step must not trigger repeated billable requests. On a limit, preserve progress and offer a clear manual continuation choice rather than silently extending the budget.

For transient provider failures, use limited exponential backoff with jitter. For authentication/configuration errors, fail clearly without a retry storm. For an unknown action outcome, verify before deciding what happened. For network loss, disarm demonstrations immediately, retain the visible lesson, and resume only through fresh local intent/observation.

Record timings for capture, preprocessing, upload, inference, validation, presentation, and verification. Record selected model/version and usage metadata without raw sensitive content. Distinguish slow inference from bad target grounding and application loading.

## 16. Initial lessons and deterministic training sandbox

Build a small local training application with tabs, menus, forms, disabled controls, loading delays, dialogs, wrong-turn paths, and explicit success/error states. Use ordinary HTML/CSS/JavaScript; a large frontend framework is unnecessary.

The production assistant must use its screenshot/input path against this sandbox. Tests may inspect an isolated oracle for ground truth, but the assistant must not read that oracle, DOM selectors, or hidden success flags to solve the task.

Implement these initial lesson packs:

| Lesson | Purpose |
|---|---|
| Training sandbox | Deterministic tests of guidance, wrong turns, approvals, latency, and verification. |
| Notepad practice | Create and save a harmless document in a user-approved temporary practice folder. |
| VS Code basics | Open a practice folder/file, navigate the interface, edit harmless text, and save. No automatic terminal commands. |
| Railway walkthrough | Explain prerequisites, navigate the current visible interface, select a disposable project/repository, and guide configuration. User manually handles sign-in, secrets, billing/resource creation, and final deployment confirmation. |

Do not guess today's Railway interface or fabricate a successful deployment. Keep docs/source dates and validate the actual visible state. A deployment lesson is complete only when appropriate evidence supports the claimed outcome; otherwise report exactly what was and was not verified.

Record lesson version, qualified application/build where obtainable, last verification date, prerequisites, risk boundaries, and known limitations.

## 17. Testing and acceptance criteria

Separate tests into deterministic unit/contract tests, offline screenshot tests, optional live-provider tests, and explicit real-Windows integration tests.

### Required negative tests

Cover unauthorized input in Guide/Practice modes; duplicate and expired approvals; action payload mutation; mismatched session/device/epoch; out-of-order/late responses; changed DPI/window/capture scope; wrong-target hit tests; invalid coordinates; Stop during movement/typing/inference; physical takeover; network loss; app lock/sleep; refused model output; provider-required confirmation; and prompt-injection text visible on screen.

Also cover tenant isolation, cross-user identifiers, malformed uploads, decompression/oversized image limits, idempotency collisions, credential refresh, refresh failure, and accidental screenshot/audio leakage through logging or temporary storage.

### Verification tests

A wrong click, disabled button, failed submission, unrelated success banner, stale screenshot, or user pressing “I did this” must not falsely confirm a step. Test alternate successful paths and genuine uncertainty. Test learner questions and several consecutive recovery steps without prematurely ending the lesson.

### Desktop tests

Exercise supported DPI settings, moved/resized windows, negative monitor origins, target occlusion, the assistant panel gaining focus, capture exclusion behavior, Unicode entry, microphone denial, and global-hotkey registration conflicts.

Normal CI uses fake executors and synthetic fixtures. Real cursor/input tests require a disposable Windows session or VM and explicit `--allow-real-input` consent. Never run them automatically against the developer's active desktop.

### Evaluation gates

Build a held-out screenshot/step set across the qualified workflows. Initial quality target: at least 95 percent correct target selection on the labelled supported set, with dataset size and uncertainty reported. Do not optimize against the held-out set by hard-coding its layouts.

Require zero unauthorized dispatches and zero critical false-completion cases in the defined safety suite. These are finite test results, not universal guarantees. Record end-to-end success and recovery rates, explanation usefulness, voice intelligibility, latency, and cost per completed lesson.

A release must identify which checks ran with mocks, live Gemini, a real Windows desktop, and deployed infrastructure. An unavailable test is “not run”, not “passed”.

## 18. Implementation phases

Complete phases in dependency order. Each phase must leave the application buildable and include tests.

| Phase | Build | Exit evidence |
|---|---|---|
| 0. Foundation | Workspace inspection, architecture decisions, solution/API skeleton, schemas, local DB, mock provider, CI, scripts. | Reproducible builds and shared-contract tests pass. |
| 1. Local safety | Permission supervisor, state machine core, serialized fake executor, Stop/epoch logic, takeover handling. | Negative/race tests reject all unauthorized dispatches. |
| 2. Windows perception/UI | Capture adapter, geometry mapper, source preview, panel, overlay, real pointing behind consent. | Synthetic geometry tests plus explicit Windows smoke check. |
| 3. Gemini backend | Managed-GCP adapter, prompts, validation, usage limits, capability probe, verification contracts. | Offline tests pass; live synthetic-image probe recorded when credentials exist. |
| 4. Guided learning | Event-driven observe/explain/point/wait/verify loop, questions, wrong-turn recovery, pause/resume. | Complete sandbox lesson through the real product path. |
| 5. Demonstrations | Exact-payload local approvals, one-action execution, receipts, post-action verification. | Approval expiry/replay/takeover/Stop tests and explicit safe Windows demonstration. |
| 6. Voice and practice | Push-to-talk, optional narration, captions, interruption handling, practice mode. | English voice and text-only fallbacks tested; other languages labelled by evidence. |
| 7. Accounts/persistence | Cognito login, owner isolation, secure token storage, saved progress, retention/deletion. | Auth and cross-user tests pass; restart always disarmed. |
| 8. Qualified lessons | Notepad, VS Code, Railway lesson packs and documentation evidence. | Supported-workflow evaluation report, including failures. |
| 9. Pilot delivery | AWS/GCP Terraform, backup/restore, packaging, signing hooks, release artifacts, runbooks. | Local artifacts verified; cloud apply/smoke tests only after approval. |
| 10. Hardening | Performance, privacy audit, failure/race tests, accessibility, upgrade/rollback. | Pilot acceptance report with no unresolved critical safety defects. |

Do not polish optional branding or dashboards while the observe → guide → user action → verify loop remains fake or incomplete.

## 19. Configuration, scripts, and packaging

Provide `.env.example` for the backend and a public desktop configuration template. Separate public settings from secrets.

```dotenv
APP_ENV=development
AUTH_MODE=mock
MODEL_PROVIDER=mock
DATABASE_URL=postgresql+psycopg://desktopguide:development-only@localhost:5432/desktopguide
AWS_REGION=ap-south-1
GCP_PROJECT_ID=
GCP_LOCATION=global
GOOGLE_GENAI_USE_VERTEXAI=true
GEMINI_GUIDANCE_MODEL=gemini-3.8-flash
GEMINI_VERIFICATION_MODEL=gemini-3.8-flash
GEMINI_TRANSCRIPTION_MODEL=gemini-3.8-flash
GEMINI_COMPUTER_USE_ENABLED=false
GEMINI_COMPUTER_USE_MODEL=
GOOGLE_APPLICATION_CREDENTIALS=
COGNITO_ISSUER=
COGNITO_CLIENT_ID=
COGNITO_SCOPES=
SPEECH_OUTPUT_ENABLED=false
TTS_VOICE_NAME=
RAW_MEDIA_PERSISTENCE=false
DEBUG_MEDIA_ENABLED=false
SESSION_HISTORY_RETENTION_DAYS=30
OPERATIONAL_LOG_RETENTION_DAYS=7
```

The sample database credentials are local-development placeholders only. Production startup must reject insecure defaults. Validate actual model/region availability before changing provider mode to live.

Provide these working entrypoints with documented prerequisites:

```text
scripts/dev-up.ps1
scripts/dev-down.ps1
scripts/test-all.ps1
scripts/probe_gemini.py
scripts/run-windows-smoke.ps1
scripts/package-windows.ps1
scripts/backup-database.sh
scripts/restore-database.sh
scripts/validate-infra.ps1
```

Provide preview/mock startup modes that remain disarmed. Package a self-contained Windows x64 build. Include code-signing integration hooks and clearly mark unsigned development builds; do not claim they are signed.

Before wider distribution, validate installer/update signatures and artifact integrity. Updates must not execute during an active lesson or inherit input permissions. A manual signed-update flow is acceptable initially; do not build an insecure unsigned auto-updater.

## 20. Definition of done and final handoff

The pilot is functionally complete only when a user can install it, understand and grant capture/pointer permissions, start a goal, receive screen-grounded explanations, watch the real pointer indicate the correct control, perform the action, and receive verified feedback. The user must be able to ask questions, make a recoverable mistake, pause, Stop, and optionally approve one safe demonstration.

The core experience must work without voice. It must tolerate ordinary waiting without repeated model calls and recover from network interruption without replaying actions. Saved progress must never restore execution permission.

Required deliverables: source code, pinned dependencies, tests and fixtures, sample lessons, deterministic sandbox, local setup scripts, API documentation, architecture/decision records, safety and privacy threat model, model capability report, implementation status, infrastructure definitions, packaging artifacts where buildable, evaluation results, and deployment/rollback/backup runbooks.

At the end of an implementation session, report concrete files/features completed, exact build/test commands and outcomes, skipped or blocked checks, known limitations, required credentials/approvals, and the next unfinished phase. Do not label generated infrastructure as deployed or a mock demonstration as a working live desktop assistant.

Start with Phase 0 and the local safety foundation. Proceed through the phases without repeatedly asking to continue. Request user input only at real authorization or configuration boundaries, while continuing independent work wherever possible.

---

## Official technical references

Checked 10 October 2026. Recheck model, SDK, region, and service-specific details during implementation; project-specific capability tests remain required. The references support technology capabilities and constraints, not a claim that this application has already been built.

```text
[R1] .NET support
https://learn.microsoft.com/en-us/dotnet/core/releases-and-support

[R2] Avalonia supported platforms
https://docs.avaloniaui.net/docs/supported-platforms

[R3] Windows screen capture
https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture

[R4] Capture exclusion limitations
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity

[R5] Windows high-DPI development
https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows

[R6] SendInput and integrity restrictions
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput

[R7] Managed Gemini computer use
https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/computer-use

[R8] Google Gen AI SDK
https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/sdks/overview

[R9] Gemini 3.8 Flash capabilities
https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/gemini/3-8-flash

[R10] Google Cloud Text-to-Speech voices
https://docs.cloud.google.com/text-to-speech/docs/list-voices-and-types

[R11] Cognito PKCE
https://docs.aws.amazon.com/cognito/latest/developerguide/using-pkce-in-authorization-code.html

[R12] GCP provider retention conditions
https://docs.cloud.google.com/gemini-enterprise-agent-platform/resources/zero-data-retention

[R13] AWS-to-GCP Workload Identity Federation
https://docs.cloud.google.com/iam/docs/workload-identity-federation-with-other-clouds

[R14] Codex AGENTS.md instructions
https://developers.openai.com/codex/guides/agents-md/
```
