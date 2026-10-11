using DesktopGuide.Core;
using DesktopGuide.Platform;
using Xunit;

namespace DesktopGuide.Tests;

public sealed class ControllerTests
{
    private sealed class Environment : IScreenCapture, IWindowTracker, IUserActivityObserver, IPointerGuide, IInputExecutor,
        IGuidanceApiClient, ILocalSessionStore, IAudioCapture, ISpeechPlayback, IBrowserTargetResolver
    {
        public CaptureSource Source = new(Guid.NewGuid(), Guid.NewGuid(), "Desktop Guide Training Sandbox", "window");
        public ScreenFrame Frame;
        public SessionView Session = null!;
        public PermissionSupervisor Permissions = new();
        public GuidanceStep? Pending;
        public string Verify = "CONFIRMED";
        public bool BlockTurn;
        public bool ServiceTimeout;
        public bool WaitingForPage;
        public bool MissingTarget, LocateMissingTarget, ChangeLocatedInstruction;
        public int LocateCalls;
        public int BrowserLookups;
        public bool BlockBrowser, RejectBrowser;
        public Target? ShownTarget;
        public TaskCompletionSource BrowserEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueBrowser = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<BrowserTargetResolution> ResolveAsync(CaptureSource source, ScreenFrame frame, GuidanceStep step, CancellationToken cancellation)
        {
            BrowserLookups++; BrowserEntered.TrySetResult();
            if (BlockBrowser) await ContinueBrowser.Task; // Deliberately ignore cancellation.
            return RejectBrowser ? BrowserTargetResolution.Reject("Several matching browser controls")
                : new(BrowserTargetStatus.Matched, step.Target! with { Bounds = new(.7, .1, .8, .2) }, "Browser control checked");
        }
        public event Action? VisibilityLost;
        public void LoseMarker() => VisibilityLost?.Invoke();
        public TaskCompletionSource<bool> TurnEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ContinueTurn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PointerMoves, Dispatches, Turns, VisualMarkers, CaptureStops;
        public void StopCapture() => CaptureStops++;
        public Task ShowVisualAsync(ScreenFrame frame, Target target, CaptureSource source, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Permissions.RequireObservation(source.ScopeId); ShownTarget = target; VisualMarkers++; return Task.CompletedTask; }
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
            if (ServiceTimeout) throw new TaskCanceledException("Synthetic HTTP deadline");
            if (BlockTurn) await ContinueTurn.Task; // Deliberately ignore cancellation to exercise late-response defense.
            if (request.EventKind == "locate")
            {
                LocateCalls++;
                var located = request.PreviousStep! with { SourceObservationId = frame.Observation.Id,
                    Evidence = [new(frame.Observation.Id, "Settings visible in this fresh fixture")],
                    Instruction = ChangeLocatedInstruction ? "A different task" : request.PreviousStep!.Instruction,
                    Target = LocateMissingTarget ? null : new("rectangle", "normalized_source_image", frame.Geometry.Id, new(.5, .5, .6, .6), "Settings") };
                Pending = located; Session = Session with { SessionVersion = Session.SessionVersion + 1, PendingStep = located };
                return new(1, session, Session.SessionVersion, Guid.NewGuid(), frame.Observation.Id, "gemini", located, null, null, false);
            }
            var step = new GuidanceStep(Guid.NewGuid(), frame.Observation.Id, "Open Settings", "Change practice preferences",
                new("rectangle", "normalized_source_image", frame.Geometry.Id, new(.5, .5, .6, .6), "Settings"), "click", [new("visible_text", "Settings")], [], [new(frame.Observation.Id, "Settings visible")], []);
            if (MissingTarget) step = step with { Target = null };
            if (WaitingForPage) step = step with { Target = null, ExpectedUserAction = "wait", Instruction = "Wait for the dashboard to load" };
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
        public LessonController Controller(bool browser = false) => new(this, this, this, this, this, this, this, this, this, Permissions, browser ? this : null);
    }
    [Fact]
    public async Task BrowserGroundingCorrectsOnlyTheMarkerWithoutChangingServerStepOrInputProposal()
    {
        var env = new Environment(); using var controller = env.Controller(browser: true);
        ActionProposal? proposal = null; controller.ProposalChanged += value => proposal = value;
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Use live browser", "browser", InteractionMode.Guide, false, false, false);
        Assert.Equal(new Bounds(.7, .1, .8, .2), env.ShownTarget!.Bounds);
        Assert.Equal(new Bounds(.5, .5, .6, .6), env.Pending!.Target!.Bounds);
        Assert.Equal(.55, proposal!.Action.X);
        Assert.Equal(1, env.BrowserLookups); Assert.Equal(1, env.Turns);
        Assert.Equal(0, env.Dispatches); Assert.Equal(0, env.PointerMoves);
    }
    [Fact]
    public async Task AmbiguousBrowserControlGetsOneBoundedFreshLocatorAndNeverGuesses()
    {
        var env = new Environment { RejectBrowser = true }; using var controller = env.Controller(browser: true);
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Use live browser", "browser", InteractionMode.Guide, false, false, false);
        Assert.Equal(GuideMarkerState.BrowserTargetUnconfirmed, controller.MarkerState);
        Assert.Equal(0, env.VisualMarkers); Assert.Equal(2, env.Turns); Assert.Equal(1, env.LocateCalls);
    }
    [Fact]
    public async Task SandboxNeverReceivesBrowserElementInspection()
    {
        var env = new Environment(); using var controller = env.Controller(browser: true);
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Practice", "sandbox", InteractionMode.Guide, false, false, false);
        Assert.Equal(0, env.BrowserLookups); Assert.Equal(1, env.VisualMarkers);
    }
    [Fact]
    public async Task StopRejectsBrowserResultsThatIgnoreCancellation()
    {
        var env = new Environment { BlockBrowser = true }; using var controller = env.Controller(browser: true);
        await controller.PreviewAsync(env.Source);
        var start = controller.StartAsync(env.Source, "Use browser", "browser", InteractionMode.Guide, false, false, false);
        await env.BrowserEntered.Task; env.RaiseStop(); env.ContinueBrowser.TrySetResult(); await start;
        Assert.Equal(LessonState.Stopped, controller.State); Assert.Equal(0, env.VisualMarkers); Assert.Equal(0, env.Dispatches);
    }
    [Fact]
    public async Task LoadingPageWaitsForNewImageRatherThanSearchingForANonexistentClick()
    {
        var env = new Environment { WaitingForPage = true }; using var controller = env.Controller();
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Use live screen", "browser", InteractionMode.Guide, false, false, false);
        Assert.Equal(LessonState.WaitingForApplication, controller.State);
        Assert.Equal(GuideMarkerState.WaitingForPage, controller.MarkerState);
        Assert.False(controller.CanLocateCurrentTarget); Assert.Equal(1, env.Turns); Assert.Equal(0, env.LocateCalls);
        await controller.ObserveTurnAsync("locate"); Assert.Equal(1, env.Turns);
        env.Verify = "NOT_YET"; await controller.ObserveTurnAsync("activity");
        Assert.Equal(LessonState.WaitingForApplication, controller.State); Assert.Equal(0, env.VisualMarkers);
        env.WaitingForPage = false; env.Verify = "CONFIRMED"; await controller.ObserveTurnAsync("activity");
        Assert.Equal(LessonState.WaitingForLearner, controller.State); Assert.Equal(GuideMarkerState.Visible, controller.MarkerState);
        Assert.Equal(1, env.VisualMarkers); Assert.Equal(0, env.PointerMoves);
    }
    [Fact]
    public async Task ServiceTimeoutShowsRecoverableErrorInsteadOfStayingInPlanning()
    {
        var env = new Environment { ServiceTimeout = true }; using var controller = env.Controller();
        string notice = ""; controller.Notice += value => notice = value;
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Use live screen", "browser", InteractionMode.Guide, false, false, false);
        Assert.Equal(LessonState.Recovering, controller.State); Assert.Contains("timed out", notice);
        Assert.Equal(GuideMarkerState.Off, controller.MarkerState); Assert.Equal(0, env.VisualMarkers);
        env.ServiceTimeout = false; await controller.ObserveTurnAsync("reobserve");
        Assert.Equal(LessonState.WaitingForLearner, controller.State); Assert.Equal(1, env.VisualMarkers);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingMarkerGetsOneBoundedTargetSearchWithoutAdvancing(bool stillMissing)
    {
        var env = new Environment { MissingTarget = true, LocateMissingTarget = stillMissing };
        using var controller = env.Controller(); var instructions = new List<string>();
        controller.Presentation += (text, _, _) => instructions.Add(text);
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Use live screen", "browser", InteractionMode.Guide, false, false, false);
        Assert.Equal(2, env.Turns); Assert.Equal(1, env.LocateCalls);
        Assert.Equal(0, env.Session.CompletedSteps); Assert.Equal(0, env.PointerMoves); Assert.Equal(0, env.Dispatches);
        Assert.All(instructions, text => Assert.Equal("Open Settings", text));
        Assert.Equal(stillMissing ? GuideMarkerState.MissingTarget : GuideMarkerState.Visible, controller.MarkerState);
        Assert.Equal(stillMissing ? 0 : 1, env.VisualMarkers);
    }
    [Fact]
    public async Task TargetSearchCannotReplaceTheCurrentInstruction()
    {
        var env = new Environment(); using var controller = env.Controller();
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Use live screen", "browser", InteractionMode.Guide, false, false, false);
        env.ChangeLocatedInstruction = true; await controller.ObserveTurnAsync("locate");
        Assert.Equal(LessonState.Recovering, controller.State); Assert.Equal(1, env.VisualMarkers);
        Assert.Equal(GuideMarkerState.ScreenChanged, controller.MarkerState);
    }
    [Fact]
    public async Task VisualMarkerReturnsAfterFocusWithoutAnotherModelCallOrPhysicalMovement()
    {
        var env = new Environment(); using var controller = env.Controller();
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Use live screen", "browser", InteractionMode.Guide, false, false, false);
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.MarkerStateChanged += state => { if (state == GuideMarkerState.Visible) restored.TrySetResult(); };
        env.Foreground = false; env.LoseMarker(); Assert.Equal(GuideMarkerState.FocusRequired, controller.MarkerState);
        env.Foreground = true; await restored.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Equal(2, env.VisualMarkers); Assert.Equal(1, env.Turns); Assert.Equal(0, env.PointerMoves);
        controller.StopLocal(); Assert.Equal(GuideMarkerState.Off, controller.MarkerState);
    }
    [Fact]
    public async Task StopRejectsALateTargetSearch()
    {
        var env = new Environment(); using var controller = env.Controller();
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Use live screen", "browser", InteractionMode.Guide, false, false, false);
        env.BlockTurn = true; env.TurnEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var finding = controller.ObserveTurnAsync("locate"); await env.TurnEntered.Task;
        controller.StopLocal(); env.ContinueTurn.SetResult(true); await finding;
        Assert.Equal(LessonState.Stopped, controller.State); Assert.Equal(GuideMarkerState.Off, controller.MarkerState);
        Assert.Equal(1, env.VisualMarkers); Assert.Equal(0, env.Dispatches);
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
    [InlineData("UNCERTAIN")] [InlineData("DIFFERENT_STATE")]
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
    public async Task NotYetRefreshesTheSameInstructionInsteadOfLeavingTheCursorMissing()
    {
        var env = new Environment(); using var controller = env.Controller();
        await controller.PreviewAsync(env.Source);
        await controller.StartAsync(env.Source, "Open settings", "browser", InteractionMode.Guide, false, false, false);
        var originalStep = env.Pending!;
        env.Frame = env.Frame with { Observation = env.Frame.Observation with { Id = Guid.NewGuid() }, PixelDigest = "fresh screenshot" };
        env.Verify = "NOT_YET";
        await controller.ObserveTurnAsync("activity");
        Assert.Equal(LessonState.WaitingForLearner, controller.State);
        Assert.Equal(GuideMarkerState.Visible, controller.MarkerState);
        Assert.Equal(originalStep.Id, env.Pending!.Id); Assert.Equal(originalStep.Instruction, env.Pending.Instruction);
        Assert.Equal(env.Frame.Observation.Id, env.Pending.SourceObservationId);
        Assert.Equal(2, env.VisualMarkers); Assert.Equal(1, env.LocateCalls); Assert.Equal(3, env.Turns);
        Assert.Equal(0, env.Session.CompletedSteps); Assert.Equal(0, env.Dispatches);
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
