using DesktopGuide.Core;
using System.Diagnostics;

namespace DesktopGuide.Platform;

public enum GuideMarkerState { Off, Locating, Visible, MissingTarget, ScreenChanged, FocusRequired, WaitingForPage, BrowserTargetUnconfirmed }

public sealed class LessonController : IDisposable
{
    private readonly IScreenCapture capture;
    private readonly IWindowTracker tracker;
    private readonly IUserActivityObserver activity;
    private readonly IPointerGuide pointer;
    private readonly IInputExecutor executor;
    private readonly IGuidanceApiClient api;
    private readonly ILocalSessionStore store;
    private readonly IAudioCapture microphone;
    private readonly ISpeechPlayback speech;
    private readonly IBrowserTargetResolver browserTargets;
    private readonly SemaphoreSlim turnGate = new(1);
    private readonly object sessionGate = new();
    private readonly System.Threading.Timer privacyTimer;
    private CancellationTokenSource? debounce;
    private DateTimeOffset lastActivity = DateTimeOffset.UtcNow;
    private SessionMachine machine = new();
    private SessionView? session;
    private GuidanceStep? previous;
    private ActionProposal? proposal;
    private ScreenFrame? frame;
    private CaptureSource? source;
    private string goal = "", lessonId = "sandbox";
    private InteractionMode mode;
    private bool pointAllowed, demonstrationAllowed, savedProgress;
    private int eventSequence;
    private volatile bool disposed;
    private int polling;
    private int restoringMarker;
    public GuideMarkerState MarkerState { get; private set; }
    public bool HasCurrentInstruction => previous is not null;
    public bool CanLocateCurrentTarget => previous is not null && previous.ExpectedUserAction != "wait";
    public event Action<GuideMarkerState>? MarkerStateChanged;
    private void SetMarkerState(GuideMarkerState value) { MarkerState = value; MarkerStateChanged?.Invoke(value); }
    private DateTimeOffset lastAutomaticObservation = DateTimeOffset.MinValue;
    public PermissionSupervisor Permissions { get; } = new();
    public LessonState State => machine.State;
    public IReadOnlyList<Bounds> Masks { get; set; } = [];
    public bool NarrationEnabled { get; set; }
    public string DetailLevel { get; set; } = "standard";
    public IReadOnlyDictionary<string, double> LastTimingsMs { get; private set; } = new Dictionary<string, double>();
    public event Action<IReadOnlyDictionary<string, double>>? TimingsRecorded;
    public event Action<string, string, string>? Presentation;
    public event Action<ScreenFrame>? Preview;
    public event Action<LessonState>? StateChanged;
    public event Action<ActionProposal?>? ProposalChanged;
    public event Action<string>? Notice;

    public LessonController(IScreenCapture capture, IWindowTracker tracker, IUserActivityObserver activity,
        IPointerGuide pointer, IInputExecutor executor, IGuidanceApiClient api, ILocalSessionStore store,
        IAudioCapture microphone, ISpeechPlayback speech, PermissionSupervisor permissions, IBrowserTargetResolver? browserTargets = null)
    {
        (this.capture, this.tracker, this.activity, this.pointer, this.executor, this.api, this.store, this.microphone, this.speech)
            = (capture, tracker, activity, pointer, executor, api, store, microphone, speech);
        Permissions = permissions;
        this.browserTargets = browserTargets ?? new NoBrowserTargetResolver();
        WireMachine();
        activity.Activity += LearnerActivity;
        activity.EmergencyStop += StopLocal;
        activity.SessionUnavailable += Unavailable;
        activity.Takeover += Takeover;
        pointer.VisibilityLost += MarkerVisibilityLost;
        privacyTimer = new(_ =>
        {
            if (disposed) return;
            if (State is not (LessonState.Idle or LessonState.Stopped or LessonState.Completed) && !activity.TrackingAvailable)
            { Interrupt(LessonState.PrivacyPaused); Notice?.Invoke("Local input observer heartbeat failed. Resume only after the observer is available."); return; }
            if (State is LessonState.WaitingForLearner or LessonState.WaitingForApplication or LessonState.Recovering)
            {
                if (DateTimeOffset.UtcNow - lastActivity > TimeSpan.FromMinutes(5)) Interrupt(LessonState.PrivacyPaused);
                else if (source is not null && tracker.IsLocked) Interrupt(LessonState.PrivacyPaused);
                else if (State == LessonState.WaitingForLearner && !pointAllowed && MarkerState == GuideMarkerState.FocusRequired
                         && source is not null && tracker.IsForegroundAndVisible(source)) _ = RestoreVisualMarkerAsync();
                else if (source is not null && frame is not null && tracker.IsForegroundAndVisible(source)
                         && DateTimeOffset.UtcNow - lastAutomaticObservation > TimeSpan.FromSeconds(5)) _ = PollForChangeAsync();
            }
        }, null, 1000, 1000);
    }
    private void WireMachine() => machine.Changed += state => StateChanged?.Invoke(state);
    public IReadOnlyList<CaptureSource> Sources() => capture.ListSources();

