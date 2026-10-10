# Desktop Guide contributor instructions

## Build and checks
- `pwsh ./scripts/dev-up.ps1` prepares Python, PostgreSQL, API, and sandbox.
- `pwsh ./scripts/test-all.ps1` runs deterministic tests and builds. It never moves the cursor.
- `pwsh ./scripts/package-windows.ps1` publishes the self-contained x64 desktop.
- `pwsh ./scripts/validate-infra.ps1` formats/checks Terraform; it never applies resources.
- Use repository `.tools/dotnet/dotnet.exe` when the system SDK is unavailable.

## Boundaries
- App/viewmodels depend on Core/Platform interfaces; only Windows contains Win32 input.
- The backend returns untrusted guidance/proposals, never input authorization.
- JSON schemas and shared fixtures define the wire contract. Unknown versions fail closed.
- Keep mock/offline behavior labelled. Report live, mocked, Windows, and deployed checks separately.

## Safety
- Capture, pointer, and one-action demonstration need separate local grants.
- Stop is local: invalidate epoch, revoke capabilities, cancel work, reject late responses.
- Input requires exact immutable action approval, current scope/focus/geometry, and no takeover.
- Never persist raw learner screenshots/audio, tokens, model prompts, or armed capabilities.
- Never use DOM selectors/oracles to solve sandbox lessons.
- No paid provisioning, deployment, DNS change, push, or destructive cloud operation without approval.
- Real input tests need a disposable session and explicit `--allow-real-input` consent. Never run them in ordinary CI.
- Preserve unrelated work and the original build specification.
