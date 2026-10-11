# Opt-in autonomous tasks

Requested 11 October 2026, after the live-guidance reliability/cursor release. This is an extension of the original guide-first specification, not a claim that autonomous control already ships.

## User flow

The prompt surface offers **Guide me** and **Do it for me**. Guide remains the default. Autonomous control is a fresh, explicit grant for the current prompt and selected/opened application. It is never restored from saved progress or inferred from signing in. The grant describes live image sharing and the permitted actions. The companion always shows what the agent is doing and a working Stop button. User takeover, lock, focus/scope loss, a failed input observer, cancellation, expiry or action limit interrupts execution.

The first device action is setting a supported laptop display's brightness to an exact percentage. The browser path opens the requested public HTTPS site and repeatedly observes, proposes one action, validates, executes and verifies the visible outcome. IRCTC availability is a search task: collect the user's route, travel date/time window and class, inspect live results, and report the observed availability. Missing details require a question. CAPTCHA/login stay interactive; booking/payment are separate decisions and are not implied by checking availability.

## Implementation

1. Add an autonomous session mode to shared contracts, generated schemas, desktop state and backend lesson context. Keep remote proposals separate from local authority.
2. Add a task-scoped local grant with expiry and an action budget. Each dispatch receives an immutable, single-use capability bound to the current image, window, geometry, epoch and exact action. Existing one-action demonstration approvals remain separate.
3. Run one serialized observe/plan/act/verify loop. No next action until the previous result is checked; do not blindly retry a click with unknown outcome. Changed screenshots require a fresh plan. A recoverable failure shows the reason and releases control.
4. Permit bounded visual click/scroll/text/navigation primitives in the granted window. Keep sensitive, irreversible or externally committing operations outside ordinary navigation. Do not add arbitrary shell execution, elevation, hidden background browsing, downloaded-code execution or secret extraction.
5. Add a typed brightness service behind a Platform interface, implemented in Windows using the OS brightness API. Validate 0–100, select active supported displays, read back the result, and report unsupported hardware honestly. Unit tests use a fake device; do not change the developer's brightness as an incidental test.
6. Add the IRCTC starting-site intent without a fixed walkthrough. The user subsequently authorized programmatic browser inspection/control to improve accuracy. Use live screenshots together with available browser accessibility/DOM information, never stored target coordinates or evaluation oracles. The read-only Windows UIA target resolver is now implemented for Guide mode; autonomous browser actions still need the task-grant and single-use capability path above. The task never claims confirmed ticket availability without a matching visible train/date/class result.
7. Extend both the main prompt surface and floating companion with mode choice, explicit control grant, live action status, Pause/Stop and fresh consent on resume. Keep guide-mode wording and behavior unchanged.
8. Test grant isolation, expiry/replay, late responses after Stop, focus/window changes, manual takeover, failed/unknown dispatch, no false completion and unsupported brightness devices. Render both modes and their error/recovery states.
9. Package the Windows build and deploy the API only after the reliability release is verified. Report deterministic, synthetic cloud and native end-to-end evidence separately. Native autonomous-input tests need their own disposable-session approval; the earlier one-click consent has already been used.

## Completion checks

- Guide mode never performs clicks or typing without its existing separate grants.
- An opted-in browser task can execute ordinary visible navigation and stop before ungranted actions.
- A brightness task changes only the requested supported display setting and verifies the reported value.
- Stop prevents any queued/new dispatch and releases held input; stale output never resumes control.
- Actual IRCTC availability and real Windows task qualification are reported only after end-to-end observation, not inferred from unit tests or synthetic pictures.
