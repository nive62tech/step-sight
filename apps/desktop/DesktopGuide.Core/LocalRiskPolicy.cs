namespace DesktopGuide.Core;

public static class LocalRiskPolicy
{
    public static void RequireAutonomousAction(GuidedActionContext context)
    {
        var step = context.Step; var proposal = context.Proposal;
        proposal.Action.Validate();
        if (proposal.Action.Chord == "Ctrl+C") throw new SafetyException("Clipboard access stays manual in autonomous mode.");
        if (proposal.Risk != "low" || proposal.ProviderConfirmationRequired || step.ExpectedUserAction == "manual"
            || step.Uncertainty.Length != 0 || step.Evidence.Length == 0 || step.ExpectedUserAction != proposal.Action.Kind)
            throw new SafetyException("This step needs your input or confirmation. Complete it manually, then resume.");
        // Screen text is untrusted; it cannot widen the task's local control grant.
        // These action-level checks supplement model risk classification, not the goal:
        // a goal involving booking may still contain ordinary search/navigation steps.
        var description = step.Instruction + " " + proposal.Effect + " " + step.Target?.VisibleLabel;
        // Wheel scrolling a visible reading region does not submit its contents.
        // A payment/login word inside an information banner must not prohibit reading it.
        if (proposal.Action.Kind != "scroll" && System.Text.RegularExpressions.Regex.IsMatch(description,
            @"\b(password|passcode|otp|captcha|mfa|log\s*in|sign\s*in|payment|pay|purchase|buy|checkout|billing|delete|erase|format|deploy|publish|install|download|administrator|elevat\w*|permission|terminal|powershell|command|shell|transfer|send|submit\s+(order|booking)|confirm\s+(booking|purchase|payment)|book\s+now)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new SafetyException("This action needs you: login, security, payments and external commitments stay manual.");
        if (proposal.Action.Kind is "click" or "scroll")
        {
            var bounds = step.Target?.Bounds ?? throw new SafetyException("The action has no current visual target.");
            if (proposal.Action.X < bounds.Left || proposal.Action.X > bounds.Right || proposal.Action.Y < bounds.Top || proposal.Action.Y > bounds.Bottom)
                throw new SafetyException("Action coordinates do not match the current visual target.");
        }
        if (proposal.Action.Kind == "type" && step.Target is null)
            throw new SafetyException("Typing requires a visibly identified, focused input field.");
    }
    private static readonly string[] ManualContexts = ["password", "secret", "mfa", "captcha", "payment", "billing", "delete", "deploy", "publish", "permission", "administrator", "terminal", "command", "production"];
    public static void RequirePracticeDemonstration(string lessonId, string goal, string sourceLabel, ActionProposal proposal)
    {
        if (lessonId != "sandbox" || !sourceLabel.Contains("Desktop Guide Training Sandbox", StringComparison.OrdinalIgnoreCase))
            throw new SafetyException("Pilot demonstrations are restricted to the disposable training sandbox. Complete other applications manually.");
        var context = goal + " " + proposal.Effect + " " + proposal.Action.Text;
        if (ManualContexts.Any(word => context.Contains(word, StringComparison.OrdinalIgnoreCase)))
            throw new SafetyException("This action may affect a sensitive or external operation. Keep it manual.");
        proposal.Action.Validate();
    }
}

public sealed record GuidedActionContext(GuidanceStep Step, ActionProposal Proposal);