    public async Task PreviewAsync(CaptureSource selected)
    {
        if (State is not (LessonState.Idle or LessonState.Stopped or LessonState.Paused or LessonState.PrivacyPaused or LessonState.NetworkInterrupted))
            throw new SafetyException("Pause the lesson before changing capture scope");
        Permissions.GrantFromLocalConsent(selected.ScopeId, InteractionMode.Guide, true, false, false);
        frame = await capture.CaptureAsync(selected, Masks, Permissions.Cancellation);
        source = selected; Preview?.Invoke(frame);
        Notice?.Invoke("Local preview only. Review the exact source and masks before starting; nothing has been uploaded.");
    }

    public async Task StartAsync(CaptureSource selected, string selectedGoal, string selectedLesson,
        InteractionMode selectedMode, bool point, bool demonstration, bool save)
    {
        if (frame is null || source?.ScopeId != selected.ScopeId) throw new SafetyException("Preview the selected source before starting");
        if ((point || demonstration) && !activity.TrackingAvailable) throw new SafetyException("Input observer unavailable; pointer and demonstrations remain disarmed");
        StopLocal();
        machine = new SessionMachine(); WireMachine(); machine.Transition(LessonState.Consent);
        source = selected; goal = selectedGoal; lessonId = selectedLesson; mode = selectedMode;
        pointAllowed = point; demonstrationAllowed = demonstration; savedProgress = save;
        Permissions.GrantFromLocalConsent(source.ScopeId, mode, true, pointAllowed, demonstrationAllowed);
        activity.ObservationEnabled = true;
        previous = null; proposal = null; eventSequence = 0;
        machine.Transition(LessonState.Observing);
        var epoch = Permissions.Epoch;
        try
        {
            var created = await api.CreateAsync(new(1, store.DeviceId, goal, ModeName(mode), "en", DetailLevel, source.ScopeId, save, lessonId), Permissions.Cancellation);
            if (epoch != Permissions.Epoch) return;
            lock (sessionGate) session = created;
            await ObserveTurnAsync("start");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Interrupt(LessonState.NetworkInterrupted); Notice?.Invoke(Friendly(ex)); }
    }

    private static string ModeName(InteractionMode value) => value switch { InteractionMode.Guide => "guide", InteractionMode.Demonstrate => "demonstrate", _ => "practice" };

