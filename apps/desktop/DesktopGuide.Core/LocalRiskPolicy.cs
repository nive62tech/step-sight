namespace DesktopGuide.Core;

public static class LocalRiskPolicy
{
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
