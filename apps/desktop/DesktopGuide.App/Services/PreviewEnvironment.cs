using DesktopGuide.Core;
using DesktopGuide.Platform;

namespace DesktopGuide.App.Services;

/// <summary>Explicit UI-render preview. No desktop observation, native input, microphone, or cloud calls.</summary>
public sealed class PreviewEnvironment : IScreenCapture, IWindowTracker, IUserActivityObserver, IInputExecutor,
    ILocalSessionStore, IAudioCapture, ISpeechPlayback
{
    public IReadOnlyList<CaptureSource> ListSources() => [new(Guid.Empty, Guid.Empty, "UI preview — no desktop capture", "preview")];
    public Task<ScreenFrame> CaptureAsync(CaptureSource source, IReadOnlyList<Bounds> masks, CancellationToken cancellation) => throw new SafetyException("UI render preview cannot capture");
    public bool IsForegroundAndVisible(CaptureSource source) => false;
    public bool IsCurrentGeometry(CaptureSource source, Geometry geometry) => false;
    public bool IsPointVisible(CaptureSource source, int x, int y) => false;
    public bool HasMeaningfulChange(ScreenFrame before, ScreenFrame after) => false;
    public bool IsLocked => true;
    public bool HotkeyRegistered => false;
    public bool TrackingAvailable => false;
    public bool IsAssistantInputActive { get; set; }
    public bool ObservationEnabled { get; set; }
    public event Action? Activity { add { } remove { } }
    public event Action? Takeover { add { } remove { } }
    public event Action? EmergencyStop { add { } remove { } }
    public event Action? SessionUnavailable { add { } remove { } }
    public void IgnoreWindow(nint handle) { }
    public Task<ActionReceipt> ExecuteAsync(LocalCapability capability, ActionProposal proposal, ScreenFrame frame, CaptureSource source, CancellationToken cancellation) => throw new SafetyException("UI preview cannot dispatch");
    public void ReleaseHeldControls() { }
    public Guid DeviceId => Guid.Empty;
    public void SaveSummary(string lessonId, int completedSteps) { }
    public (string LessonId, int CompletedSteps)? ReadSummary() => null;
    public void Clear() { }
    public bool Recording => false;
    public void Start() => throw new SafetyException("UI preview cannot record audio");
    public Task<byte[]> StopAsync() => Task.FromResult(Array.Empty<byte>());
    public void Cancel() { }
    public void Play(byte[] wav) { }
    public void Stop() { }
    public void Dispose() { }
}
