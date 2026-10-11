# Desktop Guide: Windows companion implementation

Reference reviewed 11 October 2026. This document refines the original ten-phase specification using the user's HeyClicky reference and subsequent choices. The original specification is preserved. **This is a development pilot, not full HeyClicky feature parity or a qualified release.**

## What the reference establishes

HeyClicky's public product combines a desktop companion, voice/text requests with screen context, teaching and spatial annotations. Its public download currently targets macOS Sonoma 14.2 or later; Windows is a waitlist. The website demonstrates guidance over applications, rather than requiring users to work inside a separate lesson dashboard. These are public demonstrations and product descriptions, not an independent installed-app test. [Product and demonstrations](https://www.heyclicky.com/)

The October changelog describes a much wider product: named agents, connected accounts, routines, floating task cards and continued voice conversations. It also records cursor/capture overlap problems and changes to the agent cursor. The central interaction is the useful reference; every ancillary feature is not a prerequisite for our teaching pilot. [Changelog](https://www.heyclicky.com/changelog)

HeyClicky describes hotkey-triggered screen context, cloud processing, and retention of prompt/analysis data while excluding raw screenshots. Desktop Guide must describe its own behavior accurately: a started guide repeatedly samples the authorized window until paused/stopped; inference uses AWS and managed Gemini. We cannot import another product's privacy claims. [Trust page](https://www.heyclicky.com/trust)

Use original Desktop Guide branding and implementation. Do not copy proprietary assets or infer undocumented internals from marketing demos.

## Product decisions already supplied by the user

- A prompt opens the proposed browser/site automatically. Chrome, Edge and Firefox are supported if installed. Railway is recognized by name; other destinations currently need one explicit HTTPS URL.
- The learner clicks, types, creates resources and deploys. Prompt launch grants no physical cursor movement or website-click authority.
- Each next action comes from a fresh image and the stated goal. No offline walkthrough, fixed Railway click sequence or DOM answer oracle.
- A visible animated guide cursor, action comment and target outline identify the suggested control. This drawn cursor is independent of the real mouse.
- Prefer a cheaper Gemini model, with bounded inference and explicit uncertainty. An unavailable model stops live guidance; there is no silent expensive-model or offline fallback.

## The intended Windows experience

1. Start Desktop Guide and sign in with the invited account. A small floating dock remains available; the larger panel holds setup, account and preferences.
2. Enter “Open Chrome and guide me through deploying my example on Railway.” The app signs in if necessary, opens one new Chrome window, binds that exact window, and starts its disclosed live observation. No tab picker is needed.
3. Alternatively, press **Ctrl + Alt + Space** in an existing application. The companion names that window before any capture. Enter a goal and press **Help with this window** to authorize its live images.
4. The request card collapses and the selected application is brought forward when Windows permits. The model interprets a current image, returns a validated step, and the app draws a cursor, ring, outline and short comment over the target.
5. The learner acts. Activity/change detection requests a new image; the model verifies the visible result before suggesting another step. **Check screen** provides an explicit refresh. Ambiguous or changed targets are withheld.
6. The dock shows status and always offers Stop. Expand for instructions, a question, recording, pause or a new task. Pause stops capture; resuming requires fresh consent. Stop also cancels an unfinished sign-in/browser launch and rejects late results.

The prompt disclosure names the cloud image-processing boundary. Privacy masks and the manual source/preview flow remain in the main panel for users who need them. The hotkey alone only resolves a window identity; it is not observation consent.

## Implemented in this iteration

| Area | Implementation | Evidence / limit |
|---|---|---|
| Desktop companion | `CompanionWindow.axaml`, dock/card, current-window context, live status, questions, pause/resume/Stop, tray entry | Real Avalonia/Skia render checks; native multi-monitor placement still open |
| Invocation and focus | `ICompanionHotkey`, Windows Ctrl+Alt+Space, foreground source registration, explicit start/focus handoff | Summoning grants nothing; canceled sign-in cannot start capture. Actual native shortcut/focus flow still needs qualification |
| Prompt launch | `BrowserLaunch` + `WindowsBrowserLauncher`, allowlisted installed browser, HTTPS validation, exact new HWND/executable binding | Parsing/late-completion/cancellation tests. No website actions are dispatched |
| Live decisions | Generic `lessons/browser` has no milestones; deployed guidance prompt uses current image and goal | API release r7 deployed; no preset walkthrough in the app |
| Visible guidance | Observation-only animation, ring/comment and physical-bounds highlight; hides on focus/geometry/epoch change | Synthetic marker and DPI tests; native foreground marker visibility remains unproven |
| Capture stability | Persistent WGC session, stable source identity, fresh snapshots, bounded resize recovery, Stop/error disposal | Own-window capture-only harness: five snapshots reuse one session; resize requires one restart; no input dispatched |
| Login | Visible account state, verified callback, service retry and continuation into prompt launch | Previous live connected-Chrome Cognito/desktop test passed; new companion handoff is not covered by that earlier result |
| Cost | Active `gemini-3.1-flash-lite`, same model for guidance/verification/transcription | Basic image/schema probe passed; broader synthetic target check 4/5. Quality gate remains open |

The default is a visual guide; separate opt-in physical pointing and sandbox-only single-action demonstration remain advanced capabilities. They do not become authorized through companion invocation.

## Engineering sequence for the remaining work

| Priority / work package | Concrete change and affected boundary | Exit criteria |
|---|---|---|
| P0 — Native companion qualification | Exercise App composition, `WindowsActivity`, `WindowsCapture`, `WindowsPointer`, overlays on a consented Windows desktop | Shortcut context names the right window; prompt opens exactly one requested browser; UI returns to task; visible marker aligns at 100/150/200% DPI including negative monitor origins; overlay never consumes the learner's click; Stop removes it and ends capture |
| P0 — Model quality | Extend `evaluate-grounding.py` with per-check failure metadata; obtain an approved held-out real-app set; compare economical models using identical cases | Current observation/evidence/geometry binding always valid; no false completion; representative target accuracy and confidence interval meet the original release gate. Record latency and total cost per completed task, not just a token-price comparison |
| P0 — Full live manual workflow | Run actual desktop → deployed API → image analysis → annotation → learner action → verification on a disposable task | Record state/timing metadata through one complete task, wrong turn, loading state, question, pause, Stop and network failure. User handles all Railway clicks and final deployment. No browser DOM is passed to the model |
| P1 — Screen-aware conversation | Extend question request/API/provider boundary with a consented current observation and image, reuse media validation and current-evidence binding | “What is this?” answers current pixels; changing windows revokes context; text/audio question takes no extra hidden observation grant. Current questions use lesson context and are not fresh-image chat |
| P1 — Voice-first request | Add microphone gesture to idle companion, bounded recording/transcription, editable goal confirmation, cancellation/denial feedback | Speaking a goal needs no typing; user sees/accepts the transcript before capture starts. No always-on microphone or wake-word claim. Current voice adapter is for bounded questions and remains device-unqualified |
| P1 — Rich annotations | Generalize one grounded rectangle to a small typed annotation contract (label/arrow/rectangle), with observation ID and geometry | All annotations bind to the same fresh frame, clip to capture scope and clear together on focus/move/Stop. Model cannot issue arbitrary drawing code |
| P1 — Companion polish/accessibility | Draggable/snap placement that avoids target controls; font scaling across the card; keyboard focus restoration and accessible live status | Narrator, keyboard-only, high contrast, reduced motion, compact displays and multi-monitor acceptance. High contrast and reduced-motion visuals exist; full accessibility is not yet qualified |
| P1 — Distribution and operations | Signing/installer/updater, safe single-instance activation, recovery, rollback and release telemetry | Signed install/update/rollback works without persisting grants; no raw capture/prompt/audio in telemetry; existing pilot account/cloud resource scope retained |
| P2 — Optional broader Clicky-like product | Named assistants, connected integrations, durable conversations and scheduled routines, each with scoped credentials and retention design | Separate product scope and explicit action boundaries before implementation. Not present in this pilot; the learner-operated deployment preference still applies |

P0 packages close the current teaching experience before adding integrations. P1 voice/conversation can be implemented independently of richer annotations after their shared evidence contract is designed. No additional cloud service or account migration is needed for the current companion.

## Verification and release boundary

Run `scripts/test-all.ps1` for deterministic checks and build. UI scenes include signed-in, current-window, lesson, paused and high contrast under `--render-preview`; these use fake adapters and do not prove native overlays. The `--capture-only` harness uses only its own synthetic windows and does not dispatch input. Any further real input testing needs its own consent; the prior one-click approval has been consumed.

The current cheap model is a development candidate, not a qualified substitution for the older model's 20/20 synthetic report. Its five-case result contains one invalid evidence/target binding; strict checks remain in place. Shared synthetic validation reservations total **$0.925609 of the approved $1**. Do not reset the ledger. See [model evidence](model-capability.md), [acceptance](acceptance.md), and [user guide](user-guide.md).
