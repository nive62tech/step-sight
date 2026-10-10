# Implementation status

Updated 11 October 2026. Code is implemented across phases 1–10, and the approved cloud development pilot is deployed. **Full functional-pilot acceptance remains pending** the real workflow and broader qualification gates below.

| Phase | Implemented work | Evidence and remaining exit checks |
|---|---|---|
| 1 — Safety foundation | Separate grants, epochs, exact immutable single-use approvals, cancellation, state machine and serialized bounded dispatch | Deterministic permission/replay/mutation/Stop/controller tests pass; wider native race qualification remains |
| 2 — Capture and pointer | Isolated WGC windows, disclosed GDI monitor capture, geometry, masks, preview, pointer/overlay, hooks and Stop hotkey | Approved native harness passed capture, pointer, one real click and visible outcome; broader DPI/occlusion/exclusion/window-change matrix remains |
| 3 — Managed inference | Gemini SDK, compatible generation schema, strict returned contracts, current-evidence checks, retries and usage reservations | gemini-3.8-flash/global/SDK 2.29.0 image/schema/target probe passed through AWS federation; forced refresh passed; natural expiry/refusal/cancellation qualification remains |
| 4 — Guided lesson loop | Observe/explain/point/wait/verify, questions, recovery, debounce, meaningful-change detection, privacy pause and fresh resume | Controller tests pass; complete learner-driven Windows capture → deployed model → pointer → outcome lesson remains |
| 5 — One-action demonstration | Exact approval dialog, bounded primitives, current focus/geometry/pixels, receipts, verification, takeover and Stop | Fake controller/executor tests and one approved native click pass; Unicode and Stop during movement/typing remain |
| 6 — Voice and accessibility | Bounded push-to-talk WAV, confirmed transcription, optional narration, text fallback, Practice, motion/contrast/text controls | Adapters compile; real voice devices/transcription/narration and screen-reader qualification remain; narration is disabled in the deployed pilot |
| 7 — Accounts and progress | Public Cognito PKCE, token binding, refresh/revocation, DPAPI opt-in, ownership/device checks, progress retention/deletion | Actual Hosted UI HTTP PKCE, refresh, access/ID-token distinction, cross-user isolation and production S3 deletion ledger passed; Windows browser/secure-store experience and human invitation remain |
| 8 — Workflows | Sandbox, Notepad, VS Code and manual-only experimental Railway templates, version/source metadata and evaluation tooling | 200 synthetic cases generated; 20/20 evaluated targets and current-evidence bindings passed; Wilson 95% interval 83.9–100%; real workflows remain unqualified |
| 9 — Pilot delivery | AWS/GCP Terraform, federation, hardened containers, encrypted secrets/state/backup, restore reconciliation, CI, self-contained package and signing hooks | AWS/GCP deployed with clean drift checks; HTTPS/auth/private origin/IMDS isolation passed; S3 backup restored to a new local DB with current deletion reconciliation; package verified, unsigned |
| 10 — Hardening | Negative auth/media/schema/permission tests, bounded concurrency/retries/spend, generic errors, retention, heartbeat and operational runbooks | 42 Python + 45 Core/Platform + 4 credential tests pass (91 total), build has zero warnings/errors; load, adversarial live screenshots, natural expiry, broader native races, accessibility and rollback drills remain |

Foundation phase 0 also includes pinned toolchains/dependency locks, schemas/shared fixtures, CI, frozen Alembic migration, local PostgreSQL and development scripts.

## Deployed scope

- AWS account `214422569127`, profile `personal`, region `ap-south-1`; instance `i-09b7452f878f98708`; API `https://d1gnr0x6ly7io1.cloudfront.net`.
- The dedicated GCP project could not be created because of project quota. The user then approved isolated pilot resources in existing project `gen-lang-client-0644821693`. Its lifecycle, billing configuration and other workloads remain unmanaged by this Terraform root.
- Dedicated service account `desktop-guide-runtime`, inference role, federation pool `desktop-guide-aws` and provider `ec2-runtime`. No service-account key or desktop cloud credential.
- Temporary synthetic Cognito users were created with email suppressed and removed after checks. A human invitation needs the intended email; it is not inferred from cloud credentials.
- User approved a $50/month target, up to $1 of synthetic Gemini validation, and one native click in a disposable session. The native harness will not be rerun automatically.

## Artifacts and limits

`artifacts/DesktopGuide-windows-x64-pilot.zip` is a self-contained unsigned x64 package with public cloud configuration and an embedded `SHA256SUMS.csv`. The local mock package is `artifacts/DesktopGuide-windows-x64.zip`; `artifacts/windows` currently contains that local variant. Packaging without a configuration path always restores the local mock configuration.

Reports in `artifacts` cover Windows smoke, model probe, deployed HTTP/auth/image turn, runtime federation, evaluation, validation budget and cloud-backup restore. They contain synthetic/operational metadata, not learner captures, passwords, tokens or full model responses.

The earliest incomplete phase exit is broader **Phase 2 native qualification**. Later implementations and live checks do not close it. This is an invited development pilot, not an accepted public learner release. Current limits: one API worker/instance, sandbox-only demonstrations, strict pixel checks, English-only qualification, no bulk history/account-erasure UI, no installer/updater and no signing identity. AWS fixes the default CloudFront certificate's edge policy; a custom hostname/certificate is needed to enforce a modern minimum TLS policy at the edge.
