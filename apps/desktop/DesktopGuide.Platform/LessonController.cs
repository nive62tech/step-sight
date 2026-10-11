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
    private readonly SemaphoreSlim autonomousGate = new(1);
    private int resuming;
    private Task remoteIntent = Task.CompletedTask;
    private string? latestVerification;
    public const int MaximumRecoveryAttempts = 3;
    public int RetryAttempt { get; private set; }
    public event Action<int>? RetryChanged;
    private void SetRetry(int attempt) { RetryAttempt = attempt; RetryChanged?.Invoke(attempt); }
    private static bool IsTransient(Exception ex) => ex is TransientGuidanceException or HttpRequestException or OperationCanceledException;
    public GuideMarkerState MarkerState { get; private set; }
    public bool HasCurrentInstruction => previous is not null;
    public bool CanLocateCurrentTarget => previous is not null && previous.ExpectedUserAction != "wait";
    public event Action<GuideMarkerState>? MarkerStateChanged;
    private void SetMarkerState(GuideMarkerState value) { MarkerState = value; MarkerStateChanged?.Invoke(value); }
    private DateTimeOffset lastAutomaticObservation = DateTimeOffset.MinValue;
    public PermissionSupervisor Permissions { get; } = new();
    public Guid DeviceId => store.DeviceId;
    public LessonState State => machine.State;
    public InteractionMode Mode => mode;
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
            if (mode == InteractionMode.Autonomous && State is not (LessonState.Idle or LessonState.Stopped or LessonState.Completed or LessonState.Paused or LessonState.PrivacyPaused or LessonState.NetworkInterrupted)
                && source is not null && (tracker.IsLocked || !tracker.IsForegroundAndVisible(source)))
            { Interrupt(LessonState.PrivacyPaused); Notice?.Invoke("Autonomous control paused because the task window lost focus. Resume when ready."); return; }
            if (State is not (LessonState.Idle or LessonState.Stopped or LessonState.Completed or LessonState.Paused or LessonState.PrivacyPaused or LessonState.NetworkInterrupted) && !activity.TrackingAvailable)
            { Interrupt(LessonState.PrivacyPaused); Notice?.Invoke("Local input observer heartbeat failed. Resume only after the observer is available."); return; }
            if (State is LessonState.WaitingForLearner or LessonState.WaitingForApplication or LessonState.Recovering)
            {
                if (DateTimeOffset.UtcNow - lastActivity > TimeSpan.FromMinutes(5)) Interrupt(LessonState.PrivacyPaused);
                else if (source is not null && tracker.IsLocked) Interrupt(LessonState.PrivacyPaused);
                else if (mode != InteractionMode.Autonomous && State == LessonState.WaitingForLearner && !pointAllowed && MarkerState == GuideMarkerState.FocusRequired
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
        InteractionMode selectedMode, bool point, bool demonstration, bool save, bool autonomousConsent = false)
    {
        if (frame is null || source?.ScopeId != selected.ScopeId) throw new SafetyException("Preview the selected source before starting");
        if ((point || demonstration) && !activity.TrackingAvailable) throw new SafetyException("Input observer unavailable; pointer and demonstrations remain disarmed");
        if (selectedMode == InteractionMode.Autonomous && (!autonomousConsent || selected.Kind != "window" || !activity.TrackingAvailable || !tracker.IsForegroundAndVisible(selected)))
            throw new SafetyException("Autonomous mode needs fresh control consent, a focused application window and the local input observer.");
        StopLocal();
        machine = new SessionMachine(); WireMachine(); machine.Transition(LessonState.Consent);
        source = selected; goal = selectedGoal; lessonId = selectedLesson; mode = selectedMode;
        pointAllowed = point; demonstrationAllowed = demonstration; savedProgress = save;
        if (mode == InteractionMode.Autonomous) Permissions.GrantAutonomousTaskFromLocalConsent(source.ScopeId, source.TargetId, autonomousConsent);
        else Permissions.GrantFromLocalConsent(source.ScopeId, mode, true, pointAllowed, demonstrationAllowed);
        activity.ObservationEnabled = true;
        activity.AutonomousControlActive = mode == InteractionMode.Autonomous;
        previous = null; proposal = null; eventSequence = 0;
        machine.Transition(LessonState.Observing);
        var epoch = Permissions.Epoch;
        try
        {
            var created = await api.CreateAsync(new(1, store.DeviceId, goal, ModeName(mode), "en", DetailLevel, source.ScopeId, save, lessonId), Permissions.Cancellation);
            if (epoch != Permissions.Epoch) return;
            lock (sessionGate) session = created;
            await ObserveTurnAsync("start");
            if (epoch == Permissions.Epoch && mode == InteractionMode.Autonomous) await RunAutonomousAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (epoch == Permissions.Epoch) { Interrupt(LessonState.NetworkInterrupted); Notice?.Invoke(Friendly(ex)); } }
    }

    private static string ModeName(InteractionMode value) => value switch { InteractionMode.Guide => "guide", InteractionMode.Demonstrate => "demonstrate", InteractionMode.Autonomous => "autonomous", _ => "practice" };

    private async Task RunAutonomousAsync()
    {
        var epoch = Permissions.Epoch; var token = Permissions.Cancellation;
        try { await autonomousGate.WaitAsync(token); } catch (OperationCanceledException) { return; }
        var repeated = new Dictionary<(string Pixels, InputAction Action), int>();
        var blockedAttempts = 0; var waitingChecks = 0; var recoveryAttempts = 0;
        try
        {
            while (epoch == Permissions.Epoch && source is not null && frame is not null && previous is not null
                && State is LessonState.WaitingForLearner or LessonState.WaitingForApplication)
            {
                token.ThrowIfCancellationRequested();
                Permissions.RequireAutonomousTask(source.ScopeId, source.TargetId);
                if (!activity.TrackingAvailable || tracker.IsLocked || !tracker.IsForegroundAndVisible(source))
                    throw new SafetyException("Autonomous control paused: return to the task window and resume.");
                if (State == LessonState.WaitingForApplication)
                {
                    if (++waitingChecks > 40) throw new SafetyException("The application is still waiting. Resume after it is ready.");
                    await Task.Delay(1500, token);
                    var changed = await capture.CaptureAsync(source, Masks, token);
                    if (tracker.HasMeaningfulChange(frame, changed) || waitingChecks % 10 == 0) await ObserveTurnAsync("reobserve");
                    continue;
                }
                waitingChecks = 0;
                if (proposal is null && previous.ExpectedUserAction is "click" or "scroll" or "type"
                    && latestVerification != "UNCERTAIN" && recoveryAttempts < MaximumRecoveryAttempts)
                {
                    recoveryAttempts++;
                    Notice?.Invoke($"Finding another safe way forward · retry {recoveryAttempts}/{MaximumRecoveryAttempts}...");
                    await ObserveTurnAsync("recover");
                    continue;
                }
                var action = proposal ?? throw new SafetyException("No safe action is visible yet. Check the page, then Resume to analyse it again.");
                LocalRiskPolicy.RequireAutonomousAction(new(previous, action));
                Notice?.Invoke("");
                var fresh = await capture.CaptureAsync(source, Masks, token);
                if (epoch != Permissions.Epoch) return;
                if (fresh.Geometry != frame.Geometry || fresh.PixelDigest != frame.PixelDigest)
                {
                    if (++blockedAttempts > MaximumRecoveryAttempts) throw new SafetyException("The screen kept changing after 3 retries. Paused before clicking; Resume will read a fresh image.");
                    Notice?.Invoke($"Screen changed · refreshing target {blockedAttempts}/{MaximumRecoveryAttempts}...");
                    await ObserveTurnAsync("reobserve"); continue;
                }
                var key = (frame.PixelDigest, action.Action);
                var attempts = repeated.GetValueOrDefault(key);
                if (attempts >= 2) throw new SafetyException("The same action has not changed the screen. Control is paused for your review.");
                repeated[key] = attempts + 1;
                var capability = Permissions.ApproveAutonomousAction(action, frame);
                machine.Transition(LessonState.Acting);
                Presentation?.Invoke(previous.Instruction, "Autonomous mode · performing one action, then checking its result.", "Agent acting");
                var receipt = await executor.ExecuteAsync(capability, action, frame, source, token);
                if (epoch != Permissions.Epoch) return;
                proposal = null; ProposalChanged?.Invoke(null); pointer.Hide(); SetMarkerState(GuideMarkerState.Off);
                if (receipt.ProposalId != action.Id || receipt.LocalEpoch != epoch || receipt.Outcome is not ("dispatched" or "not_dispatched" or "unknown"))
                    throw new SafetyException("Input returned an incompatible receipt. Control has been paused.");
                if (session is not null)
                {
                    // Recording a receipt must not cause a second dispatch when the network fails.
                    try { await api.EventAsync(session.Id, new(1, Guid.NewGuid(), ++eventSequence, epoch, session.SessionVersion, "dispatch", action.Id, receipt.Outcome), token); }
                    catch (Exception ex) when (IsTransient(ex) && !token.IsCancellationRequested) { }
                }
                if (receipt.Outcome == "not_dispatched")
                {
                    if (++blockedAttempts > MaximumRecoveryAttempts) throw new SafetyException("Input could not reach the target after 3 fresh checks. Paused; Resume will analyse the current screen.");
                    machine.Transition(LessonState.Observing); await ObserveTurnAsync("reobserve"); continue;
                }
                blockedAttempts = 0; recoveryAttempts = 0;
                machine.Transition(LessonState.Verifying);
                await Task.Delay(650, token);
                await ObserveTurnAsync("autonomous_result");
                if (epoch != Permissions.Epoch) return;
                if (receipt.Outcome == "unknown" && latestVerification != "CONFIRMED")
                    throw new SafetyException("The last action's outcome is uncertain. Check it manually before resuming; it will not be blindly repeated.");
            }
            if (epoch == Permissions.Epoch && State is LessonState.Recovering or LessonState.WaitingForLearner)
                throw new SafetyException("The agent needs your help to establish the current state. Check the screen, then resume.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (epoch == Permissions.Epoch) { PauseLocal(); Notice?.Invoke(Friendly(ex)); }
        }
        finally { autonomousGate.Release(); }
    }

    public async Task ObserveTurnAsync(string eventKind)
    {
        if (source is null || session is null || State is LessonState.Stopped or LessonState.Paused or LessonState.PrivacyPaused or LessonState.NetworkInterrupted or LessonState.Completed) return;
        var epoch = Permissions.Epoch;
        try { await turnGate.WaitAsync(Permissions.Cancellation); } catch (OperationCanceledException) { return; }
        var locating = eventKind == "locate";
        var locateAfterTurn = false;
        try
        {
            if (epoch != Permissions.Epoch || State is LessonState.Stopped or LessonState.Paused or LessonState.PrivacyPaused or LessonState.NetworkInterrupted or LessonState.Completed) return;
            if (locating && !CanLocateCurrentTarget) return;
            var token = Permissions.Cancellation;
            Permissions.RequireObservation(source.ScopeId);
            pointer.Hide(); SetMarkerState(GuideMarkerState.Locating); speech.Stop(); proposal = null; ProposalChanged?.Invoke(null);
            if (State is not (LessonState.Observing or LessonState.Verifying)) machine.Transition(LessonState.Observing);
            var timer = Stopwatch.StartNew();
            ScreenFrame current; SessionView localSession; TurnResponse result;
            double captureMs, roundtripMs;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (attempt > 0)
                    {
                        SetRetry(attempt);
                        Notice?.Invoke($"Analysis interrupted · retry {attempt}/{MaximumRecoveryAttempts}. Reading a fresh screen...");
                        await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), token);
                        // Invalidate/drain an in-flight server turn before submitting a new image.
                        var latest = await api.GetAsync(session.Id, token);
                        if (latest.State == "STOPPED") throw new SafetyException("This task was stopped. Start a new task.");
                        var synced = await api.IntentAsync(latest.Id, "resume", latest.SessionVersion, token);
                        if (epoch != Permissions.Epoch) return;
                        session = synced;
                        if (previous?.Id != synced.PendingStep?.Id)
                        {
                            // A lost response may already have changed server context. Treat its
                            // unexecuted instruction as history, never as a completed action.
                            eventKind = "resume"; locating = false;
                        }
                        previous = synced.PendingStep;
                    }
                    timer.Restart();
                    current = await capture.CaptureAsync(source, Masks, token);
                    captureMs = timer.Elapsed.TotalMilliseconds; timer.Restart();
                    if (epoch != Permissions.Epoch) return;
                    frame = current;
                    if (State == LessonState.Observing) machine.Transition(previous is null ? LessonState.Planning : LessonState.Verifying);
                    localSession = session;
                    result = await api.TurnAsync(localSession.Id, new(1, Guid.NewGuid(), localSession.SessionVersion, current.Observation, eventKind, previous), current, token);
                    roundtripMs = timer.Elapsed.TotalMilliseconds; timer.Restart();
                    break;
                }
                catch (Exception ex) when (IsTransient(ex) && !token.IsCancellationRequested && epoch == Permissions.Epoch && attempt < MaximumRecoveryAttempts) { }
            }
            if (epoch != Permissions.Epoch) return;
            SetRetry(0); Notice?.Invoke("");
            ValidateResponse(result, localSession, current);
            latestVerification = result.Verification?.Status;
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
            if (previous is not null && result.Verification?.Status != "CONFIRMED" && !locating && eventKind != "resume"
                && !(mode == InteractionMode.Autonomous && result.Step is not null && result.Verification?.Status is "NOT_YET" or "DIFFERENT_STATE"))
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
                Permissions.Revoke(); capture.StopCapture(); pointer.Hide(); activity.ObservationEnabled = false; activity.AutonomousControlActive = false; return;
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
                if (mode is not (InteractionMode.Practice or InteractionMode.Autonomous)) await ShowWhereAsync();
                locateAfterTurn = mode is not (InteractionMode.Practice or InteractionMode.Autonomous) && !locating && MarkerState is GuideMarkerState.MissingTarget or GuideMarkerState.ScreenChanged or GuideMarkerState.BrowserTargetUnconfirmed;
            }
            if (NarrationEnabled) await NarrateAsync(previous.Instruction + " " + previous.Explanation, epoch);
        }
        catch (OperationCanceledException) when (epoch != Permissions.Epoch || Permissions.Cancellation.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            pointer.Hide(); SetMarkerState(previous is null ? GuideMarkerState.Off : GuideMarkerState.ScreenChanged);
            if (mode == InteractionMode.Autonomous) { PauseLocal(); Notice?.Invoke("Analysis timed out after 3 retries. Resume will read a fresh screen in autonomous mode."); return; }
            Notice?.Invoke("Live image analysis timed out after 3 retries. Use Check screen to retry.");
            if (State is LessonState.Planning or LessonState.Verifying) machine.Transition(LessonState.Recovering);
        }
        catch (SafetyException ex)
        {
            if (epoch != Permissions.Epoch) return;
            if (mode == InteractionMode.Autonomous) { PauseLocal(); Notice?.Invoke(ex.Message); return; }
            Permissions.CancelPendingInput(); pointer.Hide(); speech.Stop(); proposal = null; ProposalChanged?.Invoke(null);
            SetMarkerState(previous is null ? GuideMarkerState.Off : GuideMarkerState.ScreenChanged);
            Notice?.Invoke(ex.Message);
            if (State is LessonState.Planning or LessonState.Verifying) machine.Transition(LessonState.Recovering);
        }
        catch (Exception ex) { if (epoch == Permissions.Epoch) { Interrupt(LessonState.NetworkInterrupted); Notice?.Invoke(Friendly(ex)); } }
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
        if (mode == InteractionMode.Autonomous && activity.AutonomousControlActive) { Takeover(); return; }
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
        if (mode == InteractionMode.Autonomous) return; // The serialized autonomous loop owns observation.
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
    private void Takeover() { if (!activity.ObservationEnabled) return; Interrupt(LessonState.Paused); Notice?.Invoke("You took control. Resume locally when ready."); }
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
    private void Unavailable() { if (activity.ObservationEnabled) Interrupt(LessonState.PrivacyPaused); }
    private void Interrupt(LessonState state)
    {
        Permissions.Revoke(); capture.StopCapture(); pointer.Hide(); speech.Stop(); microphone.Cancel(); executor.ReleaseHeldControls();
        SetRetry(0);
        SetMarkerState(GuideMarkerState.Off);
        activity.ObservationEnabled = false;
        activity.AutonomousControlActive = false;
        proposal = null; ProposalChanged?.Invoke(null); debounce?.Cancel();
        if (State is not (LessonState.Stopped or LessonState.Idle or LessonState.Completed)) machine.Transition(state);
    }
    public void StopLocal()
    {
        if (State == LessonState.Stopped) { Interrupt(LessonState.Stopped); return; }
        var stoppedSession = session;
        Interrupt(LessonState.Stopped);
        if (State != LessonState.Stopped) machine.Transition(LessonState.Stopped);
        if (stoppedSession is not null) QueueIntent(stoppedSession, "stop");
    }
    public void PauseLocal()
    {
        if (State is LessonState.Stopped or LessonState.Paused or LessonState.PrivacyPaused or LessonState.NetworkInterrupted) return;
        Interrupt(LessonState.Paused);
        if (session is not null) QueueIntent(session, "pause");
    }
    private void QueueIntent(SessionView snapshot, string intent)
    {
        lock (sessionGate) remoteIntent = AfterIntent(remoteIntent, snapshot, intent);
    }
    private async Task AfterIntent(Task prior, SessionView snapshot, string intent)
    { await prior; await BestEffortIntent(snapshot, intent); }
    private async Task BestEffortIntent(SessionView snapshot, string intent)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var latest = await api.GetAsync(snapshot.Id, deadline.Token);
            var response = await api.IntentAsync(latest.Id, intent, latest.SessionVersion, deadline.Token);
            lock (sessionGate) { if (session?.Id == snapshot.Id) session = response; }
        }
        catch { /* Local safety already completed; remote recording is best effort. */ }
    }
    public async Task ResumeAsync(bool autonomousConsent = false)
    {
        if (source is null || session is null || State is not (LessonState.Paused or LessonState.PrivacyPaused or LessonState.NetworkInterrupted)) return;
        if (Interlocked.Exchange(ref resuming, 1) != 0) return;
        var snapshot = session;
        var epoch = Permissions.Epoch;
        try
        {
            if (mode == InteractionMode.Autonomous && !autonomousConsent) throw new SafetyException("Resume autonomous control only with fresh task consent.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Permissions.Cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(180));
            // A cancelled HTTP request or executor can finish late. Drain the old local
            // loop and observation before arming a new epoch; never silently skip resume.
            await autonomousGate.WaitAsync(deadline.Token);
            try { await turnGate.WaitAsync(deadline.Token); turnGate.Release(); }
            finally { autonomousGate.Release(); }
            Task pending; lock (sessionGate) pending = remoteIntent;
            await pending.WaitAsync(deadline.Token);
            if (epoch != Permissions.Epoch) return;
            var resumed = await SynchronizeResumeAsync(snapshot.Id, deadline.Token);
            if (epoch != Permissions.Epoch) return;
            if (mode == InteractionMode.Autonomous && (source.Kind != "window" || !activity.TrackingAvailable || !tracker.IsForegroundAndVisible(source)))
                throw new SafetyException("Focus the task window and restore the input observer before resuming autonomous control.");
            session = resumed; previous = resumed.PendingStep; latestVerification = null;
            machine.Transition(LessonState.Consent);
            if (mode == InteractionMode.Autonomous) Permissions.GrantAutonomousTaskFromLocalConsent(source.ScopeId, source.TargetId, autonomousConsent);
            else Permissions.GrantFromLocalConsent(source.ScopeId, mode, true, pointAllowed, demonstrationAllowed);
            activity.ObservationEnabled = true;
            activity.AutonomousControlActive = mode == InteractionMode.Autonomous;
            machine.Transition(LessonState.Observing); lastActivity = DateTimeOffset.UtcNow;
            epoch = Permissions.Epoch;
            await ObserveTurnAsync("resume");
            if (epoch == Permissions.Epoch && mode == InteractionMode.Autonomous) await RunAutonomousAsync();
        }
        catch (OperationCanceledException) { if (epoch == Permissions.Epoch) Notice?.Invoke("The previous analysis is still ending. Control remains paused; try Resume again."); }
        catch (Exception ex) { if (epoch == Permissions.Epoch) Notice?.Invoke(Friendly(ex)); }
        finally { if (epoch == Permissions.Epoch) SetRetry(0); Interlocked.Exchange(ref resuming, 0); }
    }
    private async Task<SessionView> SynchronizeResumeAsync(Guid id, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (attempt > 0) { SetRetry(attempt); Notice?.Invoke($"Reconnecting task · retry {attempt}/{MaximumRecoveryAttempts}..."); await Task.Delay(500 * attempt, token); }
                var latest = await api.GetAsync(id, token);
                if (latest.State == "STOPPED") throw new SafetyException("This task was stopped. Start a new task with fresh consent.");
                return await api.IntentAsync(id, "resume", latest.SessionVersion, token);
            }
            catch (Exception ex) when (IsTransient(ex) && !token.IsCancellationRequested && attempt < MaximumRecoveryAttempts) { }
        }
    }
    private static string Friendly(Exception ex) => ex is SafetyException ? ex.Message : "The service is still unavailable after recovery attempts. Input is paused. Resume will reconnect and analyse a fresh screen in the same mode.";
    public void Dispose()
    {
        disposed = true; StopLocal(); privacyTimer.Dispose(); debounce?.Cancel();
        activity.Activity -= LearnerActivity; activity.EmergencyStop -= StopLocal;
        activity.Takeover -= Takeover; activity.SessionUnavailable -= Unavailable;
        pointer.VisibilityLost -= MarkerVisibilityLost;
    }
}
