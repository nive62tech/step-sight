# Model capability and evaluation

Checked against official documentation and the approved project on 11 October 2026. Live managed `gemini-3.8-flash` at `global`, `google-genai==2.29.0`, API v1 and LOW thinking passed image/structured-output/target checks through AWS-role federation into `gen-lang-client-0644821693`.

Automatic tool execution and native computer-use tools are disabled. Image/WAV inputs, outputs and context are bounded. Provider output supplies guidance/proposals only; strict Pydantic contracts, current-observation checks and local permissions remain mandatory.

The SDK initially rejected the Pydantic action union's `discriminator`/`oneOf` fields before sending a request. `generation_schema` adapts that provider-facing union to supported `anyOf`; unique literal action kinds preserve its alternatives. Original wire schemas and strict returned-result validation remain unchanged. Regression tests encode the real contract through the pinned SDK without network access. See [structured-output documentation](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/capabilities/control-generated-output) and [model documentation](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/gemini/3-8-flash).

## Observed checks

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

Application inference separately has a $20 global/$4 user allowance and bounded accounted retries. The [managed pricing page](https://cloud.google.com/gemini-enterprise-agent-platform/generative-ai/pricing) includes introductory pricing; estimates use conservative $1.50/M input and $7.50/M output including thinking. Application allowances and cloud budgets are not hard provider billing cutoffs.