    public async Task ObserveTurnAsync(string eventKind)
    {
        if (source is null || session is null || State is LessonState.Stopped or LessonState.Paused or LessonState.PrivacyPaused or LessonState.NetworkInterrupted or LessonState.Completed) return;
        if (!await turnGate.WaitAsync(0)) return;
        var epoch = Permissions.Epoch;
        var locating = eventKind == "locate";
        var locateAfterTurn = false;
        try
        {
            if (locating && !CanLocateCurrentTarget) return;
            var token = Permissions.Cancellation;
            Permissions.RequireObservation(source.ScopeId);
            pointer.Hide(); SetMarkerState(GuideMarkerState.Locating); speech.Stop(); proposal = null; ProposalChanged?.Invoke(null);
            if (State is not (LessonState.Observing or LessonState.Verifying)) machine.Transition(LessonState.Observing);
            var timer = Stopwatch.StartNew();
            var current = await capture.CaptureAsync(source, Masks, token);
            var captureMs = timer.Elapsed.TotalMilliseconds; timer.Restart();
            if (epoch != Permissions.Epoch) return;
            frame = current;
            if (State != LessonState.Verifying) machine.Transition(previous is null ? LessonState.Planning : LessonState.Verifying);
            var localSession = session;
            var result = await api.TurnAsync(localSession.Id, new(1, Guid.NewGuid(), localSession.SessionVersion, current.Observation, eventKind, previous), current, token);
            var roundtripMs = timer.Elapsed.TotalMilliseconds; timer.Restart();
            if (epoch != Permissions.Epoch) return;
            ValidateResponse(result, localSession, current);
            if (locating && (result.Step is not { } located || previous is null || located.Id != previous.Id
                || located.Instruction != previous.Instruction || located.ExpectedUserAction != previous.ExpectedUserAction
                || !located.Postconditions.SequenceEqual(previous.Postconditions) || result.Completed
                || result.Verification is not null || result.ActionProposal is not null))
                throw new SafetyException("Target refresh tried to change the current task. Stop and start a new guide.");
            var timings = new Dictionary<string, double> { ["capture_preprocess"] = captureMs, ["roundtrip"] = roundtripMs,
                ["local_validation"] = timer.Elapsed.TotalMilliseconds };
            if (result.TimingsMs is not null) foreach (var pair in result.TimingsMs) timings["server_" + pair.Key] = pair.Value;
            LastTimingsMs = timings; TimingsRecorded?.Invoke(timings);
            session = localSession with { SessionVersion = result.SessionVersion };
            if (result.Verification is { Status: "CONFIRMED" })
            {
                session = session with { CompletedSteps = session.CompletedSteps + 1 };
                if (savedProgress) store.SaveSummary(lessonId, session.CompletedSteps);
            }
            if (previous is not null && result.Verification?.Status != "CONFIRMED" && !locating)
            {
                var waiting = previous.ExpectedUserAction == "wait";
                SetMarkerState(waiting ? GuideMarkerState.WaitingForPage : GuideMarkerState.ScreenChanged);
                machine.Transition(waiting ? LessonState.WaitingForApplication : LessonState.Recovering);
                Presentation?.Invoke(result.Verification?.RecoverySuggestion ?? "The result is uncertain. Ask for help or try again.",
                    "Previous action has not been verified.", waiting ? "Waiting for the page" : "Let's try again");
                // NOT_YET is not task completion, but it must not leave the user without a
                // target. Locate this SAME instruction on a fresh image after releasing the gate.
                locateAfterTurn = !waiting && mode == InteractionMode.Guide && result.Verification?.Status == "NOT_YET";
                return;
            }
            if (result.Completed)
            {
                SetMarkerState(GuideMarkerState.Off);
                machine.Transition(LessonState.Completed); previous = null;
                Presentation?.Invoke("Lesson complete", "The final visible outcome was confirmed by the configured provider.", result.Provider);
                Permissions.Revoke(); capture.StopCapture(); pointer.Hide(); activity.ObservationEnabled = false; return;
            }
            if (result.Step is null) throw new SafetyException("No grounded next step; reobserve or ask for help");
            previous = result.Step; proposal = result.ActionProposal;
            machine.Transition(LessonState.Presenting);
            Presentation?.Invoke(previous.Instruction, previous.Explanation, result.Provider == "mock" ? "MOCK — screen not interpreted" : "Screen-grounded guidance");
            ProposalChanged?.Invoke(proposal);
            lastActivity = DateTimeOffset.UtcNow;
            if (previous.ExpectedUserAction == "wait")
            {
                proposal = null; ProposalChanged?.Invoke(null);
                SetMarkerState(GuideMarkerState.WaitingForPage); Notice?.Invoke("");
                machine.Transition(LessonState.WaitingForApplication);
            }
            else
            {
                machine.Transition(LessonState.WaitingForLearner);
                if (mode != InteractionMode.Practice) await ShowWhereAsync();
                locateAfterTurn = mode != InteractionMode.Practice && !locating && MarkerState is GuideMarkerState.MissingTarget or GuideMarkerState.ScreenChanged or GuideMarkerState.BrowserTargetUnconfirmed;
            }
            if (NarrationEnabled) await NarrateAsync(previous.Instruction + " " + previous.Explanation, epoch);
        }
        catch (OperationCanceledException) when (epoch != Permissions.Epoch || Permissions.Cancellation.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            pointer.Hide(); SetMarkerState(previous is null ? GuideMarkerState.Off : GuideMarkerState.ScreenChanged);
            Notice?.Invoke("Live image analysis timed out. Use Check screen to retry, or Find click target for the current instruction.");
            if (State is LessonState.Planning or LessonState.Verifying) machine.Transition(LessonState.Recovering);
        }
        catch (SafetyException ex)
        {
            if (epoch != Permissions.Epoch) return;
            Permissions.CancelPendingInput(); pointer.Hide(); speech.Stop(); proposal = null; ProposalChanged?.Invoke(null);
            SetMarkerState(previous is null ? GuideMarkerState.Off : GuideMarkerState.ScreenChanged);
            Notice?.Invoke(ex.Message);
            if (State is LessonState.Planning or LessonState.Verifying) machine.Transition(LessonState.Recovering);
        }
        catch (Exception ex) { Interrupt(LessonState.NetworkInterrupted); Notice?.Invoke(Friendly(ex)); }
        finally
        {
            turnGate.Release();
            // Runs even when verification took the early-return path above. A locate call
            // never schedules another locate, verifies success, or advances the current step.
            if (locateAfterTurn && epoch == Permissions.Epoch && State is LessonState.WaitingForLearner or LessonState.Recovering)
                await ObserveTurnAsync("locate");
        }
    }

