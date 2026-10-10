using DesktopGuide.Core;
using DesktopGuide.Platform;
using Xunit;

namespace DesktopGuide.Tests;

public sealed class ControllerTests
{
    private sealed class Environment : IScreenCapture, IWindowTracker, IUserActivityObserver, IPointerGuide, IInputExecutor,
        IGuidanceApiClient, ILocalSessionStore, IAudioCapture, ISpeechPlayback
    {
        public CaptureSource Source = new(Guid.NewGuid(), Guid.NewGuid(), "Desktop Guide Training Sandbox", "window");
        public ScreenFrame Frame;
        public SessionView Session = null!;
        public PermissionSupervisor Permissions = new();
        public GuidanceStep? Pending;
        public string Verify = "CONFIRMED";
        public bool BlockTurn;
        public TaskCompletionSource<bool> TurnEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ContinueTurn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PointerMoves, Dispatches, Turns, VisualMarkers, CaptureStops;
        public void StopCapture() => CaptureStops++;
        public Task ShowVisualAsync(ScreenFrame frame, Target target, CaptureSource source, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Permissions.RequireObservation(source.ScopeId); VisualMarkers++; return Task.CompletedTask; }
        public bool Foreground = true;
        public Environment()
        {
            var geometry = new Geometry(Guid.NewGuid(), Source.ScopeId, new(0, 0, 800, 450), 800, 450, 96, "test", Source.TargetId, new(0, 0, 1, 1), new(0, 0, 1, 1));
            Frame = new(new(Guid.NewGuid(), DateTimeOffset.UtcNow, Source.ScopeId, geometry.Id, 800, 450, true), geometry, [1], "synthetic");
        }
        public bool IsForegroundAndVisible(CaptureSource source) => Foreground;
        public bool IsCurrentGeometry(CaptureSource source, Geometry geometry) => geometry == Frame.Geometry;
        public bool IsPointVisible(CaptureSource source, int x, int y) => true;
        public bool HasMeaningfulChange(ScreenFrame before, ScreenFrame after) => false;
        public bool IsLocked => false;
        public bool IsAssistantInputActive { get; set; }
        public bool ObservationEnabled { get; set; }
        public bool TrackingAvailable => true;
        public bool HotkeyRegistered => true;
        public event Action? Activity;
        public event Action? Takeover;
        public event Action? EmergencyStop;
        public event Action? SessionUnavailable;
        public void IgnoreWindow(nint handle) { }
        public void RaiseActivity() => Activity?.Invoke();
        public void RaiseTakeover() => Takeover?.Invoke();
        public void RaiseStop() => EmergencyStop?.Invoke();
        public void RaiseLock() => SessionUnavailable?.Invoke();
        public IReadOnlyList<CaptureSource> ListSources() => [Source];
        public Task<ScreenFrame> CaptureAsync(CaptureSource source, IReadOnlyList<Bounds> masks, CancellationToken cancellation) => Task.FromResult(Frame);
        public Task PointAsync(ScreenFrame frame, Target target, CaptureSource source, CancellationToken cancellation)
        { Permissions.GuideBatch(frame, true, () => PointerMoves++); return Task.CompletedTask; }
        public void Hide() { }
        public Task<ActionReceipt> ExecuteAsync(LocalCapability capability, ActionProposal proposal, ScreenFrame frame, CaptureSource source, CancellationToken cancellation)
        {
            var lease = Permissions.Consume(capability, proposal, frame, true);
            Permissions.ExecuteBatch(lease, frame, true, () => Dispatches++);
            return Task.FromResult(new ActionReceipt(proposal.Id, "dispatched", Permissions.Epoch));
        }
        public void ReleaseHeldControls() { }
        public Task<SessionView> CreateAsync(SessionCreate request, CancellationToken cancellation)
        {
            Session = new(1, Guid.NewGuid(), request.DeviceId, request.Goal, request.Mode, "en", "standard", request.ScopeId, 0, "OBSERVING", "sandbox", false, 0, DateTimeOffset.UtcNow, "gemini", null);
            return Task.FromResult(Session);
        }
        public Task<SessionView> GetAsync(Guid session, CancellationToken cancellation) => Task.FromResult(Session with { PendingStep = Pending });
        public async Task<TurnResponse> TurnAsync(Guid session, TurnRequest request, ScreenFrame frame, CancellationToken cancellation)
        {
            Turns++; TurnEntered.TrySetResult(true);
            if (BlockTurn) await ContinueTurn.Task; // Deliberately ignore cancellation to exercise late-response defense.
            var step = new GuidanceStep(Guid.NewGuid(), frame.Observation.Id, "Open Settings", "Change practice preferences",
                new("rectangle", "normalized_source_image", frame.Geometry.Id, new(.5, .5, .6, .6), "Settings"), "click", [new("visible_text", "Settings")], [], [new(frame.Observation.Id, "Settings visible")], []);
            Pending = step; Session = Session with { SessionVersion = Session.SessionVersion + 1, PendingStep = step };
            var proposal = new ActionProposal(Guid.NewGuid(), frame.Observation.Id, frame.Geometry.Id, new("click", .55, .55, "left"), "Open practice Settings", "low", false);
            return new(1, session, Session.SessionVersion, Guid.NewGuid(), frame.Observation.Id, "gemini", step,
                request.PreviousStep is null ? null : new(Verify, [new(frame.Observation.Id, "Visible state")], Verify == "CONFIRMED" ? [] : ["Expected state"], "Return to Settings", "gemini"), proposal, false);
        }
        public Task<SessionView> IntentAsync(Guid session, string intent, int version, CancellationToken cancellation)
        { Session = Session with { State = intent.ToUpperInvariant(), SessionVersion = version + 1 }; return Task.FromResult(Session); }
        public Task<QuestionResponse> QuestionAsync(Guid session, int version, string text, CancellationToken cancellation) => Task.FromResult(new QuestionResponse("Practice settings change the workspace appearance.", "gemini"));
        public Task EventAsync(Guid session, LearnerEvent request, CancellationToken cancellation) => Task.CompletedTask;
        public Task<string> TranscribeAsync(byte[] wav, CancellationToken cancellation) => Task.FromResult("Why Settings?");
        public Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellation) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteAsync(Guid session, CancellationToken cancellation) => Task.CompletedTask;
        public Guid DeviceId { get; } = Guid.NewGuid();
        public void SaveSummary(string lessonId, int completedSteps) { }
        public (string LessonId, int CompletedSteps)? ReadSummary() => null;
        public void Clear() { }
        public bool Recording => false;
        public void Start() { }
        public Task<byte[]> StopAsync() => Task.FromResult(Array.Empty<byte>());
        public void Cancel() { }
        public void Play(byte[] wav) { }
        public void Stop() { }
        public void Dispose() { }
        public LessonController Controller() => new(this, this, this, this, this, this, this, this, this, Permissions);
    }
    [Fact]
    public async Task VisualGuideDoesNotNeedOrGrantPhysicalInputAndStopReleasesCapture()
    {
        var env = new Environment(); using var controller = env.Controller();
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Use the live screen", "browser", InteractionMode.Guide, false, false, false);
        Assert.Equal(1, env.VisualMarkers); Assert.Equal(0, env.PointerMoves); Assert.Equal(0, env.Dispatches);
        Assert.Throws<SafetyException>(() => env.Permissions.GuideBatch(env.Frame, true, () => throw new Exception("Should not dispatch")));
        var stops = env.CaptureStops; controller.StopLocal(); Assert.True(env.CaptureStops > stops);
        Assert.Throws<SafetyException>(() => env.Permissions.RequireObservation(env.Source.ScopeId));
    }
    [Fact]
    public async Task PointingWaitsForTheLearnerToFocusTheSelectedWindow()
    {
        var env = new Environment(); using var controller = env.Controller();
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Configure workspace", "sandbox", InteractionMode.Guide, true, false, false);
        env.Foreground = false;
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.Notice += text => { if (text.StartsWith("Switch back")) waiting.TrySetResult(); };
        var pointing = controller.ShowWhereAsync(); await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, env.PointerMoves); Assert.False(pointing.IsCompleted);
        env.Foreground = true; await pointing;
        Assert.Equal(2, env.PointerMoves); Assert.Equal(LessonState.WaitingForLearner, controller.State);
    }
    [Fact]
    public async Task StopWhileWaitingForWindowFocusPreventsPointerMovement()
    {
        var env = new Environment(); using var controller = env.Controller();
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Configure workspace", "sandbox", InteractionMode.Guide, true, false, false);
        env.Foreground = false;
        var pointing = controller.ShowWhereAsync(); Assert.False(pointing.IsCompleted);
        controller.StopLocal(); env.Foreground = true; await pointing;
        Assert.Equal(1, env.PointerMoves); Assert.Equal(LessonState.Stopped, controller.State);
    }
    [Fact]
    public async Task GuidedLessonQuestionsAndVerificationUseProductController()
    {
        var env = new Environment(); using var controller = env.Controller();
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Configure workspace", "sandbox", InteractionMode.Guide, true, false, false);
        Assert.Equal(LessonState.WaitingForLearner, controller.State); Assert.Equal(1, env.PointerMoves);
        await controller.AskAsync("Why?"); Assert.Equal(LessonState.WaitingForLearner, controller.State);
        await controller.ObserveTurnAsync("i_did_this"); Assert.Equal(2, env.Turns); Assert.Equal(0, env.Dispatches);
    }
    [Theory]
    [InlineData("UNCERTAIN")] [InlineData("DIFFERENT_STATE")] [InlineData("NOT_YET")]
    public async Task WrongTurnsDoNotAdvanceOrPointAtOldTargets(string status)
    {
        var env = new Environment(); using var controller = env.Controller();
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Configure workspace", "sandbox", InteractionMode.Guide, true, false, false);
        env.Verify = status;
        await controller.ObserveTurnAsync("i_did_this"); Assert.Equal(LessonState.Recovering, controller.State);
        await controller.ShowWhereAsync(); Assert.Equal(1, env.PointerMoves);
        for (int i = 0; i < 3; i++) await controller.ObserveTurnAsync("activity");
        Assert.Equal(LessonState.Recovering, controller.State);
    }
    [Fact]
    public async Task LateModelResponseCannotRestoreStoppedState()
    {
        var env = new Environment { BlockTurn = true }; using var controller = env.Controller();
        await controller.PreviewAsync(env.Source);
        var start = controller.StartAsync(env.Source, "Configure workspace", "sandbox", InteractionMode.Guide, true, false, false);
        await env.TurnEntered.Task; env.RaiseStop(); env.ContinueTurn.TrySetResult(true); await start;
        Assert.Equal(LessonState.Stopped, controller.State); Assert.Equal(0, env.PointerMoves); Assert.False(env.ObservationEnabled);
    }
    [Fact]
    public async Task OneDemonstrationRequiresLocalCapabilityAndThenVerification()
    {
        var env = new Environment(); using var controller = env.Controller(); ActionProposal? proposal = null;
        controller.ProposalChanged += value => proposal = value;
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Configure workspace", "sandbox", InteractionMode.Demonstrate, false, true, false);
        await controller.DemonstrateAsync(proposal!);
        Assert.Equal(1, env.Dispatches); Assert.Equal(2, env.Turns); Assert.Equal(LessonState.WaitingForLearner, controller.State);
    }
    [Fact]
    public async Task PracticeAndTakeoverStayDisarmed()
    {
        var env = new Environment(); using var controller = env.Controller();
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Configure workspace", "sandbox", InteractionMode.Practice, false, false, false);
        await controller.ShowWhereAsync(); Assert.Equal(0, env.PointerMoves);
        env.RaiseTakeover(); Assert.Equal(LessonState.Paused, controller.State); Assert.False(env.ObservationEnabled);
        Assert.Throws<SafetyException>(() => env.Permissions.RequireObservation(env.Source.ScopeId));
    }
}
