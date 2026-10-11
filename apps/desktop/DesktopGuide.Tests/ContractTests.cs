using System.Text.Json;
using DesktopGuide.Core;
using Xunit;

namespace DesktopGuide.Tests;

public sealed class ContractTests
{
    [Theory]
    [InlineData("TurnResponse")]
    [InlineData("SessionView")]
    [InlineData("ActionProposal")]
    [InlineData("BrowserPlanRequest")]
    [InlineData("BrowserPlanResponse")]
    [InlineData("RemotePollResponse")]
    public void SharedFixtureRoundTripsWithoutUnknownFields(string name)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".json"));
        object dto = name switch
        {
            "TurnResponse" => WireJson.Read<TurnResponse>(json),
            "SessionView" => WireJson.Read<SessionView>(json),
            "BrowserPlanRequest" => WireJson.Read<BrowserPlanRequest>(json),
            "BrowserPlanResponse" => WireJson.Read<BrowserPlanResponse>(json),
            "RemotePollResponse" => WireJson.Read<RemotePollResponse>(json),
            _ => JsonSerializer.Deserialize<ActionProposal>(json, WireJson.Options)!
        };
        var serialized = JsonSerializer.Serialize(dto, WireJson.Options);
        using var expected = JsonDocument.Parse(json); using var actual = JsonDocument.Parse(serialized);
        Assert.Equal(Canonical(expected.RootElement), Canonical(actual.RootElement));
    }
    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(',', value.EnumerateObject().OrderBy(p => p.Name).Select(p => p.Name + ":" + Canonical(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(',', value.EnumerateArray().Select(Canonical)) + "]",
        JsonValueKind.Number => value.GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        JsonValueKind.String when value.GetString()!.Contains('T') && DateTimeOffset.TryParse(value.GetString(), out var date) => date.ToUniversalTime().ToString("O"),
        _ => value.GetRawText()
    };
    [Fact]
    public void UnknownVersionOrPropertyFailsClosed()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "SessionView.json"));
        Assert.Throws<JsonException>(() => WireJson.Read<SessionView>(json.Replace("\"schema_version\": 1", "\"schema_version\": 2")));
        Assert.Throws<JsonException>(() => WireJson.Read<SessionView>(json.Insert(1, "\"permission_granted\":true,")));
    }
    [Fact]
    public void LocalPolicyRequiresSandboxAndManualSensitiveContexts()
    {
        var proposal = new ActionProposal(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new("type", Text: "practice"), "Enter practice name", "low", false);
        Assert.Throws<SafetyException>(() => LocalRiskPolicy.RequirePracticeDemonstration("railway", "Deploy project", "Railway", proposal));
        Assert.Throws<SafetyException>(() => LocalRiskPolicy.RequirePracticeDemonstration("sandbox", "enter password", "Desktop Guide Training Sandbox", proposal));
        LocalRiskPolicy.RequirePracticeDemonstration("sandbox", "configure workspace", "Desktop Guide Training Sandbox", proposal);
    }
}
