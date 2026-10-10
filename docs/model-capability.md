# Model capability and evaluation

Checked against official documentation and the approved project on 11 October 2026. The active development pilot now uses managed **`gemini-3.1-flash-lite`** at `global` for guidance, verification and transcription, with `google-genai==2.29.0`, API v1 and LOW thinking. It passed a basic image/schema/target probe through AWS-role federation into `gen-lang-client-0644821693`, but **failed the broader quality check: 4/5 synthetic cases passed**. One case had an invalid evidence/target binding; this candidate is not qualified for release. No expensive-model fallback is enabled.

The [3.1 Flash-Lite model documentation](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/gemini/3-1-flash-lite) lists image/audio input and structured output. Global prices are $0.25/M text/image input, $0.50/M audio input and $1.50/M output including thinking. Compared with 3.8 Flash's introductory $0.75/$3.75 text/image-input/output rates, these are about 67%/60% lower respectively. Per-task cost also depends on retries and output length. [Official managed pricing](https://cloud.google.com/gemini-enterprise-agent-platform/generative-ai/pricing)

Automatic tool execution and native computer-use tools are disabled. Image/WAV inputs, outputs and context are bounded. Provider output supplies guidance/proposals only; strict Pydantic contracts, current-observation checks and local permissions remain mandatory.

The SDK initially rejected the Pydantic action union's `discriminator`/`oneOf` fields before sending a request. `generation_schema` adapts that provider-facing union to supported `anyOf`; unique literal action kinds preserve its alternatives. Original wire schemas and strict returned-result validation remain unchanged. Regression tests encode the real contract through the pinned SDK without network access. See [structured-output documentation](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/capabilities/control-generated-output) and [model documentation](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/gemini/3-8-flash).

## Active cheap-model checks

- 3.1 Flash-Lite basic probe: image/schema/target passed; 3,557 input and 740 output tokens. The shared validation ledger retained conservative older-model rates ($0.0108855); this is not an invoice estimate at the new unit rates.
- Five synthetic cases: four correct; case 001 failed the combined binding check. Wilson 95% interval 37.6–96.4%; the 95% target gate is not met. The combined diagnostic does not identify which observation/evidence/geometry/target/completion subcheck failed. Strict server/client validation remains enabled; no test criterion was relaxed to promote this result.
- An alternative 3.5 Flash-Lite basic probe failed with `ProviderUnavailable`; diagnostics did not establish whether timeout/schema/provider support caused it. It was not promoted and no broader evaluation ran. Its unknown usage retains the $0.08 reservation.
- API r5 is running, reports ready, and runtime configuration confirms all three model settings are 3.1 Flash-Lite. Local metadata reports are in `artifacts/ui-qa/prompt-live` and `artifacts/ui-qa/companion/runtime-check.json`.

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

Application inference separately has a $20 global/$4 user allowance and bounded accounted retries. Current application estimates use $0.50/M input (conservatively covering audio as well as cheaper images/text) and $1.50/M output including thinking. Synthetic validation deliberately retains its older conservative estimates/reservations; the shared ledger now totals **$0.5999045 of the approved $1**. Application allowances and cloud budgets are not hard provider billing cutoffs. Changing models requires reviewing both capability results and the configured prices.
