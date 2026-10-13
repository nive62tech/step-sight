using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopGuide.Core;

public static class WireJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static T Read<T>(string json) where T : IVersioned
    {
        var value = JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("Empty response");
        if (value.SchemaVersion != 1) throw new JsonException("Unsupported contract version");
        return value;
    }
}

public interface IVersioned { int SchemaVersion { get; } }
public sealed record BrowserPlanRequest(int SchemaVersion, string Goal) : IVersioned;
public sealed record BrowserPlanResponse(int SchemaVersion, string Decision, string Browser, string? Address, string Message, string Provider) : IVersioned;
public sealed record Bounds(double Left, double Top, double Right, double Bottom)
{
    public void Validate()
    {
        if (!new[] { Left, Top, Right, Bottom }.All(v => double.IsFinite(v) && v is >= 0 and <= 1)
            || Left > Right || Top > Bottom) throw new SafetyException("Invalid target coordinates");
    }
    [JsonIgnore] public (double X, double Y) Center => ((Left + Right) / 2, (Top + Bottom) / 2);
}

public sealed record Target(string Kind, string CoordinateSpace, Guid GeometryId, Bounds Bounds, string VisibleLabel)
{
    public void Validate(Guid geometryId)
    {
        Bounds.Validate();
        if (Kind != "rectangle" || CoordinateSpace != "normalized_source_image" || GeometryId != geometryId)
            throw new SafetyException("Incompatible target geometry");
    }
}
public sealed record Evidence(Guid ObservationId, string Detail);
public sealed record Postcondition(string Kind, string Value);
public sealed record GuidanceStep(Guid Id, Guid SourceObservationId, string Instruction, string Explanation,
    Target? Target, string ExpectedUserAction, Postcondition[] Postconditions, string[] Hints,
    Evidence[] Evidence, string[] Uncertainty);

// No serialization or wire route exists for local capabilities.
public sealed record InputAction(string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? X = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Y = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Button = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Ticks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Chord = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Application = null)
{
    public void Validate()
    {
        if (Kind != "open_app" && Application is not null) throw new SafetyException("Unexpected application argument");
        switch (Kind)
        {
            case "click" when X.HasValue && Y.HasValue && Button == "left" && Ticks is null && Text is null && Chord is null:
                ValidatePoint(); break;
            case "scroll" when X.HasValue && Y.HasValue && Ticks is >= -3 and <= 3 && Ticks != 0 && Button is null && Text is null && Chord is null:
                ValidatePoint(); break;
            case "type" when !string.IsNullOrEmpty(Text) && Text.Length <= 256 && X is null && Y is null && Button is null && Ticks is null && Chord is null:
                if (Text.Any(char.IsControl)) throw new SafetyException("Text cannot contain commands or control characters");
                break;
            case "key_chord" when Chord is "Ctrl+A" or "Ctrl+C" or "Ctrl+S" or "Escape" or "Tab" or "Enter" or "Backspace" or "Home" or "End" or "Left" or "Right" or "Up" or "Down" && X is null && Y is null && Button is null && Ticks is null && Text is null:
                break;
            case "open_app" when Application == "whatsapp" && X is null && Y is null && Button is null && Ticks is null && Text is null && Chord is null:
                break;
            default: throw new SafetyException("Unsupported input primitive");
        }
    }
    private void ValidatePoint()
    {
        if (!double.IsFinite(X!.Value) || !double.IsFinite(Y!.Value) || X is < 0 or > 1 || Y is < 0 or > 1)
            throw new SafetyException("Invalid action point");
    }
}
public sealed record ActionProposal(Guid Id, Guid SourceObservationId, Guid GeometryId, InputAction Action,
    string Effect, string Risk, bool ProviderConfirmationRequired);
public sealed record VerificationResult(string Status, Evidence[] Evidence, string[] UnmetPostconditions,
    string? RecoverySuggestion, string Provenance);
public sealed record Observation(Guid Id, DateTimeOffset CapturedAt, Guid ScopeId, Guid GeometryId,
    int ImageWidth, int ImageHeight, bool PrivacyFiltered);
public sealed record TaskContext(string CurrentApplication, string[] AllowedApplications, Guid? HandoffFromScopeId = null);
public sealed record TurnRequest(int SchemaVersion, Guid RequestId, int SessionVersion, Observation Observation,
    string EventKind, GuidanceStep? PreviousStep,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskContext? TaskContext = null) : IVersioned;
public sealed record TurnResponse(int SchemaVersion, Guid SessionId, int SessionVersion, Guid TurnId,
    Guid ObservationId, string Provider, GuidanceStep? Step, VerificationResult? Verification,
    ActionProposal? ActionProposal, bool Completed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Dictionary<string, int>? TimingsMs = null) : IVersioned;
public sealed record SessionCreate(int SchemaVersion, Guid DeviceId, string Goal, string Mode, string Language,
    string DetailLevel, Guid ScopeId, bool SaveProgress, string LessonId) : IVersioned;
public sealed record SessionView(int SchemaVersion, Guid Id, Guid DeviceId, string Goal, string Mode,
    string Language, string DetailLevel, Guid ScopeId, int SessionVersion, string State, string LessonId,
    bool SavedProgress, int CompletedSteps, DateTimeOffset CreatedAt, string Provider, GuidanceStep? PendingStep) : IVersioned;
public sealed record SessionIntent(int SchemaVersion, int SessionVersion) : IVersioned;
public sealed record QuestionRequest(int SchemaVersion, int SessionVersion, string Text) : IVersioned;
public sealed record QuestionResponse(string Answer, string Provider);
public sealed record LearnerEvent(int SchemaVersion, Guid Id, int Sequence, long LocalEpoch,
    int SessionVersion, string Kind, Guid? ProposalId, string? Outcome) : IVersioned;
public sealed record ActionReceipt(Guid ProposalId, string Outcome, long LocalEpoch);
public sealed class SafetyException(string message) : Exception(message);
