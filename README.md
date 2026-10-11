# Desktop Guide

A Windows desktop companion that uses Gemini to interpret your prompt and choose a starting website, opens a browser, analyses live screenshots of its authorized window, draws the next step, and verifies the learner's action. Website routing and lesson steps are not hard-coded. Browser control inspection is disabled by user choice. Press Ctrl+Alt+Space to ask about an existing app. The desktop owns capture and input authority; the API returns untrusted guidance and optional proposals.

Implementation spans phases 1–10. The approved AWS/GCP development pilot is deployed, live Gemini and deployed OAuth checks pass, and the disposable Windows one-click harness passed. This is an **unsigned development build**; broader workflow/release qualification remains in [implementation status](docs/implementation-status.md). Mock mode never claims screen understanding or successful verification.

## Run locally

Prerequisites: Windows 11, .NET SDK 10.0.401, Python 3.12 via `uv`, Docker Desktop running, and PowerShell. Scripts use `.tools/dotnet/dotnet.exe` when installed locally; otherwise `dotnet`. Put Docker's CLI on PATH. No AWS/GCP credentials are needed for mock mode.

```powershell
./scripts/dev-up.ps1
./scripts/test-all.ps1
dotnet run --project apps/desktop/DesktopGuide.App/DesktopGuide.App.csproj
```

If only the local SDK exists, replace `dotnet` with `./.tools/dotnet/dotnet.exe`. API: `http://127.0.0.1:8000`, API docs: `/docs`, sandbox: `http://127.0.0.1:8765`. Mock adapters support tests and UI previews; the app refuses to start a live guide with a mock provider. There is no offline walkthrough. Use the cloud-configured package for actual image analysis.

`dev-up.ps1` creates `.env` from `.env.example` only when absent, starts PostgreSQL, applies migrations, and launches hidden loopback helpers. `dev-down.ps1` stops those helpers and PostgreSQL without deleting the development volume. Local credentials are development placeholders; production refuses them.

## Build and checks

```powershell
./scripts/test-all.ps1
./scripts/validate-infra.ps1
uv run python scripts/check-postgres.py
./scripts/package-windows.ps1
./.tools/dotnet/dotnet.exe run --project apps/desktop/DesktopGuide.App -- --render-preview
```

The PostgreSQL check creates and removes only new random local test databases. The preview renders the assistant UI with fake adapters; it captures no desktop and dispatches no input. Use `--scene signed-in`, `review`, `lesson`, `paused`, or `high-contrast` and `--width 420` for render checks. The default package is `artifacts/DesktopGuide-windows-x64.zip`, with file hashes in `artifacts/windows/SHA256SUMS.csv`. Extract to a user-writable folder and run `DesktopGuide.App.exe`. No installer or automatic updater is included. Signing hooks require an existing signing identity.

Ordinary tests use fake input. The separate [Windows smoke harness](scripts/run-windows-smoke.ps1) requires an explicitly approved disposable session and `-AllowRealInput -DisposableSession`; it moves the real pointer and clicks its own test window once. That approved test passed in this session. It is not rerun by ordinary checks.

## Live pilot

The deployed accounts are AWS `214422569127`, region `ap-south-1`, and explicitly approved pilot resources inside GCP project `gen-lang-client-0644821693`. Project quota prevented the originally proposed dedicated project. AWS hosts the API/database and Cognito; managed GCP hosts inference through exact-role workload federation. No service-account key is required.

API: [d1gnr0x6ly7io1.cloudfront.net](https://d1gnr0x6ly7io1.cloudfront.net/health/ready). The updated cloud-configured package is `artifacts/DesktopGuide-windows-x64-pilot.zip`; extract it and run `DesktopGuide.App.exe`. Sign-in is invite-only. The authorized human pilot account is now enabled and `CONFIRMED` in Cognito; its old temporary invitation password is no longer the sign-in credential. Use the permanent password set during first sign-in. Synthetic test users were removed.

The app starts from a prompt or the current-window shortcut, then collapses to a companion dock. Successful sign-in shows a connected state; browser success is sent only after desktop token verification. A visual cursor, pulse, outline and instruction callout work without moving the physical mouse. The optional manual source/preview flow remains available. Railway remains learner-operated: the user creates resources, handles secrets/billing and deploys. See [using the app](docs/user-guide.md) and the [HeyClicky reference / Windows implementation plan](docs/heyclicky-windows-plan.md).

To package the cloud variant separately without overwriting local mock artifacts:

```powershell
./scripts/package-windows.ps1 -ConfigurationPath .local/deployment/desktop-config.json -OutputDirectory artifacts/windows-intent-screenshots -ArchivePath artifacts/DesktopGuide-windows-x64-pilot.zip
```

The checks include 222 deterministic tests, real PostgreSQL migration/restore, HTTPS/auth/origin isolation, federation/forced refresh and S3 backup/deletion reconciliation. A capture-only Windows fixture verifies persistent WGC snapshots and resize recovery without input. The older Gemini 3.8 Flash passed 20/20 synthetic cases; the cheaper Gemini 3.1 Flash-Lite with compact responses and MINIMAL thinking passed three live synthetic state checks. Its earlier schema passed 4/5 broader cases; real application accuracy remains unqualified. Connected Chrome passed live Cognito PKCE through the actual desktop login/viewmodel and deployed lesson-access check with disarmed native adapters. Native companion/overlay placement, real application lessons, voice/accessibility and signed-release qualification remain.

Review [deployment and costs](docs/deployment.md) for the active resource inventory, spending target and repeatable deployment steps. Creation and bounded synthetic usage were explicitly approved. The local [cloud plan script](scripts/preview-cloud-plan.ps1) remains read-only. Do not tear down or expand scope without reviewing the intended changes.

Further documentation: [architecture](docs/architecture.md), [API](docs/api.md), [safety/privacy](docs/safety-privacy.md), [model capability and evaluation](docs/model-capability.md), [operations](docs/operations.md), [acceptance](docs/acceptance.md). The original [build specification](desktop_guide_codex_build_spec.md) is preserved.
