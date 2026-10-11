# Phone control and fresh Resume — r16

Open **https://d1gnr0x6ly7io1.cloudfront.net/control/** on a phone or desktop browser.

1. In the updated Windows app, sign in and open **Preferences → Control from your phone**. Stop any existing task first.
2. To permit Autonomous tasks from the phone, select **Also allow phone-started tasks to click, scroll and type autonomously**. Then select **Enable phone control**.
3. Sign into the web page with the same Desktop Guide account, choose the connected PC, select Guided or Autonomous, and send a prompt.
4. Use Pause, Resume or Stop from the web page. Guided tasks also expose **Check my PC screen**. Instructions, connection state and command acknowledgements appear on the page.

The PC must remain awake, unlocked and running Desktop Guide. Phone control expires after one hour. Local Stop, sign-out, lock, exit or losing the relay connection revoke phone control. Local Stop remains immediate. Remote commands take a network round trip; an unacknowledged command is not evidence that the PC acted. The desktop polls every two seconds and disconnects after a failed request/deadline. Closing the web page does not stop an already running task. Reopening/reloading requires web sign-in again; tokens are kept in memory.

The phone uses the existing prompt-to-browser launcher. Screenshot analysis selects subsequent actions. Native applications still need to be opened and selected locally; this release does not add phone-launched native settings tasks. Guided mode is green and leaves clicks on the PC to the user. Autonomous mode is red and uses the existing bounded local input grants, focus/geometry/fresh-image checks, takeover and sensitive-action handoffs. No screenshot stream or remote microphone access is added.

## English banner and Resume correction

Previously, Resume synchronized the session but still sent the old manual instruction as a step that had to be verified. If that step was “click English,” an uncertain verification could discard the next proposed step and leave the old instruction displayed again.

Resume now sends a fresh screenshot for replanning. The previous instruction is historical context, not a prerequisite to repeat. Replanning cannot count the previous step as completed. A new action still needs current visual evidence and the existing local input checks. Language choices and ordinary notices are described to the model as normal navigation; it can select a grounded scroll within a notice when appropriate. There are no fixed IRCTC selectors or coordinates.

The controller retains the task's original interaction mode. The viewmodel restores that mode before Resume without invoking its “change mode, stop task” handler. A manual handoff therefore does not convert an Autonomous task to Guided mode. Phone Resume is permitted only within the still-active local delegation and keeps that original mode.

Start a fresh task after this release's app/API restart. Unsaved in-flight sessions are not migrated across deployment.

## Relay and authentication

Static assets live in `apps/control`, served by the existing FastAPI service at `/control/`. The separate product website is unchanged. The public Cognito client uses authorization-code PKCE with the added HTTPS callback; the existing desktop loopback callback remains registered. Access/refresh tokens and lease keys are not persisted. Session storage holds only the short-lived OAuth state and verifier during sign-in.

All `/v1/remote/*` routes use the existing Cognito subject and scope checks. A random, hashed desktop lease key additionally binds polling to the registered PC. Other accounts cannot list or command that connection. Registration records the local Autonomous choice; every received command is checked again on the desktop. The cloud never issues a native input capability.

| Route | Purpose |
|---|---|
| POST `/v1/remote/connections` | Register an explicitly enabled desktop for one hour |
| GET `/v1/remote/connections` | List this account's connections, status and latest acknowledgement |
| POST `/v1/remote/connections/{id}/commands` | Queue start/pause/resume/stop/check with a request UUID |
| POST `/v1/remote/connections/{id}/poll` | Heartbeat/status, acknowledgement and delivery; requires `X-Desktop-Lease` |
| DELETE `/v1/remote/connections/{id}` | Revoke the connection |

The relay is volatile and uses the pilot's single API worker. Restart requires re-enabling phone control. It holds one pending command per PC for at most 30 seconds, rejects offline delivery, and disables a connection after 20 seconds without a heartbeat. Stop/Pause supersede pending work. Request IDs are immutable/idempotent; the desktop deduplicates delivered IDs for the lease lifetime. Task text is dropped when delivery is acknowledged, cancelled or expires, and is never written to the database. Periodic cleanup drops expired connections. See generated `Remote*.schema.json` contracts and the shared fixture.

## Verification and release

On 11 October 2026:

- 21 focused regression cases passed: 11 backend relay/Resume cases, 8 Core/Platform bridge/Resume cases, one viewmodel remote-start/local-Stop race, and one shared Python/C# remote fixture. Shared schema validation and changed backend Ruff checks passed.
- Windows build/publish succeeded. All 318 package manifest files verified. The r16 executable was launched with observation/input and phone control off; its window title and exact process path were verified.
- Connected Chrome loaded the live page and Cognito sign-in form. Disposable Cognito users completed real PKCE via the new callback, account isolation, lease checks, idempotent delivery, acknowledgement, Autonomous Resume, Stop priority, revocation and browser-origin refresh CORS. Test users and refresh tokens were removed/revoked.
- A separate Chromium browser exercised the real loopback relay with a simulated PC: Autonomous start → Pause → Resume retaining red Autonomous mode → Stop; Guided start → screen check → Stop; disconnect. No JavaScript errors or horizontal overflow at 320, 390, 426 or 1280 CSS pixels. See [visual verification](design/mobile-control-verification.md).

No broad suite, paid Gemini probe, microphone test or real native input ran for r16. The full live IRCTC workflow and real mobile-device qualification remain unverified; deterministic replanning tests do not establish model accuracy on every live banner.

AWS image: `desktop-guide-api:pilot-20261011-r16`, digest `sha256:78af3a5a90bccce6bca2946b38bd84fed0abbee343d3bf1f56c31c126c400515`. SSM promotion `5639c853-59ce-4454-9783-0a74f470a314` succeeded with readiness and served-page checks, using the exact r15 base and network-disabled build. Source archive SHA256: `1e7d0a99f0c45bdf8950f739485e03311f463cf1beba5e5507e1f72a9b98e288`.

Windows: `artifacts/windows-r16/DesktopGuide.App.exe`; unsigned ZIP `artifacts/DesktopGuide-windows-x64-r16.zip`, SHA256 `612E77C2848786D68CB4021F8B60DFD823622788097AD45A0EA217074E422B04`. The runtime adds `COGNITO_DOMAIN`; no new cloud service or model change was needed. Cognito callback/logout lists were updated narrowly, preserving other client settings. Terraform records this through `web_control_callback_urls`; no broad Terraform apply ran.
