# Guided and autonomous tasks

Implemented in the 11 October 2026 r14 pilot. The user selects **Guided ? I click** or **Autonomous ? agent acts** on the prompt screen or floating companion. Guided is the default. Autonomous control requires a separate, unchecked task consent box; sign-in, changing modes, saved progress and cloud responses cannot grant control.

## Shipped behavior

- The existing Gemini intent planner can open a browser and starting website from the prompt. For an existing Windows application, summon the companion with Ctrl+Alt+Space or select and preview its window.
- An autonomous grant covers the starting window and installed WhatsApp Desktop when explicitly named in the prompt, for 10 minutes or 30 dispatch attempts across the whole task. Handoffs consume an exact local action lease and preserve the original budget/expiry. The controller observes, proposes one action, validates it locally, executes it and requests a fresh screenshot to verify the outcome before continuing.
- Gemini 3.1 Flash-Lite plans and verifies. Each autonomous click receives a second coordinate proposal from the dedicated Gemini 3.5 Flash-Lite computer-use locator. Both inference calls share the turn deadline and are individually usage-accounted. Neither model executes tools directly.
- Decisions use screenshots only. Browser UIA/DOM inspection remains disabled. There are no site-specific click scripts or stored target coordinates.
- Supported inputs are left click, bounded vertical scrolling, bounded Unicode text, and allowlisted navigation keys. Typing requires an identified focused field. Every action uses an exact single-use local capability bound to the image, geometry, target, scope and current permission epoch.
- Moving the physical mouse or typing pauses the agent. Stop revokes permissions immediately, cancels pending work, hides the marker, ends capture and releases held controls. Focus loss, lock, unavailable input observer, exhausted allowance, changed imagery or model failure also disarm input. Resume needs fresh local consent.
- Login, CAPTCHA, credentials, security prompts, billing, purchases, booking confirmation, deletion, deployment and other external commitments hand control back to the user. Local risk checks supplement model risk classification; they are not a complete defense against adversarial interfaces.
- Unknown dispatch outcomes are observed once and cannot be retried unless positively verified. Known no-dispatch failures and repeated identical actions on unchanged pixels have finite retry limits. Recovery can choose a new step on NOT_YET/DIFFERENT_STATE without crediting the previous step as complete; UNCERTAIN pauses.

## Scope and remaining work

The r20 task can move from its starting window into WhatsApp Desktop when the prompt explicitly names it, while preserving the same goal, session, verified history and mode. It is bounded to those task windows. The prompt launcher still launches browsers; other native apps must be opened and selected/summoned first. An already-open Settings window can receive screenshot-based navigation proposals, but exact brightness adjustment and real IRCTC availability searches have not been qualified end to end. No native brightness API or programmatic website inspection is used.

The r20 `open_app` primitive can launch the known WhatsApp Store package without arguments and rebind to its exact focused window. Other new windows, elevation, terminal commands, clipboard extraction, arbitrary shell execution, file uploads, drag gestures and secrets remain outside the supported primitives. A task that needs an unsupported action or a new window pauses for the user. Other application launch/window handoffs remain unimplemented.

## Evidence

The r14 release passes 108 Python, 109 Core/Platform and 40 app/auth tests (257 total), with a clean desktop build. Fake-input tests cover action/verify/complete, guided-mode isolation, missing consent, grant expiry/budget/replay/payload mutation, stale evidence, changed pixels, manual handoff, unknown outcomes, repeated non-progress, late responses after Stop/takeover, and fresh consent on resume. API tests cover coordinate replacement, separate accounting, idempotency, stale grounding and recovery without false progress. Headless Avalonia renders cover the autonomous prompt and paused companion.

The candidate ran a single real Gemini call from the AWS pilot against a generated synthetic settings image: an autonomous low-risk click proposal and current-image binding passed in 5,567 ms. The native coordinate locator was separately qualified on a synthetic browser image in r12. The integrated two-model route is tested with fake providers; this release did not execute real mouse or keyboard input. Native autonomous workflows, adversarial UI, focus/hover timing, multi-monitor behavior and extended typing/Stop races remain unqualified. The earlier disposable one-click consent was for a previous smoke test and was not reused.

Artifacts: `artifacts/windows-autonomous`, `artifacts/DesktopGuide-windows-x64-autonomous-pilot.zip`, and `artifacts/ui-qa/autonomous`. The package is unsigned and starts disarmed.