    private static void ValidateResponse(TurnResponse result, SessionView localSession, ScreenFrame current)
    {
        if (result.SchemaVersion != 1 || result.SessionId != localSession.Id || result.SessionVersion != localSession.SessionVersion + 1 || result.ObservationId != current.Observation.Id)
            throw new SafetyException("Stale or incompatible server response");
        if (result.Step is { } step)
        {
            if (step.SourceObservationId != current.Observation.Id || step.Postconditions.Length == 0 || step.Evidence.Any(e => e.ObservationId != current.Observation.Id))
                throw new SafetyException("Guidance lacks current evidence");
            step.Target?.Validate(current.Geometry.Id);
        }
        if (result.Verification is { } verification)
        {
            if (verification.Status is not ("CONFIRMED" or "NOT_YET" or "DIFFERENT_STATE" or "UNCERTAIN")
                || verification.Evidence.Any(e => e.ObservationId != current.Observation.Id)
                || verification.Status == "CONFIRMED" && (verification.Evidence.Length == 0 || verification.UnmetPostconditions.Length != 0))
                throw new SafetyException("Invalid verification result");
        }
        if (result.Completed && result.Verification?.Status != "CONFIRMED") throw new SafetyException("Completion lacks confirmation");
    }

    public async Task ShowWhereAsync()
    {
        if (source is null || frame is null || previous is null || State != LessonState.WaitingForLearner) return;
        if (previous.Target is null)
        {
            SetMarkerState(GuideMarkerState.MissingTarget);
            Notice?.Invoke("No click target was identified in this image. Read the instruction, or use Find click target for a fresh image."); return;
        }
        var selected = source; var original = frame; var step = previous;
        var epoch = Permissions.Epoch; var token = Permissions.Cancellation;
        pointer.Hide();
        try
        {
            if (step.SourceObservationId != original.Observation.Id) throw new SafetyException("The original target is stale. Reobserve and recover before pointing.");
            if (mode == InteractionMode.Practice) throw new SafetyException("Switch modes with fresh consent to enable pointer guidance");
            machine.Transition(LessonState.Pointing);
            if (!tracker.IsForegroundAndVisible(selected))
            {
                SetMarkerState(GuideMarkerState.FocusRequired);
                Notice?.Invoke("Switch back to your selected window within five seconds. The guide will point once it is in front.");
                var focusDeadline = Stopwatch.StartNew();
                while (!tracker.IsForegroundAndVisible(selected))
                {
                    if (focusDeadline.Elapsed > TimeSpan.FromSeconds(5)) throw new SafetyException("Pointing paused. Focus your selected window, then use Show me where again.");
                    await Task.Delay(100, token);
                }
            }
            token.ThrowIfCancellationRequested();
            var fresh = await capture.CaptureAsync(selected, Masks, token);
            token.ThrowIfCancellationRequested(); if (epoch != Permissions.Epoch) return;
            var target = step.Target!;
            var browser = BrowserTargetResolution.Unavailable;
            // Sandbox evaluation stays image-only. Browser inspection is a local, read-only
            // cross-check; it must never rewrite the server step or authorize input proposals.
            if (lessonId != "sandbox" && mode == InteractionMode.Guide)
            {
                browser = await browserTargets.ResolveAsync(selected, original, step, token);
                token.ThrowIfCancellationRequested(); if (epoch != Permissions.Epoch) return;
                if (browser.Status == BrowserTargetStatus.Rejected)
                { SetMarkerState(GuideMarkerState.BrowserTargetUnconfirmed); Notice?.Invoke(browser.Message); return; }
                if (browser.Status == BrowserTargetStatus.Matched)
                {
                    target = browser.Target ?? throw new SafetyException("Browser inspection returned no target.");
                    target.Validate(original.Geometry.Id);
                    // Re-capture after inspection. Validate the corrected area, not the model's
                    // original approximate area, and retain the exact image/epoch/scope guards.
                    fresh = await capture.CaptureAsync(selected, Masks, token);
                    token.ThrowIfCancellationRequested(); if (epoch != Permissions.Epoch) return;
                }
            }
            if (!tracker.IsGuidanceTargetCurrent(original, fresh, target))
            { SetMarkerState(GuideMarkerState.ScreenChanged); Notice?.Invoke("The screen changed before the cursor could appear. Use Find click target to locate this step on a fresh image."); return; }
            pointer.SetHint(step.Instruction, step.ExpectedUserAction);
            if (pointAllowed) await pointer.PointAsync(fresh, target, selected, token);
            else await pointer.ShowVisualAsync(fresh, target, selected, token);
            if (epoch == Permissions.Epoch) { SetMarkerState(GuideMarkerState.Visible); Notice?.Invoke(browser.Status == BrowserTargetStatus.Matched ? browser.Message : ""); }
        }
        catch (OperationCanceledException) { }
        catch (SafetyException ex) { if (epoch == Permissions.Epoch) { SetMarkerState(GuideMarkerState.FocusRequired); Notice?.Invoke(ex.Message); } }
        finally { if (epoch == Permissions.Epoch && State == LessonState.Pointing) machine.Transition(LessonState.WaitingForLearner); }
    }

