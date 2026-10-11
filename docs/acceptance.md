# Pilot acceptance report

Date: 11 October 2026. Build: unsigned Windows x64 development pilot. **Full functional-pilot acceptance is pending.** Code exists across phases 1–10; the approved AWS/GCP deployment and the checks below have completed.

## Checks that ran

**r15 incremental update:** 17 focused deterministic recovery/resume/voice cases passed; Windows publish, two headless mode renders, package integrity (318 files) and deployed readiness passed. Native IRCTC and real microphone behavior remain unqualified. No additional paid model validation or broad suite was run. See [recovery-r15.md](recovery-r15.md).

| Check | Result and scope |
|---|---|
| Deterministic suite | 95 Python tests, 88 Core/Platform tests and 39 app/auth tests (222 total); lint/contracts pass; .NET build has zero warnings/errors |
| Model-driven prompt launch / screenshots only | API r13 live synthetic typo-to-site check passed in 9,936ms; model selected Chrome and the official IRCTC host. No browser was opened by the probe. Runtime/public readiness and anonymous rejection passed. Hard-coded routing removed; production browser inspection disabled. Late planning Stop, clarification, arbitrary sites, invalid destinations, budget accounting and shared contracts have deterministic coverage |
| Deployed browser-label prompt | API r11 candidate passed a live synthetic browser-like image containing several different controls: exact New Project label, current bindings and target center in 5,115ms. Promoted through approved SSM; runtime/public HTTPS readiness passed |
| Dedicated computer-use locator | API r12: Gemini 3.5 Flash-Lite desktop tool with LOW thinking, proposal only, selected the correct New Project point in 5,165ms. Current bindings passed; runtime/public HTTPS ready. Parser/provider tests cover bounded coordinates, one call, refusal, unsupported actions and disabled automatic execution. This synthetic result does not qualify actual Railway targeting |
| Target recovery and dock | NOT_YET refreshes the same instruction's target without false completion; a rejected browser label triggers one locator fallback. Native points resolve to one accessible control, including unlabeled icons. Fake-adapter tests verify moving the dock before the visibility check and rejecting Stop during that preparation |
| Historical browser target cross-check (disabled in r13) | 15 added deterministic cases: correct a misplaced image box using an exact label; reject duplicate/disabled/masked/password targets; handle DPI, negative origin and crop; preserve proposals; skip sandbox; reject late Stop. Windows UIA adapter compiles and is packaged. Actual browser/native pointer integration remains unqualified because the native test helper cannot initialize |
| Compact response and cursor | API r10: three live synthetic state checks passed with MINIMAL thinking (loading 9,932ms, ready target 4,431ms, unchanged NOT_YET 2,296ms). Black/white pointer, blue pulse/ring, fixed target tip and dark instruction callout rendered with fake adapters |
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
| Live model | Active gemini-3.1-flash-lite/global/SDK 2.29.0 for planning/verification; 3.5 Flash-Lite dedicated desktop tool for current-target coordinates. An earlier structured-output 3.5 candidate failed; the r12 computer-use locator passed its synthetic check. No automatic tool execution |
| Current-step locator | API r6: one live synthetic button probe passed exact observation/evidence/geometry binding and button-center checks in 6,431ms; no real learner capture/input. Compact instruction/missing-target/provider-error cards rendered successfully. Native overlay visibility on Railway remains unverified |
| Gateway/recovery correction | API r7 and Terraform: deployed CloudFront origin timeout raised from 30s to 60s; desktop deadline 65s. Authenticated synthetic Gemini image turn completed in 30,000ms; same-request result recovery and cross-user rejection passed; temporary accounts removed. Simulated 504/network loss uses GET polling without re-upload, and Stop cancels it. Loading-page waiting is tested separately from click targets. Native inspection helper still fails initialization |
| Deployed model route | One authenticated synthetic image turn through the real API/provider/current-evidence validation passed; 8,359ms round trip in one sample |
| Synthetic grounding | Older 3.8 Flash: 20/20 synthetic cases passed. Earlier 3.1 Flash-Lite adapter: 4/5 passed; one invalid evidence/target binding, Wilson 95% interval 37.6–96.4%. Cheap model and real applications remain unqualified |
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

One worker/instance; no high availability. Sandbox demonstration policy uses a declared trusted window/title, not cryptographic process identity. Demonstrations require strict whole-frame equality; visual guidance tolerates minor changes outside the target while rejecting target/layout changes. Explicit guide start and companion Show me where/Check screen request foreground for the bound window; Windows may refuse. Saved metadata restores no grants; bulk history/account-erasure UI is absent. Global provider processing/retention conditions apply. Cloud resources are active under the approved $50/month target, not a hard cap; synthetic reservations total $0.9133935 of the separate $1 ceiling. The current questions use lesson context; fresh-image conversation and voice-first invocation are remaining work in the [companion plan](heyclicky-windows-plan.md).

The earliest incomplete phase exit is broader **Phase 2 native qualification**. Later code and successful deployed checks do not close it. Treat this artifact as an invited development pilot until the remaining evidence is recorded.
