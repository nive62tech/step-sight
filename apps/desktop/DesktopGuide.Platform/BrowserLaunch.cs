using System.Text.RegularExpressions;
using DesktopGuide.Core;

namespace DesktopGuide.Platform;

public sealed record BrowserLaunch(string Browser, Uri Address, string LessonId)
{
    public static BrowserLaunch FromPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new SafetyException("Describe what you want to do first.");
        var browsers = new[] { "chrome", "edge", "firefox" }.Where(b => Regex.IsMatch(prompt, $@"\b{b}\b", RegexOptions.IgnoreCase)).ToArray();
        if (browsers.Length > 1) throw new SafetyException("Name one browser for this task: Chrome, Edge or Firefox.");
        var browser = browsers.FirstOrDefault() ?? "chrome";
        var links = Regex.Matches(prompt, @"https?://[^\s<>""']+", RegexOptions.IgnoreCase);
        if (links.Count > 1) throw new SafetyException("Provide one starting website in your prompt.");
        var address = links.Count == 1 ? links[0].Value.TrimEnd('.', ',', ')') :
            Regex.IsMatch(prompt, @"\brailway\b", RegexOptions.IgnoreCase) ? "https://railway.com/dashboard" : null;
        if (address is null) throw new SafetyException("Include the starting https:// website in your prompt, or ask to open Railway.");
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.IsLoopback || uri.HostNameType != UriHostNameType.Dns || !uri.IsDefaultPort)
            throw new SafetyException("Use a public HTTPS website without credentials or a custom port.");
        return new(browser, uri, uri.Host is "railway.com" or "railway.app" || uri.Host.EndsWith(".railway.com", StringComparison.OrdinalIgnoreCase) ? "railway" : "browser");
    }
}

public interface IBrowserLauncher
{
    Task<CaptureSource> OpenAsync(BrowserLaunch plan, CancellationToken cancellation);
}
