using DesktopGuide.Core;
using DesktopGuide.Platform;
using Xunit;

namespace DesktopGuide.Tests;

public sealed class BrowserLaunchTests
{
    [Theory]
    [InlineData("Help me deploy on Railway", "chrome", "https://railway.com/dashboard", "railway")]
    [InlineData("Open Firefox and help me on Railway", "firefox", "https://railway.com/dashboard", "railway")]
    [InlineData("Use Edge on https://example.org/settings to change my theme", "edge", "https://example.org/settings", "browser")]
    public void PromptSelectsOnlyBrowserAndStartingWebsite(string prompt, string browser, string address, string lesson)
    {
        var plan = BrowserLaunch.FromPrompt(prompt);
        Assert.Equal(browser, plan.Browser); Assert.Equal(address, plan.Address.AbsoluteUri); Assert.Equal(lesson, plan.LessonId);
    }
    [Theory]
    [InlineData("")] [InlineData("do something")]
    [InlineData("Open chrome and firefox on Railway")]
    [InlineData("Open http://example.org")]
    [InlineData("Open https://user:password@example.org")]
    [InlineData("Open https://127.0.0.1")]
    [InlineData("Open https://localhost")]
    [InlineData("Open https://example.org:4567")]
    [InlineData("Open https://example.org and https://example.com")]
    public void AmbiguousOrUnsafeLaunchDoesNotGuess(string prompt) => Assert.Throws<SafetyException>(() => BrowserLaunch.FromPrompt(prompt));
}
