# Implementation status

Updated 11 October 2026. Code is implemented across phases 1–10, and the approved cloud development pilot is deployed. **Full functional-pilot acceptance remains pending** the real workflow and broader qualification gates below.

| Phase | Implemented work | Evidence and remaining exit checks |
|---|---|---|
| 1 — Safety foundation | Separate grants, epochs, exact immutable single-use approvals, cancellation, state machine and serialized bounded dispatch | Deterministic permission/replay/mutation/Stop/controller tests pass; wider native race qualification remains |
| 2 — Capture and pointer | Isolated WGC windows, disclosed GDI monitor capture, geometry, masks, preview, pointer/overlay, hooks and Stop hotkey | Approved native harness passed capture, pointer, one real click and visible outcome; broader DPI/occlusion/exclusion/window-change matrix remains |
| 3 — Managed inference | Gemini SDK, compatible generation schema, strict returned contracts, current-evidence checks, retries and usage reservations | Active cheaper gemini-3.1-flash-lite/global/SDK 2.29.0: image/schema probe passed, broader targeting 4/5 with one binding failure; not qualified. Forced refresh passed; natural expiry/refusal/cancellation remain |
| 4 — Guided lesson loop | Observe/explain/point/wait/verify, questions, recovery, debounce, meaningful-change detection, privacy pause and fresh resume | Controller tests pass; complete learner-driven Windows capture → deployed model → pointer → outcome lesson remains |
| 5 — One-action demonstration | Exact approval dialog, bounded primitives, current focus/geometry/pixels, receipts, verification, takeover and Stop | Fake controller/executor tests and one approved native click pass; Unicode and Stop during movement/typing remain |
| 6 — Voice and accessibility | Bounded push-to-talk WAV, confirmed transcription, optional narration, text fallback, Practice, motion/contrast/text controls | Adapters compile; real voice devices/transcription/narration and screen-reader qualification remain; narration is disabled in the deployed pilot |
| 7 — Accounts and progress | Public Cognito PKCE, token binding, refresh/revocation, DPAPI opt-in, ownership/device checks, progress retention/deletion, visible login/setup and service-retry states | Connected Chrome → actual desktop loopback/token verification/viewmodel → deployed lesson access passed with synthetic account and disarmed native adapters; human account is `CONFIRMED`; native window handoff and DPAPI experience remain |
| 8 — Workflows | Live goal/image planning, generic window lesson without milestones; reference templates and evaluation tooling | 200 synthetic cases generated; older 3.8 Flash passed 20/20. Current 3.1 Flash-Lite passed 4/5 (Wilson 37.6–96.4%); real workflows remain unqualified |
| 9 — Pilot delivery | AWS/GCP Terraform, federation, hardened containers, encrypted secrets/state/backup, restore reconciliation, CI, self-contained package and signing hooks | AWS/GCP deployed with clean drift checks; HTTPS/auth/private origin/IMDS isolation passed; S3 backup restored to a new local DB with current deletion reconciliation; package verified, unsigned |
| 10 — Hardening | Negative auth/media/schema/permission tests, bounded concurrency/retries/spend, generic errors, retention, heartbeat and operational runbooks | 42 Python + 62 Core/Platform + 27 app/auth tests pass (131 total), build has zero warnings/errors; load, adversarial live screenshots, natural expiry, broader native races, accessibility and rollback drills remain |

Foundation phase 0 also includes pinned toolchains/dependency locks, schemas/shared fixtures, CI, frozen Alembic migration, local PostgreSQL and development scripts.

## Deployed scope

- AWS account `214422569127`, profile `personal`, region `ap-south-1`; instance `i-09b7452f878f98708`; API `https://d1gnr0x6ly7io1.cloudfront.net`.
- The dedicated GCP project could not be created because of project quota. The user then approved isolated pilot resources in existing project `gen-lang-client-0644821693`. Its lifecycle, billing configuration and other workloads remain unmanaged by this Terraform root.
- Dedicated service account `desktop-guide-runtime`, inference role, federation pool `desktop-guide-aws` and provider `ec2-runtime`. No service-account key or desktop cloud credential.
- Temporary synthetic Cognito users were created with email suppressed and removed after checks, including the connected Chrome login check. The human account created for the explicitly supplied recipient is now enabled and `CONFIRMED` in Cognito. The reported post-login app issue was fixed; the updated human desktop flow still needs learner confirmation.
- User approved a $50/month target, up to $1 of synthetic Gemini validation, and one native click in a disposable session. The native harness will not be rerun automatically.

## Artifacts and limits

`artifacts/DesktopGuide-windows-x64-pilot.zip` is the updated self-contained unsigned x64 package with public cloud configuration and an embedded `SHA256SUMS.csv`; its publish directory is `artifacts/windows-companion-ui`. Older extracted folders do not update automatically. The local mock package is `artifacts/DesktopGuide-windows-x64.zip`; `artifacts/windows` contains that local variant. Packaging without a configuration path always restores the local mock configuration.

The prompt and current-window flows, floating companion, narrow layout, signed-in/lesson/paused/high-contrast screens and guide visuals were rendered with actual Avalonia/Skia and fake capture/input. The drawn cursor works with observation consent alone; physical movement remains separately gated. Target outlines use grounded physical bounds and hide with the cursor on focus/geometry/epoch changes. Starting a guide requests focus for that exact window; when refused, pointing waits up to five seconds and remains cancelable. Render evidence does not qualify native overlay placement/capture exclusion.

The capture-only own-window harness confirms five WGC snapshots share one session, selected-window geometry is independent of the foreground app, resize recovers with one restart and Stop ends capture. The fixture could not obtain foreground in this automation session; its visual-marker guard correctly refused display, so native cursor visibility remains unproven. No real input was dispatched. The persistent session removes repeated start/stop border transitions; the normal Windows sharing border remains enabled.

API r5 is deployed with current-image planning and a generic window lesson. Offline walkthroughs were removed. The learner chose “Guide me; I deploy”; Codex did not create or deploy a Railway project. [Reference analysis and remaining implementation sequence](heyclicky-windows-plan.md) documents the difference between this core companion and full HeyClicky parity.

Reports in `artifacts` cover Windows smoke, model probe, deployed HTTP/auth/image turn, runtime federation, evaluation, validation budget and cloud-backup restore. They contain synthetic/operational metadata, not learner captures, passwords, tokens or full model responses.

The earliest incomplete phase exit is broader **Phase 2 native qualification**. Later implementations and live checks do not close it. This is an invited development pilot, not an accepted public learner release. Current limits: one API worker/instance, sandbox-only demonstrations, strict pixel checks, English-only qualification, no bulk history/account-erasure UI, no installer/updater and no signing identity. AWS fixes the default CloudFront certificate's edge policy; a custom hostname/certificate is needed to enforce a modern minimum TLS policy at the edge.
