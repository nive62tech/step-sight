using System.Text.RegularExpressions;
using DesktopGuide.Core;

namespace DesktopGuide.Platform;

public sealed record BrowserLaunch(string Browser, Uri Address, string LessonId)
{
    public static BrowserLaunch FromPlan(BrowserPlanResponse plan)
    {
        if (plan.SchemaVersion != 1 || plan.Provider != "gemini" || plan.Browser is not ("chrome" or "edge" or "firefox")
            || string.IsNullOrWhiteSpace(plan.Message) || plan.Message.Length > 500)
            throw new SafetyException("The agent returned an invalid starting decision. Try again.");
        if (plan.Decision == "clarify" && plan.Address is null) throw new SafetyException(plan.Message);
        if (plan.Decision != "open_browser" || string.IsNullOrEmpty(plan.Address) || plan.Address.Length > 2048
            || plan.Address.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\')
            || !Uri.TryCreate(plan.Address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo) || uri.IsLoopback || uri.HostNameType != UriHostNameType.Dns || !uri.IsDefaultPort
            || !Regex.IsMatch(uri.Host, @"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$")
            || new[] { ".localhost", ".local", ".internal", ".lan", ".home", ".invalid", ".test" }.Any(s => uri.Host.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            throw new SafetyException("The agent couldn't establish a public HTTPS starting website. Try describing the destination more clearly.");
        // The model chooses the destination. Every site uses the same live image loop.
        return new(plan.Browser, uri, "browser");
    }
}

public interface IBrowserPlanner
{
    Task<BrowserPlanResponse> PlanAsync(string goal, CancellationToken cancellation);
}

public interface IBrowserLauncher
{
    Task<CaptureSource> OpenAsync(BrowserLaunch plan, CancellationToken cancellation);
}
