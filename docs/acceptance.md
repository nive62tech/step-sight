# Pilot acceptance report

Date: 11 October 2026. Build: unsigned Windows x64 development pilot. **Full functional-pilot acceptance is pending.** Code exists across phases 1–10; the approved AWS/GCP deployment and the checks below have completed.

## Checks that ran

| Check | Result and scope |
|---|---|
| Deterministic suite | 42 Python tests, 45 Core/Platform tests and 4 credential tests (91 total); lint/contracts pass; .NET build has zero warnings/errors |
| Infrastructure | All three roots format/validate; encrypted remote state; approved AWS/GCP applies completed; final plans report no drift |
| Local development | Real PostgreSQL migration/drift/binary restore passed; loopback API/sandbox and isolated nonroot/read-only API container checked |
| Actual Windows harness | Explicit disposable-session approval; WGC capture/repeated capture, real pointer, one real click, visible outcome, Guide input rejection and local Stop passed; local epoch invalidation 0.283ms; hotkey registered |
| UI/package | Actual Avalonia/Skia rendering with disarmed fake adapters; self-contained x64 ZIP and hashes, public cloud config; unsigned |
| Sandbox browser | Settings/Ocean, loading/disabled Save, visible success and failed-save recovery checked as a browser UI test; not the assistant's model/input path |
| Deployed HTTP | HTTPS readiness 200, no-store, anonymous/invalid-token 401, plain HTTP 403, direct instance-origin connection denied |
| Deployed OAuth/ownership | Temporary synthetic users, email suppressed; actual Hosted UI HTTP PKCE, Cognito refresh/revocation, signed access vs ID-token rejection, cross-user Get/delete isolation and S3 deletion ledger passed; users removed |
| Runtime credentials | API IMDSv2 role, exact AWS-role federation and forced GCP refresh passed; separate container's actual IMDSv2 PUT denied |
| Live model | Global gemini-3.8-flash/SDK 2.29.0 synthetic image/schema/grounding passed; no native computer-use tool |
| Deployed model route | One authenticated synthetic image turn through the real API/provider/current-evidence validation passed; 8,359ms round trip in one sample |
| Synthetic grounding | 200 cases generated; 20 evaluated, 20 correct with current bindings, point estimate 100%, Wilson 95% interval 83.9–100%; real applications unqualified |
| Backup/restore | Scheduled encrypted S3 metadata backup created; actual object restored to a new local PostgreSQL target; current independent deletion ledger reconciled; temporary DB removed |

Deterministic coverage includes permission modes, approval expiry/replay/mutation, epochs/current geometry/pixels, negative origins/DPI/padding, questions/wrong turns/late Stop, one-in-flight, bounded retry/repair, failed/missing usage reservations, owner/device isolation, token bindings, malformed/oversized media, no spooling/log canary, secure-store opt-out and restore deletion reconciliation. These finite results are not universal safety guarantees.

## Required checks still open

| Gate | Remaining evidence |
|---|---|
| Phase 2/5 native integration | Broader DPI/window move/resize/negative-origin/occlusion/capture-exclusion/Unicode/hotkey-conflict and Stop during movement/typing tests; one-click consent does not authorize extra physical input automatically |
| Phase 3 provider | Live refusal, adversarial screenshots, stale/false confirmation and cancellation; natural expiry/role refresh over time |
| Phase 4 full product lesson | Actual learner-driven Windows capture → deployed guidance → physical pointer → learner action → visible verification; questions, wrong turns, waiting, pause/resume and Stop |
| Phase 6 voice/accessibility | Real microphone denial/recording/transcription, optional narration/intelligibility/interruption, text fallback, screen-reader/contrast/text-scale checks; other languages unqualified |
| Phase 7 user experience | Windows external-browser/loopback/DPAPI experience, startup disarmed with saved progress, human invitation recipient |
| Phase 8 workflows | Consented real held-out screenshots and complete Sandbox/Notepad/VS Code lessons; Railway manual deployment only when appropriate; representative ≥95% target-selection evidence |
| Phase 9 release/operations | Signing identity and signature verification, custom hostname/certificate for enforced modern edge TLS minimum, operator alert destination, reboot/firewall races and recovery objectives |
| Phase 10 hardening | Live injection, device/network/lock/sleep races, load/memory, latency percentiles/cost per successful lesson, accessibility and upgrade/rollback drill |

Zero unauthorized dispatches and zero critical false completions are required in the defined release safety suite. The executed deterministic/native subset passes; untested races/provider/application scenarios remain unassessed.

## Practical limits

One worker/instance; no high availability. Sandbox demonstration policy uses a declared trusted window/title, not cryptographic process identity. Strict pixel equality can refuse benign hover/caret/loading changes. The learner restores the foreground application after using the panel; the client does not steal focus. Saved metadata restores no grants; bulk history/account-erasure UI is absent. Global provider processing/retention conditions apply. Cloud resources are active under the approved $50/month target, not a hard cap; synthetic validation remains below its separate $1 conservative ledger ceiling.

The earliest incomplete phase exit is broader **Phase 2 native qualification**. Later code and successful deployed checks do not close it. Treat this artifact as an invited development pilot until the remaining evidence is recorded.