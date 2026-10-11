using DesktopGuide.Core;

namespace DesktopGuide.Platform;

public sealed record CaptureSource(Guid ScopeId, Guid TargetId, string Label, string Kind);
public interface ICompanionHotkey
{
    event Action? Invoked;
    bool CompanionHotkeyRegistered { get; }
}
public interface IScreenCapture : IDisposable
{
    IReadOnlyList<CaptureSource> ListSources();
    Task<ScreenFrame> CaptureAsync(CaptureSource source, IReadOnlyList<Bounds> masks, CancellationToken cancellation);
    void StopCapture() { }
}
public interface IWindowTracker
{
    bool IsForegroundAndVisible(CaptureSource source);
    bool IsCurrentGeometry(CaptureSource source, Geometry geometry);
    bool IsPointVisible(CaptureSource source, int x, int y);
    bool IsLocked { get; }
    bool HasMeaningfulChange(ScreenFrame before, ScreenFrame after);
    bool IsGuidanceTargetCurrent(ScreenFrame before, ScreenFrame after, Target target)
        => before.Geometry == after.Geometry && before.PixelDigest == after.PixelDigest;
    bool IsActionTargetCurrent(ScreenFrame before, ScreenFrame after, InputAction action)
        => before.Geometry == after.Geometry && before.PixelDigest == after.PixelDigest;
}
public interface IUserActivityObserver : IDisposable
{
    event Action? Activity;
    event Action? Takeover;
    event Action? EmergencyStop;
    event Action? SessionUnavailable;
    bool IsAssistantInputActive { get; set; }
    bool AutonomousControlActive { get => false; set { } }
    void IgnoreWindow(nint handle);
    bool HotkeyRegistered { get; }
    bool TrackingAvailable { get; }
    bool ObservationEnabled { get; set; }
}
public interface IPointerGuide
{
    event Action? VisibilityLost { add { } remove { } }
    void SetHint(string instruction, string expectedAction) { }
    Task PointAsync(ScreenFrame frame, Target target, CaptureSource source, CancellationToken cancellation);
    Task ShowVisualAsync(ScreenFrame frame, Target target, CaptureSource source, CancellationToken cancellation) => Task.CompletedTask;
    void Hide();
}
public interface IInputExecutor
{
    string? LastDeferralReason => null;
    Task<ActionReceipt> ExecuteAsync(LocalCapability capability, ActionProposal proposal, ScreenFrame frame,
        CaptureSource source, CancellationToken cancellation);
    void ReleaseHeldControls();
}
public interface IGuidanceApiClient
{
    Task<SessionView> CreateAsync(SessionCreate request, CancellationToken cancellation);
    Task<SessionView> GetAsync(Guid session, CancellationToken cancellation);
    Task<TurnResponse> TurnAsync(Guid session, TurnRequest request, ScreenFrame frame, CancellationToken cancellation);
    Task<SessionView> IntentAsync(Guid session, string intent, int version, CancellationToken cancellation);
    Task<QuestionResponse> QuestionAsync(Guid session, int version, string text, CancellationToken cancellation);
    Task EventAsync(Guid session, LearnerEvent request, CancellationToken cancellation);
    Task<string> TranscribeAsync(byte[] wav, CancellationToken cancellation);
    Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellation);
    Task DeleteAsync(Guid session, CancellationToken cancellation);
}
public interface IAudioCapture : IDisposable
{
    bool Recording { get; }
    void Start();
    Task<byte[]> StopAsync();
    void Cancel();
}
public interface ISpeechPlayback : IDisposable
{
    void Play(byte[] wav);
    void Stop();
}
public interface ILocalSessionStore
{
    Guid DeviceId { get; }
    void SaveSummary(string lessonId, int completedSteps);
    (string LessonId, int CompletedSteps)? ReadSummary();
    void Clear();
}