    public async Task DemonstrateAsync(ActionProposal approvedProposal)
    {
        if (source is null || frame is null || proposal != approvedProposal || State != LessonState.WaitingForLearner)
            throw new SafetyException("Demonstration proposal is no longer current");
        LocalRiskPolicy.RequirePracticeDemonstration(lessonId, goal, source.Label, approvedProposal);
        var token = Permissions.Cancellation;
        var epoch = Permissions.Epoch;
        var capability = Permissions.ApproveFromLocalEvent(approvedProposal, frame);
        machine.Transition(LessonState.AwaitingDemonstrationApproval);
        try
        {
            speech.Stop(); pointer.Hide();
            var fresh = await capture.CaptureAsync(source, Masks, token);
            if (fresh.Geometry != frame.Geometry || fresh.PixelDigest != frame.PixelDigest) throw new SafetyException("Screen changed after approval; reobserve");
            machine.Transition(LessonState.Demonstrating);
            var receipt = await executor.ExecuteAsync(capability, approvedProposal, frame, source, token);
            if (epoch != Permissions.Epoch) return;
            proposal = null; ProposalChanged?.Invoke(null);
            Notice?.Invoke(receipt.Outcome == "unknown" ? "Action outcome is unknown. Verify before trying again." : "Action submitted. Checking the visible result.");
            if (session is not null)
                await api.EventAsync(session.Id, new(1, Guid.NewGuid(), ++eventSequence, epoch, session.SessionVersion, "dispatch", approvedProposal.Id, receipt.Outcome), token);
            machine.Transition(LessonState.Verifying);
            await ObserveTurnAsync("demonstration_result");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Notice?.Invoke(Friendly(ex));
            if (State == LessonState.AwaitingDemonstrationApproval) machine.Transition(LessonState.WaitingForLearner);
            else if (State == LessonState.Demonstrating) { machine.Transition(LessonState.Verifying); machine.Transition(LessonState.Recovering); }
        }
    }

