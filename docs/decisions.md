# Implementation decisions

The build specification is authoritative unless a choice below records an accepted refinement.

- Windows 11 x64, .NET SDK 10.0.401, Avalonia 12.1.4, Python 3.12.13. Dependency lockfiles record exact transitive versions.
- Connected AWS account 214422569127/profile personal; all regional resources explicitly use ap-south-1.
- Dedicated GCP project creation was blocked by quota. The user explicitly approved isolated pilot resources in existing gen-lang-client-0644821693. Terraform reads its project lifecycle/billing; it owns only pilot resources and preserves existing API enablement on destroy.
- Global managed Gemini processing allowed. No India-only processing or unconditional zero-retention claim.
- New CloudFront pay-as-you-go default hostname; custom domain later. No existing distribution is reused.
- Combined monthly target $50 before taxes/signing/domain: AWS $25, AI/speech $20, contingency $5. A target is not a guaranteed spending ceiling.
- Five invited users and two concurrent provider calls initially. One in-flight turn per session.
- Phase order places accounts before optional voice. Cloud preparation begins early; apply requires separate approval.
- Deployment and up to $1 of synthetic validation were approved. Live calls use the explicitly approved existing GCP project through a dedicated inference account and exact AWS-role federation.
- Observation/source preview is explicit. Monitor capture uses disclosed GDI; WGC windows passed the approved native smoke test. Broader native qualification remains in the acceptance report.
- Approval expires after 15 seconds; waiting-for-learner inactivity privacy-pause after five minutes.
- Raw model output is validated, never logged. No native model computer-use tool execution.

## Evidence boundaries
Generated code is not evidence of live success. Model access, disposable real-input integration, infrastructure apply, installer signing, and held-out workflow qualification have separate gates.
