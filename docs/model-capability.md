# Model capability and evaluation

Checked against official documentation and the approved project on 11 October 2026. API **r14** uses managed **`gemini-3.1-flash-lite`** at `global` for guidance, verification and transcription, with `google-genai==2.29.0`, API v1 and **MINIMAL thinking** (`GEMINI_THINKING_LEVEL=AUTO`). Current-target location uses **`gemini-3.5-flash-lite` with the dedicated desktop computer-use tool and LOW thinking**. The compact visual-response adapter passed three live state checks; real application qualification remains open.

## Autonomous proposal, r14

A generated settings image produced a valid low-risk autonomous click proposal with current observation/geometry binding in 5,567 ms. Usage: 3,388 input / 375 output / zero thinking tokens; conservative estimate $0.0078945. Native execution was not run. The integrated two-model route is covered with fake providers. The shared synthetic ledger is now $0.921288 accounted; $0.078712 remains, below a new $0.08 reservation.

## Current prompt planning and screenshots-only operation, r13

The desktop now sends the goal to authenticated `/v1/browser/plan` before opening a window. Gemini selects the browser and public HTTPS starting destination, or asks a clarification; there is no local website-name map, regex intent classifier or URL requirement in the prompt. Unknown sites can be approached through a short, non-sensitive search rather than an invented domain. Every destination starts the same generic live screenshot lesson. Planning creates no observation or input grant, stores only usage metadata, and uses the existing budget/reservation path. Stop rejects late plans before launch.

The user selected screenshots only. The native browser control resolver is no longer registered; no UIA, DOM or browser inspection informs current targeting. Image-based planning and the dedicated coordinate locator remain active. Local window geometry and visibility checks still control when a marker may be drawn.

The r13 live synthetic intent check used the same typo pattern as the reported failure. It selected Chrome and `www.irctc.co.in` in **9,936ms**, using 414 input / 51 output tokens and zero reported thinking tokens. Conservative validation estimate: $0.0010035. No browser was opened by the probe. This checks model intent interpretation, not a complete IRCTC lesson; raw goal/response text is absent from the report. Runtime readiness and public anonymous rejection passed.

## Historical Autobots comparison and dedicated locator, r12

The read-only comparison with the local `origin-autobots` repository found that its general desktop targeting uses the dedicated Gemini computer-use tool, rather than model-generated JSON rectangles. Desktop Guide now follows that proposal format for current-target location: desktop environment, one point in integer 0–999 image coordinates, LOW thinking and SDK automatic execution disabled. Only one click/move point is accepted; unsupported actions, malformed coordinates, multiple calls or provider-required confirmation yield no target. The response is converted into a current-observation-bound visual hint, never an executable action proposal. See [managed computer-use documentation](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/computer-use).

Ordinary planning and verification remain on 3.1 Flash-Lite. A missing, stale or rejected browser target triggers one locator request; Find click target explicitly retries the same instruction. A NOT_YET verification now refreshes the current instruction's location after releasing the turn gate, instead of returning early with no marker. It never marks the instruction complete. The browser adapter can expand the returned point to the single enabled, unmasked accessible control containing it, including an unlabeled icon, then checks identity, occlusion and a fresh image. The companion moves away before its own window can obstruct the hit test. Inaccessible or overlapping controls still withhold the marker.

The r12 candidate's live synthetic browser/distractor check passed through AWS-to-GCP federation in **5,165ms**, with 2,729 input tokens and 197 output tokens including 165 thinking tokens. The selected point was inside New Project, with current observation/geometry/evidence bindings. Conservative validation cost was $0.005571. API r12 was then promoted and runtime/public HTTPS readiness passed. The report is `artifacts/ui-qa/autobots-locator/live-model-r12.json`. This is one synthetic check, not a latency benchmark or proof of actual Railway accuracy. Thirteen new deterministic provider/parser cases also pass; native Windows browser/overlay qualification remains open.

## Historical browser target check, r11 (disabled in r13)

The user authorized programmatic browser inspection to improve target accuracy. The Windows client now uses the browser's accessibility API to cross-check the image-selected label against a unique enabled control, correct its physical rectangle and check its current hit-test visibility before showing the cursor. This is local and read-only, with no extra inference request. It is disabled for sandbox evaluation. It does not prove the model chose the right goal-level action, and inaccessible/icon-only/duplicate-label controls may require another image or instruction.