    public async Task AskAsync(string text)
    {
        if (session is null || State is not (LessonState.WaitingForLearner or LessonState.Recovering)) return;
        Permissions.CancelPendingInput(); speech.Stop(); pointer.Hide(); proposal = null; ProposalChanged?.Invoke(null);
        var epoch = Permissions.Epoch;
        machine.Transition(LessonState.AnsweringQuestion);
        try
        {
            var answer = await api.QuestionAsync(session.Id, session.SessionVersion, text, Permissions.Cancellation);
            if (epoch != Permissions.Epoch) return;
            Presentation?.Invoke(answer.Answer, previous?.Instruction ?? "Your lesson context is preserved.", answer.Provider);
            machine.Transition(LessonState.WaitingForLearner);
            lastActivity = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Interrupt(LessonState.NetworkInterrupted); Notice?.Invoke(Friendly(ex)); }
    }
    private async Task NarrateAsync(string text, long epoch)
    {
        try
        {
            var audio = await api.SynthesizeAsync(text, Permissions.Cancellation);
            if (epoch == Permissions.Epoch && State == LessonState.WaitingForLearner) speech.Play(audio);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Notice?.Invoke("Narration unavailable; the lesson remains available in text."); }
    }
    public void BeginQuestionRecording() { speech.Stop(); microphone.Start(); }
    public async Task ForgetProgressAsync()
    {
        var snapshot = session; StopLocal(); store.Clear();
        if (snapshot is not null)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await api.DeleteAsync(snapshot.Id, deadline.Token);
            lock (sessionGate) { if (session?.Id == snapshot.Id) session = null; }
        }
        Notice?.Invoke("Local progress and the current cloud lesson deleted. Usage-only records remain for 90 days.");
    }
    public async Task EndQuestionRecordingAsync()
    {
        var wav = await microphone.StopAsync();
        var transcript = await api.TranscribeAsync(wav, Permissions.Cancellation);
        if (session is null) Notice?.Invoke("Spoken goal: " + transcript + ". Enter and confirm it before starting capture.");
        else await AskAsync(transcript);
    }
    private void LearnerActivity()
    {
        lastActivity = DateTimeOffset.UtcNow;
        if (source is null || !tracker.IsForegroundAndVisible(source) || State is not (LessonState.WaitingForLearner or LessonState.WaitingForApplication or LessonState.Recovering)) return;
        lock (sessionGate)
        {
            debounce?.Cancel(); debounce?.Dispose(); debounce = new CancellationTokenSource();
            var token = debounce.Token;
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(500, token); await ObserveTurnAsync("activity"); }
                catch (OperationCanceledException) { }
            });
        }
    }
    private async Task PollForChangeAsync()
    {
        if (Interlocked.Exchange(ref polling, 1) != 0) return;
        lastAutomaticObservation = DateTimeOffset.UtcNow;
        try
        {
            if (source is null || frame is null || State is not (LessonState.WaitingForLearner or LessonState.WaitingForApplication or LessonState.Recovering)) return;
            var epoch = Permissions.Epoch;
            Permissions.RequireObservation(source.ScopeId);
            var fresh = await capture.CaptureAsync(source, Masks, Permissions.Cancellation);
            if (epoch == Permissions.Epoch && tracker.HasMeaningfulChange(frame, fresh))
            {
                lastAutomaticObservation = DateTimeOffset.UtcNow;
                await ObserveTurnAsync("activity");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* No dispatch comes from this observer. Explicit turn shows actionable errors. */ }
        finally { Interlocked.Exchange(ref polling, 0); }
    }
    private void Takeover() { Interrupt(LessonState.Paused); Notice?.Invoke("You took control. Resume locally when ready."); }
    private void MarkerVisibilityLost()
    {
        if (State is LessonState.WaitingForLearner or LessonState.Pointing) SetMarkerState(GuideMarkerState.FocusRequired);
    }
    private async Task RestoreVisualMarkerAsync()
    {
        if (Interlocked.Exchange(ref restoringMarker, 1) != 0) return;
        var epoch = Permissions.Epoch;
        try { await ShowWhereAsync(); }
        catch (Exception) { if (epoch == Permissions.Epoch) SetMarkerState(GuideMarkerState.FocusRequired); }
        finally { Interlocked.Exchange(ref restoringMarker, 0); }
    }
    private void Unavailable() => Interrupt(LessonState.PrivacyPaused);
    private void Interrupt(LessonState state)
    {
        Permissions.Revoke(); capture.StopCapture(); pointer.Hide(); speech.Stop(); microphone.Cancel(); executor.ReleaseHeldControls();
        SetMarkerState(GuideMarkerState.Off);
        activity.ObservationEnabled = false;
        proposal = null; ProposalChanged?.Invoke(null); debounce?.Cancel();
        if (State is not (LessonState.Stopped or LessonState.Idle or LessonState.Completed)) machine.Transition(state);
    }
    public void StopLocal()
    {
        var stoppedSession = session;
        Interrupt(LessonState.Stopped);
        if (State != LessonState.Stopped) machine.Transition(LessonState.Stopped);
        if (stoppedSession is not null) _ = BestEffortIntent(stoppedSession, "stop");
    }
    public void PauseLocal()
    {
        Interrupt(LessonState.Paused);
        if (session is not null) _ = BestEffortIntent(session, "pause");
    }
    private async Task BestEffortIntent(SessionView snapshot, string intent)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var latest = await api.GetAsync(snapshot.Id, deadline.Token);
            if ((pointAllowed || demonstrationAllowed) && !activity.TrackingAvailable) throw new SafetyException("Input observer unavailable; resume remains disarmed");
            var response = await api.IntentAsync(latest.Id, intent, latest.SessionVersion, deadline.Token);
            lock (sessionGate) { if (session?.Id == snapshot.Id) session = response; }
        }
        catch { /* Local safety already completed; remote recording is best effort. */ }
    }
    public async Task ResumeAsync()
    {
        if (source is null || session is null || State is not (LessonState.Paused or LessonState.PrivacyPaused or LessonState.NetworkInterrupted)) return;
        var snapshot = session;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var latest = await api.GetAsync(snapshot.Id, deadline.Token);
            if (latest.State == "STOPPED") throw new SafetyException("This lesson was stopped. Start a new lesson with fresh consent.");
            var resumed = await api.IntentAsync(snapshot.Id, "resume", latest.SessionVersion, deadline.Token);
            session = resumed; previous = latest.PendingStep;
            machine.Transition(LessonState.Consent);
            Permissions.GrantFromLocalConsent(source.ScopeId, mode, true, pointAllowed, demonstrationAllowed);
            activity.ObservationEnabled = true;
            machine.Transition(LessonState.Observing); lastActivity = DateTimeOffset.UtcNow;
            await ObserveTurnAsync("reobserve");
        }
        catch (Exception ex) { Notice?.Invoke(Friendly(ex)); }
    }
    private static string Friendly(Exception ex) => ex is SafetyException ? ex.Message : "The service or device is unavailable. Local input is disarmed; check setup and resume explicitly.";
    public void Dispose()
    {
        disposed = true; StopLocal(); privacyTimer.Dispose(); debounce?.Cancel();
        activity.Activity -= LearnerActivity; activity.EmergencyStop -= StopLocal;
        activity.Takeover -= Takeover; activity.SessionUnavailable -= Unavailable;
        pointer.VisibilityLost -= MarkerVisibilityLost;
    }
}
