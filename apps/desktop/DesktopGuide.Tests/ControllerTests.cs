using DesktopGuide.Core;
using DesktopGuide.Platform;
using Xunit;

namespace DesktopGuide.Tests;

public sealed class ControllerTests
{
    [Fact]
    public async Task LostHandoffResponseRetriesObservationWithoutOpeningAppAgain()
    {
        var env = new Environment { MultiAppTask = true, AppHandoffFailures = 1 }; using var c = env.Controller();
        await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source,"Copy the meeting link, open WhatsApp and search for Sample Contact","browser",InteractionMode.Autonomous,false,false,false,true);
        Assert.Equal(1,env.AppLaunches); Assert.Equal(2,env.Events.Count(e => e == "app_handoff"));
        Assert.Equal(LessonState.Completed,c.State);
    }
    [Fact]
    public async Task StopDuringApplicationHandoffRejectsLateWindowAndFurtherInput()
    {
        var env = new Environment { MultiAppTask = true, BlockHandoff = true }; using var c = env.Controller();
        await c.PreviewAsync(env.Source);
        var starting = c.StartAsync(env.Source,"Copy meeting link then open WhatsApp and search for Sample Contact","browser",InteractionMode.Autonomous,false,false,false,true);
        await env.HandoffEntered.Task.WaitAsync(TimeSpan.FromSeconds(4)); c.StopLocal();
        env.ContinueHandoff.TrySetResult(); await starting;
        Assert.Equal(1,env.AppLaunches); Assert.Equal(LessonState.Stopped,c.State);
        Assert.DoesNotContain("app_handoff",env.Events); Assert.Equal(2,env.Dispatches);
        Assert.Equal(0,env.Permissions.AutonomousActionsRemaining);
    }
    [Fact]
    public async Task PopupCycleOnChangingImagesReplansTwelveTimesWithoutMoreClicks()
    {
        var env = new Environment { CycleTask = true }; using var c = env.Controller();
        await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source,"Create a meeting and continue the task","browser",InteractionMode.Autonomous,false,false,false,true);
        Assert.Equal(12,env.Events.Count(e => e == "replan")); Assert.Equal(5,env.Dispatches);
        Assert.Equal(LessonState.Paused,c.State);
    }
    [Fact]
    public async Task MeetingCopyHandsOffToWhatsAppSearchWithoutRestartingGoal()
    {
        var env = new Environment { MultiAppTask = true }; using var c = env.Controller();
        var original = env.Source; var windows = new List<CaptureSource>(); c.TaskWindowChanged += windows.Add;
        await c.PreviewAsync(original);
        await c.StartAsync(original,"Create a meeting, copy its link, open WhatsApp Desktop and search for Sample Contact","browser",InteractionMode.Autonomous,false,false,false,true);
        Assert.Equal(LessonState.Completed,c.State); Assert.Equal(1,env.AppLaunches);
        Assert.Equal(new[] {"click","open_app","click","type"},env.DispatchedKinds);
        Assert.Equal(new[] {"start","autonomous_result","app_handoff","autonomous_result","autonomous_result"},env.Events);
        Assert.Single(windows); Assert.NotEqual(original.ScopeId,windows[0].ScopeId);
        Assert.Equal(InteractionMode.Autonomous,c.Mode);
        Assert.Equal(1,env.Creates);
        Assert.Throws<SafetyException>(() => env.Permissions.RequireObservation(original.ScopeId));
    }
    [Fact]
    public async Task ModelCannotOpenWhatsAppUnlessUserNamedItInTheTask()
    {
        var env = new Environment { MultiAppTask = true }; using var c = env.Controller();
        await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source,"Create a meeting and copy its link","browser",InteractionMode.Autonomous,false,false,false,true);
        Assert.Equal(0,env.AppLaunches); Assert.Equal(LessonState.Paused,c.State);
    }
    [Fact]
    public async Task TwelveUndispatchedAttemptsRecoverAndThirteenthRecoveryIsNotRequested()
    {
        var env = new Environment { DeferInputs = 12, CompleteAfter = 1 }; using var c = env.Controller();
        await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source,"Create a meeting link","browser",InteractionMode.Autonomous,false,false,false,true);
        Assert.Equal(12,env.Events.Count(e => e == "recover")); Assert.Equal(1,env.Dispatches);
        Assert.Equal(LessonState.Completed,c.State); Assert.Equal(12,LessonController.MaximumRecoveryAttempts);
    }
    [Fact]
    public async Task GuideRequestCannotInterruptAutonomousDispatch()
    {
        var env = new Environment { CompleteAfter = 1 }; using var c = env.Controller();
        var requested = false; Task? guide = null;
        c.StateChanged += state =>
        {
            if (state == LessonState.WaitingForLearner && !requested) { requested = true; guide = c.ShowWhereAsync(); }
        };
        await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source,"Create a meeting link","browser",InteractionMode.Autonomous,false,false,false,true);
        Assert.True(requested); await guide!;
        Assert.Equal(0,env.VisualMarkers); Assert.Equal(0,env.PointerMoves);
        Assert.Equal(1,env.Dispatches); Assert.Equal(LessonState.Completed,c.State);
    }
    [Fact] public async Task UndispatchedClicksRecoverWithoutRequestingManualCompletionOrCountingAsRepeatedActions()
    {
        var env = new Environment { DeferInputs = 3, CompleteAfter = 1 }; using var c = env.Controller();
        await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source,"Create a meeting link","browser",InteractionMode.Autonomous,false,false,false,true);
        Assert.Equal(new[] {"start","recover","recover","recover","autonomous_result"},env.Events);
        Assert.Equal(1,env.Dispatches); Assert.Equal(LessonState.Completed,c.State);
    }
    [Fact]
    public async Task TransientAnalysisRetriesFreshImagesAndReceiptFailureNeverReplaysInput()
    {
        var env = new Environment { TransientFailures = 2, ReceiptFailures = 1, CompleteAfter = 1 };
        using var c = env.Controller(); var retries = new List<int>(); c.RetryChanged += retries.Add;
        await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source, "Open preferences", "browser", InteractionMode.Autonomous, false, false, false, true);
        Assert.Equal(LessonState.Completed, c.State); Assert.Equal(1, env.Dispatches);
        Assert.Contains(2, retries); Assert.Equal(4, env.Turns);
        Assert.Equal(env.Observations.Count, env.Observations.Distinct().Count());
    }
    [Fact]
    public async Task ResumeRetriesConflictsWithoutChangingMode()
    {
        var env = new Environment { ManualStep = true, FreshResume = true, CompleteAfter = 1 };
        using var c = env.Controller(); await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source, "Open preferences", "browser", InteractionMode.Autonomous, false, false, false, true);
        env.ManualStep = false; env.ResumeFailures = 2;
        await c.ResumeAsync(true);
        Assert.Equal(InteractionMode.Autonomous, c.Mode); Assert.Equal(LessonState.Completed, c.State); Assert.Equal(1, env.Dispatches);
    }
    [Fact]
    public async Task StopDuringRetryPreventsAnotherAnalysisOrInput()
    {
        var env = new Environment { TransientFailures = 9 }; using var c = env.Controller();
        c.RetryChanged += attempt => { if (attempt == 1) c.StopLocal(); };
        await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source, "Open preferences", "browser", InteractionMode.Autonomous, false, false, false, true);
        Assert.Equal(1, env.Turns); Assert.Equal(0, env.Dispatches); Assert.Equal(LessonState.Stopped, c.State);
    }

    [Theory] [InlineData(InteractionMode.Guide)] [InlineData(InteractionMode.Autonomous)]
    public async Task ResumeReplacesOldManualEnglishStepAndKeepsOriginalMode(InteractionMode mode)
    {
        var env = new Environment { ManualStep = true, FreshResume = true, CompleteAfter = 1 }; using var c = env.Controller();
        await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source, "Find trains", "browser", mode, false, false, false, mode == InteractionMode.Autonomous);
        c.PauseLocal(); env.ManualStep = false;
        await c.ResumeAsync(mode == InteractionMode.Autonomous);
        Assert.Equal(mode, c.Mode); Assert.Contains("resume", env.Events);
        Assert.Equal(mode == InteractionMode.Autonomous ? 1 : 0, env.Dispatches);
        Assert.Equal(mode == InteractionMode.Autonomous ? LessonState.Completed : LessonState.WaitingForLearner, c.State);
        Assert.Equal("Scroll inside the notice", env.Pending!.Instruction);
    }
    [Fact]
    public async Task ObscuredTargetGetsFreshModelRecoveryAndScrollBeforeAnotherClick()
    {
        var env = new Environment { BannerRecovery = true, CompleteAfter = 1 }; using var c = env.Controller();
        await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source, "Find trains", "browser", InteractionMode.Autonomous, false, false, false, true);
        Assert.Equal(new[] { "start", "recover", "autonomous_result" }, env.Events);
        Assert.Equal(new[] { "scroll" }, env.DispatchedKinds);
        Assert.Equal(LessonState.Completed, c.State);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ResumeDrainsCancelledAnalysisAndStopStillWins(bool stop)
    {
        var env = new Environment { BlockTurn = true, CompleteAfter = 1 }; using var c = env.Controller();
        await c.PreviewAsync(env.Source);
        var starting = c.StartAsync(env.Source, "Find trains", "browser", InteractionMode.Autonomous, false, false, false, true);
        await env.TurnEntered.Task.WaitAsync(TimeSpan.FromSeconds(2)); c.PauseLocal();
        var resuming = c.ResumeAsync(true);
        Assert.False(resuming.IsCompleted); Assert.Equal(1, env.Turns); Assert.False(env.AutonomousControlActive);
        if (stop) c.StopLocal();
        env.ContinueTurn.TrySetResult(true);
        await Task.WhenAll(starting, resuming).WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Equal(stop ? LessonState.Stopped : LessonState.Completed, c.State);
        Assert.Equal(stop ? 0 : 1, env.Dispatches);
        Assert.Equal(!stop, env.Events.Contains("resume"));
    }
    [Fact]
    public async Task UnavailableTargetRecoveryIsBoundedWithoutAnyInput()
    {
        var env = new Environment { MissingTarget = true, Verify = "NOT_YET" }; using var c = env.Controller();
        await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source, "Find trains", "browser", InteractionMode.Autonomous, false, false, false, true);
        Assert.Equal(new[] { "start" }.Concat(Enumerable.Repeat("recover",LessonController.MaximumRecoveryAttempts)), env.Events);
        Assert.Equal(0, env.Dispatches); Assert.Equal(LessonState.Paused, c.State);
    }
    [Fact]
    public async Task AutonomousRunsActionsAndVerifiesEachBeforeCompleting()
    {
        var env = new Environment { CompleteAfter = 2 }; using var c = env.Controller();
        await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source, "Open preferences", "browser", InteractionMode.Autonomous, false, false, false, true);
        Assert.Equal("autonomous", env.Session.Mode); Assert.Equal(2, env.Dispatches);
        Assert.Equal(new[] { "start", "autonomous_result", "autonomous_result" }, env.Events);
        Assert.Equal(LessonState.Completed, c.State); Assert.False(env.AutonomousControlActive);
        Assert.Throws<SafetyException>(() => env.Permissions.RequireObservation(env.Source.ScopeId));
        Assert.Equal(0, env.BrowserLookups);
    }
    [Fact]
    public async Task AutonomousCannotStartWithoutSeparateTaskConsent()
    {
        var env = new Environment(); using var c = env.Controller(); await c.PreviewAsync(env.Source);
        await Assert.ThrowsAsync<SafetyException>(() => c.StartAsync(env.Source, "Open preferences", "browser", InteractionMode.Autonomous, false, false, false));
        Assert.Equal(0, env.Turns); Assert.Equal(0, env.Dispatches);
    }
    [Theory] [InlineData("manual")] [InlineData("stale")] [InlineData("timeout")] [InlineData("pixels")]
    public async Task AutonomousUnsafeOrUnavailableStepHandsBackControl(string issue)
    {
        var env = new Environment { ManualStep = issue == "manual", InvalidEvidence = issue == "stale", ServiceTimeout = issue == "timeout", ChangeBeforeDispatch = issue == "pixels" };
        using var c = env.Controller(); await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source, "Open preferences", "browser", InteractionMode.Autonomous, false, false, false, true);
        Assert.Equal(LessonState.Paused, c.State); Assert.Equal(0, env.Dispatches);
        Assert.False(env.AutonomousControlActive); Assert.False(env.ObservationEnabled);
        Assert.InRange(env.Turns, 1, LessonController.MaximumRecoveryAttempts + 1);
    }
    [Theory] [InlineData("NOT_YET")] [InlineData("DIFFERENT_STATE")] [InlineData("UNCERTAIN")]
    public async Task UnknownDispatchIsVerifiedOnceAndNeverRetried(string verification)
    {
        var env = new Environment { Outcome = "unknown", Verify = verification }; using var c = env.Controller();
        await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source, "Open preferences", "browser", InteractionMode.Autonomous, false, false, false, true);
        Assert.Equal(1, env.Dispatches); Assert.Equal(2, env.Turns); Assert.Equal(LessonState.Paused, c.State);
    }
    [Fact]
    public async Task UnchangedScreenHasABoundedActionLoop()
    {
        var env = new Environment { Verify = "NOT_YET" }; using var c = env.Controller(); await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source, "Open preferences", "browser", InteractionMode.Autonomous, false, false, false, true);
        Assert.Equal(2, env.Dispatches); Assert.Equal(3, env.Turns); Assert.Equal(LessonState.Paused, c.State);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task StopOrPhysicalTakeoverRejectsLateAutonomousPlan(bool takeover)
    {
        var env = new Environment { BlockTurn = true }; using var c = env.Controller(); await c.PreviewAsync(env.Source);
        var start = c.StartAsync(env.Source, "Open preferences", "browser", InteractionMode.Autonomous, false, false, false, true);
        await env.TurnEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (takeover) env.RaiseActivity(); else c.StopLocal();
        env.ContinueTurn.TrySetResult(true); await start;
        Assert.Equal(takeover ? LessonState.Paused : LessonState.Stopped, c.State);
        Assert.Equal(0, env.Dispatches); Assert.False(env.AutonomousControlActive);
    }
    [Fact]
    public async Task AutonomousResumeNeedsFreshConsentAndCanFinishAfterManualStep()
    {
        var env = new Environment { ManualStep = true, CompleteAfter = 1 }; using var c = env.Controller();
        await c.PreviewAsync(env.Source);
        await c.StartAsync(env.Source, "Open preferences", "browser", InteractionMode.Autonomous, false, false, false, true);
        await c.ResumeAsync(); Assert.Equal(LessonState.Paused, c.State); Assert.Equal(0, env.Dispatches);
        env.ManualStep = false; await c.ResumeAsync(true);
        Assert.Equal(LessonState.Completed, c.State); Assert.Equal(1, env.Dispatches);
    }
    private sealed class Environment : IScreenCapture, IWindowTracker, IUserActivityObserver, IPointerGuide, IInputExecutor,
        IGuidanceApiClient, ILocalSessionStore, IAudioCapture, ISpeechPlayback, IBrowserTargetResolver, IApplicationHandoff
    {
        public CaptureSource Source = new(Guid.NewGuid(), Guid.NewGuid(), "Desktop Guide Training Sandbox", "window");
        public ScreenFrame Frame;
        public SessionView Session = null!;
        public PermissionSupervisor Permissions = new();
        public GuidanceStep? Pending;
        public string Verify = "CONFIRMED";
        public bool BlockTurn;
        public int CompleteAfter;
        public int DeferInputs;
        public bool MultiAppTask;
        public bool CycleTask, BlockHandoff;
        public int AppHandoffFailures;
        public TaskCompletionSource HandoffEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueHandoff = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int AppLaunches, Creates;
        public IReadOnlyList<string> SupportedApplications => ["whatsapp"];
        public async Task<ApplicationHandoffResult> OpenAsync(LocalCapability capability, ActionProposal proposal, ScreenFrame frame,CaptureSource source,CancellationToken cancellation)
        {
            var lease = Permissions.Consume(capability,proposal,frame,true);
            Permissions.ExecuteBatch(lease,frame,true,() => { AppLaunches++; Dispatches++; DispatchedKinds.Add("open_app"); });
            HandoffEntered.TrySetResult(); if (BlockHandoff) await ContinueHandoff.Task;
            Source = new(Guid.NewGuid(),Guid.NewGuid(),"WhatsApp Desktop","window");
            var geometry = Frame.Geometry with { Id=Guid.NewGuid(),ScopeId=Source.ScopeId,LocalTargetId=Source.TargetId };
            Frame = Frame with { Geometry=geometry,Observation=Frame.Observation with { ScopeId=Source.ScopeId,GeometryId=geometry.Id },PixelDigest="WhatsApp" };
            return new(Source,lease);
        }
        public bool ManualStep, InvalidEvidence, ChangeBeforeDispatch;
        public bool BannerRecovery, FreshResume;
        public List<string> DispatchedKinds = [];
        public string Outcome = "dispatched";
        public bool AutonomousControlActive { get; set; }
        public List<string> Events = [];
        public bool ServiceTimeout;
        public int TransientFailures, ResumeFailures, ReceiptFailures;
        public List<Guid> Observations = [];
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
        { cancellation.ThrowIfCancellationRequested(); Permissions.RequireObservation(source.ScopeId); ShownTarget = target; VisualMarkers++;
            if (ChangeBeforeDispatch) Frame = Frame with { PixelDigest = Guid.NewGuid().ToString() }; return Task.CompletedTask; }
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
        public Task<ScreenFrame> CaptureAsync(CaptureSource source, IReadOnlyList<Bounds> masks, CancellationToken cancellation) => Task.FromResult(Frame = Frame with { Observation = Frame.Observation with { Id = Guid.NewGuid(), CapturedAt = DateTimeOffset.UtcNow }, PixelDigest = ChangeBeforeDispatch && Turns > 0 ? Guid.NewGuid().ToString() : Frame.PixelDigest });
        public Task PointAsync(ScreenFrame frame, Target target, CaptureSource source, CancellationToken cancellation)
        { Permissions.GuideBatch(frame, true, () => PointerMoves++); return Task.CompletedTask; }
        public void Hide() { }
        public Task<ActionReceipt> ExecuteAsync(LocalCapability capability, ActionProposal proposal, ScreenFrame frame, CaptureSource source, CancellationToken cancellation)
        {
            var lease = Permissions.Consume(capability, proposal, frame, true);
            if (DeferInputs-- > 0) return Task.FromResult(new ActionReceipt(proposal.Id,"not_dispatched",Permissions.Epoch));
            Permissions.ExecuteBatch(lease, frame, true, () => { Dispatches++; DispatchedKinds.Add(proposal.Action.Kind); });
            if (CycleTask) Frame = Frame with { PixelDigest=Guid.NewGuid().ToString() };
            return Task.FromResult(new ActionReceipt(proposal.Id, Outcome, Permissions.Epoch));
        }
        public void ReleaseHeldControls() { }
        public Task<SessionView> CreateAsync(SessionCreate request, CancellationToken cancellation)
        {
            Creates++;
            Session = new(1, Guid.NewGuid(), request.DeviceId, request.Goal, request.Mode, "en", "standard", request.ScopeId, 0, "OBSERVING", "sandbox", false, 0, DateTimeOffset.UtcNow, "gemini", null);
            return Task.FromResult(Session);
        }
        public Task<SessionView> GetAsync(Guid session, CancellationToken cancellation) => Task.FromResult(Session with { PendingStep = Pending });
        public async Task<TurnResponse> TurnAsync(Guid session, TurnRequest request, ScreenFrame frame, CancellationToken cancellation)
        {
            Turns++; Observations.Add(frame.Observation.Id); Events.Add(request.EventKind);
            if (MultiAppTask)
            {
                if (request.EventKind == "app_handoff" && AppHandoffFailures-- > 0) throw new TransientGuidanceException("Lost app handoff response");
                var action = Dispatches switch { 1 => new InputAction("open_app",Application:"whatsapp"),3 => new InputAction("type",Text:"Sample Contact"),_ => new InputAction("click",.55,.55,"left") };
                var taskComplete = Dispatches == 4;
                var taskStep = new GuidanceStep(Guid.NewGuid(),frame.Observation.Id,
                    Dispatches switch { 0 => "Copy meeting link",1 => "Open WhatsApp Desktop",2 => "Click chat search",_ => "Search for Sample Contact" },
                    "Continue the remaining part of the task",action.Kind == "open_app" ? null : new("rectangle","normalized_source_image",frame.Geometry.Id,new(.5,.5,.6,.6),"Search"),
                    action.Kind,[new("visible_state",Dispatches == 1 ? "WhatsApp Desktop visible" : "Task step visible")],[],[new(frame.Observation.Id,"Visible fixture")],[]);
                if (request.EventKind == "app_handoff")
                {
                    Assert.NotNull(request.TaskContext!.HandoffFromScopeId); Assert.Equal("whatsapp",request.TaskContext.CurrentApplication);
                    Assert.Contains("whatsapp",request.TaskContext.AllowedApplications);
                    Assert.NotEqual(Session.ScopeId,request.Observation.ScopeId);
                }
                Pending = taskComplete ? null : taskStep;
                Session = Session with { SessionVersion=request.SessionVersion+1,ScopeId=request.Observation.ScopeId,PendingStep=Pending };
                return new(1,session,Session.SessionVersion,Guid.NewGuid(),frame.Observation.Id,"gemini",Pending,
                    request.PreviousStep is null ? null : new("CONFIRMED",[new(frame.Observation.Id,"Visible result")],[],null,"gemini"),
                    taskComplete ? null : new(Guid.NewGuid(),frame.Observation.Id,frame.Geometry.Id,action,"Continue task","low",false),taskComplete);
            }
            if (TransientFailures-- > 0) throw new TransientGuidanceException("Synthetic disconnect"); TurnEntered.TrySetResult(true);
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
            if (CycleTask) step = step with { Instruction=Dispatches%3 == 0 ? "Open popup" : Dispatches%3 == 1 ? "Copy link" : "Close popup",
                Target=step.Target! with { VisibleLabel=Dispatches%3 == 0 ? "Open" : Dispatches%3 == 1 ? "Copy" : "Close" } };
            if (MissingTarget) step = step with { Target = null };
            if (WaitingForPage) step = step with { Target = null, ExpectedUserAction = "wait", Instruction = "Wait for the dashboard to load" };
            if (ManualStep) step = step with { ExpectedUserAction = "manual", Instruction = "Complete CAPTCHA yourself" };
            if (InvalidEvidence) step = step with { SourceObservationId = Guid.NewGuid() };
            var proposal = new ActionProposal(Guid.NewGuid(), frame.Observation.Id, frame.Geometry.Id, new("click", .55, .55, "left"), "Open practice Settings", "low", false);
            if (BannerRecovery && request.EventKind == "start") step = step with { Target = null, Uncertainty = ["An information banner covers the target"] };
            if (BannerRecovery && request.EventKind == "recover")
            {
                step = step with { Instruction = "Scroll down inside the payment information banner", ExpectedUserAction = "scroll", Postconditions = [new("visible_text", "Bottom of notice")] };
                proposal = proposal with { Action = new("scroll", .55, .55, Ticks: -2), Effect = "Reveal the rest of the information banner" };
            }
            if (FreshResume && !ManualStep)
            {
                step = step with { Instruction = "Scroll inside the notice", ExpectedUserAction = "scroll" };
                proposal = proposal with { Action = new("scroll", .55, .55, Ticks: -2), Effect = "Reveal the next control" };
            }
            Pending = step; Session = Session with { SessionVersion = Session.SessionVersion + 1, PendingStep = step };
            var verification = BannerRecovery && request.EventKind == "recover" ? "NOT_YET" : Verify;
            var complete = CompleteAfter > 0 && Dispatches >= CompleteAfter && Verify == "CONFIRMED";
            return new(1, session, Session.SessionVersion, Guid.NewGuid(), frame.Observation.Id, "gemini", complete ? null : step,
                request.PreviousStep is null || (FreshResume && request.EventKind == "resume") ? null : new(verification, [new(frame.Observation.Id, "Visible state")], verification == "CONFIRMED" ? [] : ["Expected state"], "Return to Settings", "gemini"), complete || step.Target is null ? null : proposal, complete);
        }
        public Task<SessionView> IntentAsync(Guid session, string intent, int version, CancellationToken cancellation)
        { if (intent == "resume" && ResumeFailures-- > 0) throw new TransientGuidanceException("Synthetic resume conflict"); Session = Session with { State = intent.ToUpperInvariant(), SessionVersion = version + 1 }; return Task.FromResult(Session); }
        public Task<QuestionResponse> QuestionAsync(Guid session, int version, string text, CancellationToken cancellation) => Task.FromResult(new QuestionResponse("Practice settings change the workspace appearance.", "gemini"));
        public Task EventAsync(Guid session, LearnerEvent request, CancellationToken cancellation) { if (ReceiptFailures-- > 0) throw new TransientGuidanceException("Receipt transport lost"); return Task.CompletedTask; }
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
        public LessonController Controller(bool browser = false) => new(this, this, this, this, this, this, this, this, this, Permissions, browser ? this : null,this);
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
        Assert.Equal(env.Observations.Last(), env.Pending.SourceObservationId);
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