Chromium enables native UI Automation by default from Chrome 138. This supports the existing browser/profile without opening a debugging port. Chrome 136+ requires a non-default data directory for remote-debugging switches, so attaching CDP to the ordinary profile was not assumed. [Chrome UIA support](https://developer.chrome.com/blog/windows-uia-support-update), [remote-debugging changes](https://developer.chrome.com/blog/remote-debugging-port). Physical screen rectangles are converted through the existing capture mapping, with no second DPI multiplier. [Microsoft element coordinates](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.frompoint).

The r11 live synthetic browser-like page contained New Project, Deploy, workspace settings and templates. Gemini returned the correct exact New Project label, current bindings and center in 5,115ms (3,122 input / 388 output tokens, zero thinking). This one synthetic image is not a native browser or accuracy benchmark. The candidate was promoted only after this check; the actual Windows UIA/overlay path remains unqualified because the native test helper cannot initialize. Fifteen new deterministic browser-grounding cases passed.

## Latency and response correction, r10

The earlier authenticated API sample spent 29,699ms in inference and 95ms in upload validation. Recent model durations also ranged from about 3 to 30 seconds, so a loaded browser page did not imply a quick model response. Model latency is variable; the samples below are not a p95 guarantee.

On the same synthetic Save image, 3.1 Flash-Lite MINIMAL took 6,465ms and passed target/binding checks; 2.5 Flash with thinking disabled took 5,139ms but failed the combined target/binding check; 3.5 Flash-Lite MINIMAL took 6,376ms and passed. Further legacy-schema follow-up checks failed on both Lite models. Neither speed alone nor a single valid button is sufficient for promotion. [Thinking settings](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/thinking). The managed [2.5 Flash model retires on 20 October 2026](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/gemini/2-5-flash).

`app/vision.py` now requests semantic visual predictions without transport UUIDs. The adapter binds results to the exact observation passed to that invocation, generates local step/proposal IDs and applies the existing no-advance rule when verification is not confirmed. A model cannot supply an old observation ID in this schema. This is request association, not proof that a visual claim is true. Normalized coordinate validation, strict wire contracts, server/client evidence checks, local permissions and Stop/epoch rejection remain enforced.

Final r10 live checks through the AWS runtime and managed GCP federation passed: loading page waits without a target (9,932ms), ready page confirms loading and locates the Save button (4,431ms), unchanged unsaved page returns NOT_YET with no next step or proposal (2,296ms). All used zero reported thinking tokens. This small synthetic set does not qualify actual Railway/IRCTC/Windows screens. Sanitized reports are in `artifacts/ui-qa/fast-cursor`. The original LOW/legacy-schema 4/5 result below remains historical evidence, not a result for the new adapter.

The [3.1 Flash-Lite model documentation](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/gemini/3-1-flash-lite) lists image/audio input and structured output. Global prices are $0.25/M text/image input, $0.50/M audio input and $1.50/M output including thinking. Compared with 3.8 Flash's introductory $0.75/$3.75 text/image-input/output rates, these are about 67%/60% lower respectively. Per-task cost also depends on retries and output length. [Official managed pricing](https://cloud.google.com/gemini-enterprise-agent-platform/generative-ai/pricing)

Automatic tool execution is disabled. The dedicated computer-use tool is enabled only for visual target proposals; full computer-use execution remains disabled. Image/WAV inputs, outputs and context are bounded. Strict Pydantic contracts, current-observation checks and local permissions remain mandatory.

The SDK initially rejected the Pydantic action union's `discriminator`/`oneOf` fields before sending a request. `generation_schema` adapts that provider-facing union to supported `anyOf`; unique literal action kinds preserve its alternatives. Original wire schemas and strict returned-result validation remain unchanged. Regression tests encode the real contract through the pinned SDK without network access. See [structured-output documentation](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/capabilities/control-generated-output) and [model documentation](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/gemini/3-8-flash).

## Historical cheap-model checks before the compact adapter

- 3.1 Flash-Lite basic probe: image/schema/target passed; 3,557 input and 740 output tokens. The shared validation ledger retained conservative older-model rates ($0.0108855); this is not an invoice estimate at the new unit rates.
- Five synthetic cases: four correct; case 001 failed the combined binding check. Wilson 95% interval 37.6–96.4%; the 95% target gate is not met. The combined diagnostic does not identify which observation/evidence/geometry/target/completion subcheck failed. Strict server/client validation remains enabled; no test criterion was relaxed to promote this result.
- An alternative 3.5 Flash-Lite basic probe failed with `ProviderUnavailable`; diagnostics did not establish whether timeout/schema/provider support caused it. It was not promoted and no broader evaluation ran. Its unknown usage retains the $0.08 reservation.
- API r7 previously reported ready, and its runtime configuration confirmed all three model settings are 3.1 Flash-Lite. Local metadata reports are in `artifacts/ui-qa/prompt-live` and `artifacts/ui-qa/companion/runtime-check.json`.

## Earlier checks on Gemini 3.8 Flash

These results remain historical evidence and do not transfer to the cheaper model.

- Synthetic image/schema/grounding probe: passed; 3,551 input and 657 output tokens, about $0.010254 at conservative standard rates. No real screen or input tool.
- AWS IMDSv2 role, exact AWS-to-GCP federation and two forced credential refreshes: passed. Natural token-expiry refresh remains unassessed.
- Deployed authenticated synthetic image turn: passed through Cognito → AWS API → managed Gemini → strict current-evidence validation. One round trip was 8,359ms: 93ms upload validation, 8,148ms inference and 9ms validation/persistence. This is one sample, not a latency percentile or complete Windows lesson.
- Generated 200 synthetic cases with four workflow labels, sizes and randomized target positions. Evaluated 20 held-out cases: 20 correct centers with matching observation/evidence/geometry bindings and no false initial completion. Point estimate 100%, Wilson 95% interval 83.9–100%; conservative reported evaluation estimate $0.2034795.

Expected target coordinates stay in the evaluation harness and are never sent in model prompts. These generated pictures are not actual Notepad, VS Code or Railway screenshots. Their result does not qualify those applications or establish a reliable ≥95% real-workflow result. No evaluation labels were used to tune target behavior.

## Remaining capability gates

Provider-side refusal/injection/stale-confirmation/cancellation scenarios, complete learner-driven product lessons, voice intelligibility, real held-out screenshots, natural credential expiry and cost per completed lesson remain unassessed. Qualification needs consented real screenshots, disabled/obscured controls, wrong turns, unrelated banners and loading/error states, with sample size and confidence interval.

English is the initial UI/prompt language. Optional transcription uses bounded WAV; goals need manual confirmation. Optional narration needs an explicitly discovered/configured supported Cloud TTS voice plus API/IAM opt-in; narration is disabled in this pilot. Missing microphone/provider/voice leaves text available. Malayalam/mixed-language and real device quality remain unqualified.

## Running probes again

```powershell
uv run python scripts/probe_gemini.py --allow-live --project gen-lang-client-0644821693
uv run python scripts/evaluate-grounding.py --generate
uv run python scripts/evaluate-grounding.py --allow-live --project gen-lang-client-0644821693 --limit 20
```

These commands require suitable credentials for the explicitly approved project. Runtime validation used a temporary inference container at the API's fixed permitted address with a persistent shared ledger, and restored the API afterward. Local Google ADC did not have equivalent deployment permissions. Do not implicitly use a different project's credentials or API key.

Every synthetic attempt reserves $0.08 in `live-validation-budget.json`; reported usage reconciles it, while unknown/missing usage retains it. The shared ceiling for this session is $1; do not reset, split or discard its ledger to obtain extra authorization. Reports/ledger are copied to local artifacts and synchronized with the runtime. Two early local-schema failures conservatively retain $0.16 despite no established billed output.

Application inference separately has a $20 global/$4 user allowance and bounded accounted retries. Current 3.1 application estimates use $0.50/M input (conservatively covering audio as well as cheaper images/text) and $1.50/M output including thinking. The 3.5 locator uses $0.30/M input and $2.50/M output including thinking. Synthetic validation deliberately retains its older conservative estimates/reservations; the shared ledger now totals **$0.9133935 of the approved $1**. Application allowances and cloud budgets are not hard provider billing cutoffs. Changing models requires reviewing both capability results and the configured prices.
