using DesktopGuide.Core;
using DesktopGuide.Platform;
using Xunit;

namespace DesktopGuide.Tests;

public sealed class BrowserLaunchTests
{
    private static BrowserPlanResponse Decision(string address) => new(1, "open_browser", "chrome", address, "Open the starting site.", "gemini");

    [Theory]
    [InlineData("https://www.irctc.co.in/nget/train-search")]
    [InlineData("https://railway.com/dashboard")]
    [InlineData("https://example.org/a-site-not-in-any-catalog")]
    public void ModelChoosesTheWebsiteAndEverySiteUsesLiveGuidance(string address)
    {
        var plan = BrowserLaunch.FromPlan(Decision(address) with { Browser = "firefox" });
        Assert.Equal("firefox", plan.Browser); Assert.Equal(address, plan.Address.AbsoluteUri);
        Assert.Equal("browser", plan.LessonId);
    }

    [Theory]
    [InlineData("http://example.org")]
    [InlineData("https://user:password@example.org")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://localhost")]
    [InlineData("https://example.org:4567")]
    [InlineData("file:///C:/Windows")]
    [InlineData("https://private.internal")]
    [InlineData("https://example.org/\n--argument")]
    [InlineData("https://example.org\\other")]
    public void ModelOutputStillRequiresAValidLaunchDestination(string address)
        => Assert.Throws<SafetyException>(() => BrowserLaunch.FromPlan(Decision(address)));

    [Fact] public void ClarificationHasNoLaunchAndUntrustedVersionsOrProvidersAreRejected()
    {
        var clarify = Decision("https://example.org") with { Decision = "clarify", Address = null, Message = "Which website do you mean?" };
        Assert.Equal(clarify.Message, Assert.Throws<SafetyException>(() => BrowserLaunch.FromPlan(clarify)).Message);
        Assert.Throws<SafetyException>(() => BrowserLaunch.FromPlan(clarify with { Address = "https://example.org" }));
        Assert.Throws<SafetyException>(() => BrowserLaunch.FromPlan(Decision("https://example.org") with { Provider = "mock" }));
        Assert.Throws<SafetyException>(() => BrowserLaunch.FromPlan(Decision("https://example.org") with { SchemaVersion = 2 }));
        Assert.Throws<SafetyException>(() => BrowserLaunch.FromPlan(Decision("https://example.org") with { Browser = "cmd.exe" }));
    }
}
