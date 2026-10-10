# Pilot acceptance report

Date: 11 October 2026. Build: unsigned Windows x64 development pilot. **Full functional-pilot acceptance is pending.** Code exists across phases 1–10; the approved AWS/GCP deployment and the checks below have completed.

## Checks that ran

| Check | Result and scope |
|---|---|
| Deterministic suite | 42 Python tests, 62 Core/Platform tests and 27 app/auth tests (131 total); lint/contracts pass; .NET build has zero warnings/errors |
| Infrastructure | All three roots format/validate; encrypted remote state; approved AWS/GCP applies completed; final plans report no drift |
| Local development | Real PostgreSQL migration/drift/binary restore passed; loopback API/sandbox and isolated nonroot/read-only API container checked |
| Actual Windows harness | Explicit disposable-session approval; WGC capture/repeated capture, real pointer, one real click, visible outcome, Guide input rejection and local Stop passed; local epoch invalidation 0.283ms; hotkey registered |
| UI/package | Prompt/current-window companion, dock, lesson/paused screens, 420px layout, high contrast and cursor/callout/outline rendered in actual Avalonia/Skia with fake adapters; self-contained x64 ZIP and hashes, public cloud config; unsigned |
| Capture-only Windows harness | Own synthetic windows only; five snapshots reuse one WGC session, background target identity stays stable, resize recovers with one restart, Stop releases capture. No real input. Fixture could not obtain foreground: native visual cursor was correctly rejected and remains untested |
| Sandbox browser | Settings/Ocean, loading/disabled Save, visible success and failed-save recovery checked as a browser UI test; not the assistant's model/input path |
| Deployed HTTP | HTTPS readiness 200, no-store, anonymous/invalid-token 401, plain HTTP 403, direct instance-origin connection denied |
| Deployed OAuth/ownership | Temporary synthetic users, email suppressed; actual Hosted UI HTTP PKCE, Cognito refresh/revocation, signed access vs ID-token rejection, cross-user Get/delete isolation and S3 deletion ledger passed; users removed |
| Connected Chrome login | Real Cognito login → actual C# loopback/token validation → actual viewmodel setup screen → authenticated deployed `/v1/lessons` passed. Headless Avalonia and fake native adapters; screenshot and report in `artifacts/ui-qa/live`. Capture/input stayed disarmed; synthetic user/credentials removed |
| Railway handoff | Current connected Chrome workspace/dashboard inspected; learner explicitly chose to create/deploy the example manually. No project created or deployed by Codex; no successful deployment claimed |
| Runtime credentials | API IMDSv2 role, exact AWS-role federation and forced GCP refresh passed; separate container's actual IMDSv2 PUT denied |
| Live model | Active gemini-3.1-flash-lite/global/SDK 2.29.0 basic synthetic image/schema/grounding passed; 3.5 Flash-Lite candidate probe failed ProviderUnavailable and was not promoted; no native computer-use tool |
| Deployed model route | One authenticated synthetic image turn through the real API/provider/current-evidence validation passed; 8,359ms round trip in one sample |
| Synthetic grounding | Older 3.8 Flash: 20/20 synthetic cases passed. Active 3.1 Flash-Lite: 4/5 passed; one invalid evidence/target binding, Wilson 95% interval 37.6–96.4%. Cheap model and real applications remain unqualified |
| Backup/restore | Scheduled encrypted S3 metadata backup created; actual object restored to a new local PostgreSQL target; current independent deletion ledger reconciled; temporary DB removed |

Deterministic coverage includes permission modes, approval expiry/replay/mutation, epochs/current geometry/pixels, negative origins/DPI/padding, questions/wrong turns/late Stop, one-in-flight, bounded retry/repair, failed/missing usage reservations, owner/device isolation, token bindings, malformed/oversized media, no spooling/log canary, secure-store opt-out and restore deletion reconciliation. Login regressions cover rejected exchange/nonce, occupied callback port, browser success delayed until verification, unrelated favicon requests, cancellation, visible next action and service outage/retry. Pointer focus waiting and Stop cancellation are tested with fake input. These finite results are not universal safety guarantees.

## Required checks still open

| Gate | Remaining evidence |
|---|---|
| Phase 2/5 native integration | Broader DPI/window move/resize/negative-origin/occlusion/capture-exclusion/Unicode/hotkey-conflict and Stop during movement/typing tests; one-click consent does not authorize extra physical input automatically |
| Phase 3 provider | Live refusal, adversarial screenshots, stale/false confirmation and cancellation; natural expiry/role refresh over time |
| Phase 4 full product lesson | Actual learner-driven Windows capture → deployed guidance → visual marker → learner action → visible verification; questions, wrong turns, waiting, pause/resume and Stop |
| Phase 6 voice/accessibility | Real microphone denial/recording/transcription, optional narration/intelligibility/interruption, text fallback, screen-reader/contrast/text-scale checks; other languages unqualified |
| Phase 7 user experience | Native foreground handoff and DPAPI experience, startup disarmed with saved progress, human confirmation of the updated desktop flow; connected Chrome/loopback/shared login/viewmodel path passed |
| Phase 8 workflows | Consented real held-out screenshots and complete Sandbox/Notepad/VS Code lessons; Railway manual deployment only when appropriate; representative ≥95% target-selection evidence |
| Phase 9 release/operations | Signing identity and signature verification, custom hostname/certificate for enforced modern edge TLS minimum, operator alert destination, reboot/firewall races and recovery objectives |
| Phase 10 hardening | Live injection, device/network/lock/sleep races, load/memory, latency percentiles/cost per successful lesson, accessibility and upgrade/rollback drill |

Zero unauthorized dispatches and zero critical false completions are required in the defined release safety suite. The executed deterministic/native subset passes; untested races/provider/application scenarios remain unassessed.

## Practical limits

One worker/instance; no high availability. Sandbox demonstration policy uses a declared trusted window/title, not cryptographic process identity. Demonstrations require strict whole-frame equality; visual guidance tolerates minor changes outside the target while rejecting target/layout changes. Explicit guide start and companion Show me where/Check screen request foreground for the bound window; Windows may refuse. Saved metadata restores no grants; bulk history/account-erasure UI is absent. Global provider processing/retention conditions apply. Cloud resources are active under the approved $50/month target, not a hard cap; synthetic reservations total $0.5999045 of the separate $1 ceiling. The current questions use lesson context; fresh-image conversation and voice-first invocation are remaining work in the [companion plan](heyclicky-windows-plan.md).

The earliest incomplete phase exit is broader **Phase 2 native qualification**. Later code and successful deployed checks do not close it. Treat this artifact as an invited development pilot until the remaining evidence is recorded.
