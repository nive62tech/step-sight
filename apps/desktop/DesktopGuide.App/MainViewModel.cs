using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopGuide.App.Services;
using DesktopGuide.Core;
using DesktopGuide.Platform;
using DesktopGuide.Windows;

namespace DesktopGuide.App;

public sealed record LessonChoice(string Id, string Title, string Description, string Goal);

public sealed partial class MainViewModel : ObservableObject
{
    private readonly LessonController controller;
    private readonly ILoginSession login;
    private readonly IAccountConnection connection;
    private readonly WindowsPointer pointer;
    private readonly IUserActivityObserver activity;
    private readonly Action<Action> ui;
    private CancellationTokenSource? signInCancellation;
    private CancellationTokenSource? taskCancellation;
    private readonly IBrowserLauncher? browserLauncher;
    private readonly IBrowserPlanner? browserPlanner;
    private bool ignoreStartReset;
    private TaskCompletionSource? currentOperation;
    private readonly IVoicePromptRecorder? voicePrompt;
    private CancellationTokenSource? voiceCancellation;
    private int voiceVersion;
    [ObservableProperty] private bool voiceListening;
    [ObservableProperty] private bool voiceTranscribing;
    [ObservableProperty] private string voiceStatus = "Click to speak, or type a task below.";
    public bool CanSubmitTask => !IsBusy && !VoiceListening && !VoiceTranscribing && !PhoneControlEnabled;
    public string VoiceButtonLabel => VoiceListening ? "Finish & transcribe" : VoiceTranscribing ? "Transcribing…" : "Speak a task";
    public string ModeLabel => IsAutonomous ? "Autonomous" : "Guided";
    public bool AutonomousModeSelected { get => IsAutonomous; set => SelectedTaskMode = value ? 1 : 0; }
    public event Action<CaptureSource>? GuideStarting;
    public string BrandName { get; }
    public string BuildLabel { get; }
    public string EnvironmentLabel { get; }
    public string HotkeyStatus => activity.HotkeyRegistered ? "Emergency stop  Ctrl + Alt + Shift + S" : "Use Stop in the panel or tray; the global shortcut is unavailable.";
    public ObservableCollection<CaptureSource> Sources { get; } = [];
    public LessonChoice[] LessonChoices { get; } = [
        new("sandbox", "Practice workspace", "A disposable place to learn the flow.", "Teach me to configure the practice workspace"),
        new("notepad", "Write in Notepad", "Create and save a practice note.", "Teach me to write and save a harmless practice note in Notepad"),
        new("vscode", "Explore VS Code", "Understand your editor, one visible step at a time.", "Teach me to open and edit a harmless practice file in VS Code"),
        new("railway", "Deploy with Railway", "Experimental. You handle costs and deployment.", "Teach me to deploy a disposable example on Railway. Check prerequisites and resource limits first; I will handle secrets, billing and final deployment myself.")
    ];
    public string[] Modes { get; } = ["Guide me", "Show me this action", "Let me try", "Autonomous"];
    public string[] TaskModes { get; } = ["Guided · I click", "Autonomous · agent acts"];
    [ObservableProperty] private int selectedTaskMode;
    [ObservableProperty] private bool autonomousConsent;
    public bool IsAutonomous => SelectedMode == 3;
    public bool CanChooseMode => !IsBusy && !HasLesson && !PhoneControlEnabled;
    public string TaskModeDescription => IsAutonomous
        ? "The agent clicks, scrolls and types, then checks each result using screenshots. Moving your mouse or typing pauses control."
        : "The agent shows where to click and explains each step. You control the mouse and keyboard.";
    public string TaskDisclosure => IsAutonomous
        ? "Your prompt and live screenshots go to Gemini via AWS. Control stays in the task window for up to 30 actions or 10 minutes. Login, CAPTCHA, payments and final submissions need you."
        : "Your prompt and live screenshots go to Gemini via AWS. You click and type. Stop ends observation.";
    public string ResumeConsentLabel => IsAutonomous ? "Allow fresh screenshots and autonomous control of this task window" : "Allow a fresh image of this window";
    public string CurrentWindowLabel => IsAutonomous ? "Let agent act in this window" : "Help with this window";
    public string[] DetailLevels { get; } = ["brief", "standard", "detailed"];
    [ObservableProperty] private LessonChoice? selectedLessonChoice;
    [ObservableProperty] private string selectedLesson = "browser";
    [ObservableProperty] private string selectedDetail = "standard";
    [ObservableProperty] private string responseTiming = "";
    [ObservableProperty] private CaptureSource? selectedSource;
    [ObservableProperty] private int selectedMode;
    [ObservableProperty] private string goal = "";
    [ObservableProperty] private bool observationConsent;
    [ObservableProperty] private bool previewAccepted;
    [ObservableProperty] private bool hasPreview;
    [ObservableProperty] private bool pointerConsent;
    [ObservableProperty] private bool demonstrationConsent;
    [ObservableProperty] private bool saveProgress;
    [ObservableProperty] private bool narration;
    [ObservableProperty] private bool reducedMotion;
    [ObservableProperty] private bool highContrast;
    [ObservableProperty] private bool isPinned;
    [ObservableProperty] private bool isCompact;
    [ObservableProperty] private double textSize = 15;
    [ObservableProperty] private bool rememberMe;
    [ObservableProperty] private string masks = "";
    [ObservableProperty] private string question = "";
    [ObservableProperty] private string instruction = "Your next step will appear here.";
    [ObservableProperty] private string explanation = "Choose a task and preview the window you want help with. You stay in control of each step.";
    [ObservableProperty] private string status = "Ready when you are";
    [ObservableProperty] private string notice = "";
    [ObservableProperty] private Bitmap? previewImage;
    [ObservableProperty] private ActionProposal? currentProposal;
    [ObservableProperty] private string proposalSummary = "";
    [ObservableProperty] private bool recording;
    [ObservableProperty] private string activeView = "setup";
    [ObservableProperty] private int setupStep;
    [ObservableProperty] private bool isSigningIn;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private int retryAttempt;
    public bool IsThinking => !IsPaused && (LessonState is LessonState.Observing or LessonState.Planning or LessonState.Verifying or LessonState.AnsweringQuestion || RetryAttempt > 0 || IsBusy && !HasLesson);
    public string ThinkingLabel => RetryAttempt > 0 ? $"Rechecking live screen · retry {RetryAttempt}/3" : LessonState == LessonState.Verifying ? "Checking the result…" : "Analysing the live screen…";
    [ObservableProperty] private bool isServiceReady;
    [ObservableProperty] private string connectionStatus = "Sign in to connect your lessons.";
    [ObservableProperty] private bool resumeConsent;
    [ObservableProperty] private LessonState lessonState = LessonState.Idle;
    [ObservableProperty] private CaptureSource? preparedWindow;
    [ObservableProperty] private GuideMarkerState guideMarkerState;
    public string MarkerStatus => GuideMarkerState switch
    {
        GuideMarkerState.Visible => "Guide cursor is on your task window",
        GuideMarkerState.Locating => "Finding a target in the live image…",
        GuideMarkerState.MissingTarget => "No visible click target found",
        GuideMarkerState.BrowserTargetUnconfirmed => "Browser control needs another check",
        GuideMarkerState.ScreenChanged => "Target needs a fresh image",
        GuideMarkerState.FocusRequired => "Return to your task window to see the cursor",
        GuideMarkerState.WaitingForPage => "Watching for the page to finish loading",
        _ => "Guide cursor is off"
    };
    public bool CanFindTarget => CanInteract && controller.CanLocateCurrentTarget && SelectedMode != 2 && !IsAutonomous;
    public bool HasCompactCard => HasLesson || HasNotice || IsBusy;
    public string CompactInstruction => LessonState is LessonState.Observing or LessonState.Planning or LessonState.Verifying
        ? "Reading the live screen. Your next instruction will appear here."
        : HasLesson ? Instruction : IsBusy ? "Preparing your guide…" : "Your guide needs attention";
    public bool HasPreparedWindow => PreparedWindow is not null;
    public string PreparedWindowLabel => PreparedWindow?.Label ?? "Use Ctrl + Alt + Space from the app you want help with.";
    public string CompanionHotkeyStatus => activity is ICompanionHotkey { CompanionHotkeyRegistered: true }
        ? "Summon your guide · Ctrl + Alt + Space" : "Shortcut unavailable · use this companion or the tray.";
    public string CompanionStatus => IsThinking ? ThinkingLabel : HasLesson || LessonState == LessonState.Stopped ? Status : "Ask. See. Do.";

    public bool SignedIn => login.SignedIn;
    public bool NeedsSignIn => !login.IsMock && !SignedIn;
    public bool IsMock => login.IsMock;
    public bool IsRailwayLesson => SelectedLesson == "railway";
    public bool ShowDemoPermission => SelectedMode == 1;
    public bool ShowPointerPermission => SelectedMode is 0 or 1;
    public bool HasProposal => CurrentProposal is not null && SelectedMode == 1;
    public bool ShowSetup => ActiveView == "setup";
    public bool ShowLesson => ActiveView == "lesson";
    public bool ShowSettings => ActiveView == "settings";
    public bool ShowTask => ShowSetup && SetupStep == 0;
    public bool ShowWindow => ShowSetup && SetupStep == 1;
    public bool ShowReview => ShowSetup && SetupStep == 2;
    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);
    public bool HasLesson => LessonState is not (LessonState.Idle or LessonState.Stopped);
    public bool IsPaused => LessonState is LessonState.Paused or LessonState.PrivacyPaused or LessonState.NetworkInterrupted;
    public bool CanInteract => !IsBusy && LessonState is LessonState.WaitingForLearner or LessonState.Recovering or LessonState.WaitingForApplication;
    public bool CanPoint => CanInteract && ObservationConsent && SelectedMode is 0 or 1;
    public bool CanPreview => !IsBusy && ObservationConsent && SelectedSource is not null;
    public bool CanStart => !IsBusy && SignedIn && IsServiceReady && ObservationConsent && HasPreview && PreviewAccepted
        && SelectedSource is not null && !string.IsNullOrWhiteSpace(Goal) && (SelectedMode != 1 || DemonstrationConsent && SelectedLesson == "sandbox")
        && (SelectedMode != 2 || !PointerConsent) && (!IsAutonomous || AutonomousConsent);
    public bool CanSignIn => !IsSigningIn && !IsBusy;
    public string AccountTitle => IsSigningIn ? "Finish sign-in in your browser" : IsMock ? "Development test mode" : SignedIn ? "You're signed in" : "Sign in to start learning";
    public string AccountDetail => IsSigningIn ? "Keep this app open. It will confirm when sign-in is complete." : IsMock ? "Live screen understanding is unavailable in this build." : SignedIn ? login.AccountName ?? "Your Desktop Guide account is connected." : "Use the email and password from your pilot invitation.";
    public string AccountShort => IsMock ? "Local / mock" : SignedIn ? "Signed in" : IsSigningIn ? "Signing in…" : "Signed out";
    public string PageTitle => ShowSettings ? "Make it yours." : ShowLesson ? "One step at a time." : SetupStep == 1 ? "Choose your window." : SetupStep == 2 ? "Take a look first." : "Learn by doing.";
    public string PageDescription => ShowSettings ? "Your account, comfort and privacy preferences." : ShowLesson ? TaskModeDescription : SetupStep == 1 ? "Pick the app you want help with. Preview it before sharing." : SetupStep == 2 ? "Confirm exactly what your guide can see and do." : "Describe your task and choose whether to follow along or let the agent act.";
    public string SetupProgress => SetupStep == 0 ? "Prompt → open browser → live guidance" : "Optional · existing window setup";
    public string ContinueLabel => IsBusy ? "Working on your task…" : IsAutonomous ? (NeedsSignIn ? "Sign in & let agent act" : "Start autonomous task") : NeedsSignIn ? "Sign in & open my browser" : "Open browser & guide me";
    public string SourceSummary => SelectedSource?.Label ?? "No window selected";
    public string LessonTitle => SelectedLessonChoice?.Title ?? "Your lesson";
    public string ModeHelp => IsAutonomous ? TaskModeDescription : SelectedMode == 2 ? "You try independently. The guide observes and gives feedback; it won't move your pointer." : SelectedMode == 1 ? "Practice sandbox only. Each exact action needs your separate approval." : "The guide explains. You click and type. Pointer guidance is optional.";
    public string StartHint => NeedsSignIn ? "Sign in before starting a cloud lesson." : !IsServiceReady ? "Check the service connection before starting." : !HasPreview ? "Preview your selected window first." : !PreviewAccepted ? "Review the preview and accept its scope to continue." : IsAutonomous && !AutonomousConsent ? "Allow control of this task window before starting autonomous mode." : SelectedMode == 1 && SelectedLesson != "sandbox" ? "Choose Guide me or Let me try for this lesson." : SelectedMode == 1 && !DemonstrationConsent ? "Allow separate one-action approvals to use demonstrations." : "Ready. Your first step will appear after the window is observed.";
    public string ObservationSummary => LessonState is LessonState.Idle or LessonState.Stopped or LessonState.Completed || IsPaused ? "Observation off" : "Live · observing your task window";
    public string RecordLabel => Recording ? "Finish recording" : "Speak a question";

    public MainViewModel(LessonController controller, ILoginSession login, PublicConfiguration config, WindowsPointer pointer,
        IUserActivityObserver activity, IAccountConnection connection, Action<Action>? dispatch = null, IBrowserLauncher? browserLauncher = null, IBrowserPlanner? browserPlanner = null, IVoicePromptRecorder? voicePrompt = null)
    {
        (this.controller, this.login, this.pointer, this.activity, this.connection) = (controller, login, pointer, activity, connection);
        this.browserLauncher = browserLauncher;
        this.browserPlanner = browserPlanner;
        this.voicePrompt = voicePrompt; ModeTheme.Apply(false);
        ui = dispatch ?? (action => { if (Dispatcher.UIThread.CheckAccess()) action(); else Dispatcher.UIThread.Post(action); });
        BrandName = config.BrandName; BuildLabel = config.BuildLabel;
        EnvironmentLabel = config.AuthMode == "mock" ? "LOCAL / MOCK · no live screen understanding" : "Cloud pilot";
        RefreshSources();
        login.Changed += () => ui(() => { if (!SignedIn) DisablePhoneControl(); RefreshUi(); });
        activity.EmergencyStop += () => ui(Stop);
        activity.SessionUnavailable += () => ui(() => { DisablePhoneControl(); taskCancellation?.Cancel(); CancelVoicePrompt(); });
        controller.Presentation += (text, detail, state) => ui(() => { Instruction = text; Explanation = detail; Status = state; });
        controller.Notice += message => ui(() => Notice = message);
        controller.MarkerStateChanged += value => ui(() => GuideMarkerState = value);
        controller.Preview += frame => ui(() =>
        {
            if (!ObservationConsent || SelectedSource?.ScopeId != frame.Observation.ScopeId) return;
            PreviewImage?.Dispose(); PreviewImage = new Bitmap(new MemoryStream(frame.Png)); HasPreview = true;
        });
        controller.StateChanged += state =>
        {
            var internalReset = state == LessonState.Stopped && ignoreStartReset;
            if (state == LessonState.Consent) ignoreStartReset = false;
            ui(() =>
            {
                if (state != controller.State) return; // Ignore queued state from a cancelled operation.
                LessonState = state; Status = StatusText(state);
                if (state is LessonState.Stopped or LessonState.Paused or LessonState.PrivacyPaused or LessonState.NetworkInterrupted) { Recording = false; ResumeConsent = false; }
                if (state == LessonState.Stopped && !internalReset) { taskCancellation?.Cancel(); ClearConsent(); }
            });
        };
        controller.RetryChanged += attempt => ui(() => { RetryAttempt = controller.RetryAttempt; RefreshUi(); });
        controller.ProposalChanged += proposal => ui(() => { CurrentProposal = proposal; ProposalSummary = proposal is null ? "" : $"{proposal.Action.Kind}: {proposal.Effect}"; });
        controller.TimingsRecorded += timings => ui(() => ResponseTiming = $"Screen: {timings["capture_preprocess"]:F0} ms · Guidance: {timings["roundtrip"] / 1000:F1} s");
    }
    private void RefreshUi()
    {
        OnPropertyChanged(nameof(CanEnablePhoneControl));
        foreach (var name in new[] { nameof(SignedIn), nameof(NeedsSignIn), nameof(AccountTitle), nameof(AccountDetail), nameof(AccountShort), nameof(ContinueLabel), nameof(CanSignIn),
            nameof(ShowSetup), nameof(ShowLesson), nameof(ShowSettings), nameof(ShowTask), nameof(ShowWindow), nameof(ShowReview), nameof(PageTitle), nameof(PageDescription), nameof(SetupProgress),
            nameof(CanPreview), nameof(CanStart), nameof(StartHint), nameof(HasNotice), nameof(SourceSummary), nameof(LessonTitle), nameof(ModeHelp), nameof(HasLesson),
            nameof(IsPaused), nameof(CanInteract), nameof(CanPoint), nameof(ObservationSummary), nameof(IsRailwayLesson), nameof(ShowDemoPermission), nameof(ShowPointerPermission), nameof(HasProposal), nameof(HotkeyStatus), nameof(CompanionStatus), nameof(CanFindTarget), nameof(HasCompactCard), nameof(CompactInstruction),
            nameof(IsAutonomous), nameof(CanChooseMode), nameof(TaskModeDescription), nameof(TaskDisclosure), nameof(ResumeConsentLabel), nameof(CurrentWindowLabel), nameof(ModeLabel), nameof(AutonomousModeSelected), nameof(CanSubmitTask), nameof(VoiceButtonLabel), nameof(IsThinking), nameof(ThinkingLabel) }) OnPropertyChanged(name);
    }
    private string StatusText(LessonState state) => state switch
    {
        LessonState.Observing => "Looking at your window…", LessonState.Planning => "Preparing your next step…", LessonState.Verifying => "Checking what changed…",
        LessonState.WaitingForLearner => IsAutonomous ? "Agent preparing action" : "Your turn", LessonState.PrivacyPaused => "Paused for privacy", LessonState.Paused => IsAutonomous ? "Agent paused" : "Lesson paused",
        LessonState.WaitingForApplication => "Waiting for the page", LessonState.Recovering => "Guidance needs attention",
        LessonState.Acting => "Agent acting · move the mouse to pause",
        LessonState.Stopped => "Stopped · permissions cleared", LessonState.NetworkInterrupted => "Connection interrupted", LessonState.Completed => "Lesson complete", _ => "Getting ready…"
    };
    partial void OnActiveViewChanged(string value) => RefreshUi();
    partial void OnSetupStepChanged(int value) => RefreshUi();
    partial void OnIsSigningInChanged(bool value) => RefreshUi();
    partial void OnIsBusyChanged(bool value) => RefreshUi();
    partial void OnIsServiceReadyChanged(bool value) => RefreshUi();
    partial void OnNoticeChanged(string value) => RefreshUi();
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(CompanionStatus));
    partial void OnGuideMarkerStateChanged(GuideMarkerState value) { OnPropertyChanged(nameof(MarkerStatus)); OnPropertyChanged(nameof(CanFindTarget)); }
    partial void OnInstructionChanged(string value) => OnPropertyChanged(nameof(CompactInstruction));
    partial void OnLessonStateChanged(LessonState value) => RefreshUi();
    partial void OnCurrentProposalChanged(ActionProposal? value) => RefreshUi();
    partial void OnGoalChanged(string value) { AutonomousConsent = false; RefreshUi(); }
    partial void OnPreviewAcceptedChanged(bool value) => RefreshUi();
    partial void OnHasPreviewChanged(bool value) => RefreshUi();
    partial void OnPointerConsentChanged(bool value) => RefreshUi();
    partial void OnDemonstrationConsentChanged(bool value) => RefreshUi();
    partial void OnObservationConsentChanged(bool value) { if (!value) { HasPreview = false; PreviewAccepted = false; controller.StopLocal(); } RefreshUi(); }
    partial void OnSelectedSourceChanged(CaptureSource? value) { InvalidatePreview(); RefreshUi(); }
    partial void OnMasksChanged(string value) => InvalidatePreview();
    partial void OnSelectedLessonChoiceChanged(LessonChoice? value)
    {
        if (value is null) return; SelectedLesson = value.Id; Goal = value.Goal;
        if (value.Id != "sandbox" && SelectedMode == 1) SelectedMode = 0; RefreshUi();
    }
    private bool restoringTaskMode;
    partial void OnSelectedModeChanged(int value)
    {
        ModeTheme.Apply(value == 3);
        if (!restoringTaskMode)
        {
            CancelVoicePrompt();
            if (HasLesson) Stop();
            AutonomousConsent = false;
            if (value is 2 or 3) { PointerConsent = false; DemonstrationConsent = false; }
        }
        // Keep the advanced practice modes when syncing the two-choice task selector.
        selectedTaskMode = value == 3 ? 1 : 0; OnPropertyChanged(nameof(SelectedTaskMode)); RefreshUi();
    }
    partial void OnSelectedTaskModeChanged(int value) { SelectedMode = value == 1 ? 3 : 0; }
    partial void OnAutonomousConsentChanged(bool value) => RefreshUi();
    partial void OnVoiceListeningChanged(bool value) => RefreshUi();
    partial void OnVoiceTranscribingChanged(bool value) => RefreshUi();
    partial void OnNarrationChanged(bool value) => controller.NarrationEnabled = value;
    partial void OnReducedMotionChanged(bool value) => pointer.ReducedMotion = value;
    partial void OnRememberMeChanged(bool value) => login.RememberMe = value;
    partial void OnRecordingChanged(bool value) => OnPropertyChanged(nameof(RecordLabel));
    partial void OnPreparedWindowChanged(CaptureSource? value) { OnPropertyChanged(nameof(HasPreparedWindow)); OnPropertyChanged(nameof(PreparedWindowLabel)); }
    public void PrepareCurrentWindow(CaptureSource? source)
    {
        if (HasLesson || IsBusy) return;
        PreparedWindow = source;
        if (source is not null) Goal = "Help me understand what to do in this window.";
    }
    private void InvalidatePreview() { HasPreview = false; PreviewAccepted = false; if (ObservationConsent) controller.StopLocal(); }
    private void ClearConsent() { ObservationConsent = false; PointerConsent = false; DemonstrationConsent = false; AutonomousConsent = false; PreviewAccepted = false; HasPreview = false; ResumeConsent = false; }
    private IReadOnlyList<Bounds> ReadMasks()
    {
        if (string.IsNullOrWhiteSpace(Masks)) return [];
        try
        {
            var result = Masks.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(text =>
            {
                var values = text.Split(',').Select(v => double.Parse(v.Trim(), CultureInfo.InvariantCulture)).ToArray();
                if (values.Length != 4) throw new SafetyException("Each mask needs four values: left, top, right, bottom (between 0 and 1).");
                var bounds = new Bounds(values[0], values[1], values[2], values[3]); bounds.Validate(); return bounds;
            }).ToArray();
            if (result.Length > 10) throw new SafetyException("Use no more than ten privacy masks."); return result;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException) { throw new SafetyException("Use numbers between 0 and 1 for privacy masks, separated by commas."); }
    }
    private async Task Run(Func<Task> action, bool waitForIdle = false)
    {
        if (IsBusy)
        {
            if (!waitForIdle || currentOperation is null) return;
            var epoch = controller.Permissions.Epoch;
            Notice = "Finishing the previous analysis, then resuming from a fresh image...";
            try { await currentOperation.Task.WaitAsync(controller.Permissions.Cancellation); }
            catch (OperationCanceledException) { return; }
            if (epoch != controller.Permissions.Epoch || IsBusy) return;
        }
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        currentOperation = operation; IsBusy = true; Notice = "";
        try { await action(); }
        catch (OperationCanceledException) { if (currentOperation == operation) Notice = "Cancelled. You can try again when you're ready."; }
        catch (SignInException ex) { if (currentOperation == operation) Notice = ex.Message; }
        catch (SafetyException ex) { if (currentOperation == operation) Notice = ex.Message; }
        catch (HttpRequestException) { if (currentOperation == operation) Notice = "Couldn't reach the service. Check your connection, then retry."; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { if (currentOperation == operation) Notice = "This step couldn't be completed. Check your selected window and permissions, then try again."; }
        finally { if (currentOperation == operation) { IsBusy = false; currentOperation = null; RefreshUi(); } operation.TrySetResult(); }
    }
    [RelayCommand] private void DismissNotice() => Notice = "";
    [RelayCommand] private void RefreshSources()
    {
        if (controller.State is not (LessonState.Idle or LessonState.Stopped)) { Notice = "Stop this lesson before choosing a different window."; return; }
        var selected = SelectedSource?.TargetId; Sources.Clear(); PreparedWindow = null;
        try { foreach (var source in controller.Sources()) Sources.Add(source); SelectedSource = Sources.FirstOrDefault(s => s.TargetId == selected); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Notice = "Windows couldn't list your open apps. Open the app you want help with, then refresh."; }
    }
    [RelayCommand(AllowConcurrentExecutions = true)] private Task ContinueSetup() => Run(async () =>
    {
        if (VoiceListening || VoiceTranscribing) throw new SafetyException("Finish or cancel voice input before starting.");
        var prompt = Goal;
        var autonomous = IsAutonomous;
        if (autonomous && !AutonomousConsent) throw new SafetyException("Allow autonomous control for this task before starting.");
        if (string.IsNullOrWhiteSpace(prompt)) throw new SafetyException("Describe what you want to do first.");
        if (prompt.Length > 1000) throw new SafetyException("Keep your task description under 1,000 characters.");
        if (IsMock) throw new SafetyException("Live guidance needs the connected cloud build. No offline walkthrough is available.");
        if (browserLauncher is null) throw new SafetyException("Browser launch is unavailable in this build.");
        if (browserPlanner is null) throw new SafetyException("Live intent planning is unavailable in this build.");
        StopTask(false);
        using var task = new CancellationTokenSource(); taskCancellation = task;
        try
        {
            if (!SignedIn)
            {
                IsSigningIn = true; ConnectionStatus = "Finish sign-in in your browser…";
                try { await login.SignInAsync(task.Token); } finally { IsSigningIn = false; }
            }
            task.Token.ThrowIfCancellationRequested(); await CheckConnectionCore(task.Token);
            task.Token.ThrowIfCancellationRequested(); Status = "Understanding your task…"; Notice = "The agent is choosing where to begin. Screen sharing has not started.";
            var decision = await browserPlanner.PlanAsync(prompt, task.Token);
            task.Token.ThrowIfCancellationRequested();
            var plan = BrowserLaunch.FromPlan(decision);
            Status = $"Opening {plan.Browser}…"; Notice = decision.Message + $" ({plan.Address.Host})";
            var launched = await browserLauncher.OpenAsync(plan, task.Token);
            task.Token.ThrowIfCancellationRequested();
            SelectedSource = launched; SelectedLesson = plan.LessonId;
            ObservationConsent = true; controller.Masks = ReadMasks();
            await controller.PreviewAsync(launched); task.Token.ThrowIfCancellationRequested();
            PreviewAccepted = true; ActiveView = "lesson"; controller.DetailLevel = SelectedDetail;
            Notice = "";
            GuideStarting?.Invoke(launched);
            ignoreStartReset = true;
            await controller.StartAsync(launched, prompt, plan.LessonId, autonomous ? InteractionMode.Autonomous : InteractionMode.Guide, false, false, SaveProgress, autonomous);
        }
        catch { if (taskCancellation == task) { controller.StopLocal(); ClearConsent(); } throw; }
        finally { if (taskCancellation == task) { ignoreStartReset = false; taskCancellation = null; } }
    });
    [RelayCommand] private void ChooseExistingWindow() { Stop(); RefreshSources(); SelectedLesson = "browser"; SetupStep = 1; ActiveView = "setup"; }
    [RelayCommand(AllowConcurrentExecutions = true)] private Task HelpCurrentWindow() => Run(async () =>
    {
        if (VoiceListening || VoiceTranscribing) throw new SafetyException("Finish or cancel voice input before starting.");
        var selected = PreparedWindow ?? throw new SafetyException("Press Ctrl + Alt + Space while your task window is in front.");
        var prompt = Goal;
        var autonomous = IsAutonomous;
        if (autonomous && !AutonomousConsent) throw new SafetyException("Allow autonomous control for this task before starting.");
        if (string.IsNullOrWhiteSpace(prompt)) throw new SafetyException("Describe what you want to do in this window.");
        if (IsMock) throw new SafetyException("Live screen help needs the connected cloud build.");
        StopTask(false); using var task = new CancellationTokenSource(); taskCancellation = task;
        try
        {
            if (!SignedIn)
            {
                IsSigningIn = true;
                try { await login.SignInAsync(task.Token); } finally { IsSigningIn = false; }
            }
            task.Token.ThrowIfCancellationRequested(); await CheckConnectionCore(task.Token); task.Token.ThrowIfCancellationRequested();
            SelectedSource = selected; SelectedLesson = "browser";
            ObservationConsent = true; controller.Masks = ReadMasks();
            await controller.PreviewAsync(selected); task.Token.ThrowIfCancellationRequested();
            PreviewAccepted = true; ActiveView = "lesson"; controller.DetailLevel = SelectedDetail; ignoreStartReset = true;
            GuideStarting?.Invoke(selected);
            await controller.StartAsync(selected, prompt, "browser", autonomous ? InteractionMode.Autonomous : InteractionMode.Guide, false, false, SaveProgress, autonomous);
        }
        catch { if (taskCancellation == task) { controller.StopLocal(); ClearConsent(); } throw; }
        finally { if (taskCancellation == task) { ignoreStartReset = false; taskCancellation = null; } }
    });
    [RelayCommand] private void BackSetup() { if (SetupStep > 0) SetupStep--; }
    [RelayCommand] private void NewLesson() { Stop(); SetupStep = 0; ActiveView = "setup"; Notice = ""; RefreshSources(); }
    [RelayCommand] private void OpenLesson() { if (HasLesson) ActiveView = "lesson"; }
    [RelayCommand] private void OpenSettings() { if (HasLesson) controller.PauseLocal(); ActiveView = "settings"; }
    [RelayCommand] private void ChooseRailway() { NewLesson(); SelectedLessonChoice = LessonChoices.Single(l => l.Id == "railway"); }
    [RelayCommand] private Task Preview() => Run(async () =>
    {
        if (!ObservationConsent || SelectedSource is null) throw new SafetyException("Select a window and allow its local preview first.");
        HasPreview = false; PreviewAccepted = false; controller.Masks = ReadMasks(); await controller.PreviewAsync(SelectedSource);
        if (HasPreview) SetupStep = 2;
    });
    [RelayCommand] private Task Start() => Run(async () =>
    {
        if (VoiceListening || VoiceTranscribing) throw new SafetyException("Finish or cancel voice input before starting.");
        if (!SignedIn) throw new SignInException("Sign in before starting a cloud lesson.");
        if (!IsServiceReady) await CheckConnectionCore();
        if (!ObservationConsent || !HasPreview || !PreviewAccepted || SelectedSource is null || string.IsNullOrWhiteSpace(Goal))
            throw new SafetyException("Preview your selected window and accept its scope before starting.");
        if (SelectedMode == 1 && (!DemonstrationConsent || SelectedLesson != "sandbox")) throw new SafetyException("Demonstrations need separate permission and a practice sandbox lesson.");
        if (SelectedMode == 2 && PointerConsent) throw new SafetyException("Let me try doesn't move the pointer. Turn off pointer guidance first.");
        if (IsAutonomous && !AutonomousConsent) throw new SafetyException("Allow autonomous control for this task before starting.");
        if (IsMock) throw new SafetyException("Connect the live cloud build to analyse your screen.");
        ActiveView = "lesson"; controller.DetailLevel = SelectedDetail; ignoreStartReset = true;
        GuideStarting?.Invoke(SelectedSource);
        try { await controller.StartAsync(SelectedSource, Goal, SelectedLesson, (InteractionMode)SelectedMode, PointerConsent, DemonstrationConsent, SaveProgress, AutonomousConsent); }
        finally { ignoreStartReset = false; }
    });
    private void StopTask(bool releaseOperation = true)
    {
        CancelVoicePrompt(); taskCancellation?.Cancel(); taskCancellation = null; signInCancellation?.Cancel();
        ignoreStartReset = !releaseOperation;
        controller.StopLocal(); ClearConsent(); ignoreStartReset = false;
        if (releaseOperation) { var old = currentOperation; currentOperation = null; IsBusy = false; old?.TrySetResult(); }
        Notice = ""; Status = "Stopped · permissions cleared"; RefreshUi();
    }
    [RelayCommand] private void Stop() { DisablePhoneControl(); StopTask(); }
    [RelayCommand] private void Pause() => controller.PauseLocal();
    [RelayCommand(AllowConcurrentExecutions = true)] private Task Resume() => Run(async () =>
    {
        if (!ResumeConsent) throw new SafetyException("Allow a fresh observation before resuming with the same selected window and lesson permissions.");
        // A manual handoff is a step state, never a mode change. Keep the original task mode.
        restoringTaskMode = true;
        try { SelectedMode = (int)controller.Mode; }
        finally { restoringTaskMode = false; }
        GuideStarting?.Invoke(SelectedSource!);
        await controller.ResumeAsync(IsAutonomous && ResumeConsent); ResumeConsent = false;
    }, waitForIdle: true);
    [RelayCommand] private Task ShowWhere() => Run(controller.ShowWhereAsync);
    [RelayCommand] private Task FindTarget() => Run(() => controller.ObserveTurnAsync("locate"));
    [RelayCommand] private Task DidThis() => Run(() => controller.ObserveTurnAsync("i_did_this"));
    [RelayCommand] private Task Reobserve() => Run(() => controller.ObserveTurnAsync("reobserve"));
    [RelayCommand] private Task ExplainMore() => Run(() => controller.AskAsync("Explain why this step matters and what its terms mean."));
    [RelayCommand] private Task Ask() => Run(async () => { if (!string.IsNullOrWhiteSpace(Question)) { await controller.AskAsync(Question); Question = ""; } });
    [RelayCommand] private async Task SignIn()
    {
        if (!CanSignIn) return; Stop(); IsSigningIn = true; Notice = ""; ConnectionStatus = "Waiting for browser sign-in…";
        signInCancellation = new CancellationTokenSource();
        try
        {
            await login.SignInAsync(signInCancellation.Token);
            ui(() => { ActiveView = "setup"; SetupStep = 0; Status = "Signed in · ready for your prompt"; Notice = "You're signed in. Describe your task, then choose Open browser & guide me."; RefreshUi(); });
            await CheckConnectionCore(signInCancellation.Token);
        }
        catch (OperationCanceledException) { Notice = "Sign-in cancelled. Click Sign in whenever you're ready."; ConnectionStatus = "Sign-in cancelled."; }
        catch (SignInException ex) { Notice = ex.Message; ConnectionStatus = SignedIn ? "Lesson connection needs attention." : "Not connected."; }
        catch (SafetyException ex) { Notice = ex.Message; ConnectionStatus = "Sign-in couldn't be verified."; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Notice = SignedIn ? "You're signed in, but the lesson service couldn't be reached. Retry the connection." : "Couldn't complete sign-in. Check your connection and close any other Desktop Guide instances, then try again."; ConnectionStatus = "Connection needs attention."; }
        finally { signInCancellation.Dispose(); signInCancellation = null; IsSigningIn = false; RefreshUi(); }
    }
    [RelayCommand] private void CancelSignIn() { signInCancellation?.Cancel(); taskCancellation?.Cancel(); }
    [RelayCommand] private Task SignOut() => Run(async () => { Stop(); await login.SignOutAsync(); IsServiceReady = false; ConnectionStatus = "Sign in to connect your lessons."; ActiveView = "setup"; SetupStep = 0; Notice = "You're signed out. Observation and control are off."; });
    private async Task CheckConnectionCore(CancellationToken cancellation = default)
    {
        IsServiceReady = false; ConnectionStatus = "Checking lesson access…";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20)); await connection.CheckAsync(timeout.Token);
        timeout.Token.ThrowIfCancellationRequested();
        IsServiceReady = true; ConnectionStatus = IsMock ? "Local service connected · mock guidance only" : "Connected · lessons ready";
    }
    [RelayCommand] private Task CheckConnection() => Run(() => CheckConnectionCore());
    [RelayCommand] private Task ForgetProgress() => Run(async () => { await controller.ForgetProgressAsync(); Notice = "Local progress and the current cloud lesson were deleted."; });
    [RelayCommand] private void Demonstration()
    {
        if (CurrentProposal is null) { Notice = "There isn't a current action to review. Continue manually or check the screen again."; return; }
        var approved = CurrentProposal; var dialog = new ApprovalWindow(approved, () => Run(() => controller.DemonstrateAsync(approved)));
        dialog.Opened += (_, _) => { var handle = dialog.TryGetPlatformHandle()?.Handle ?? 0; if (handle != 0) activity.IgnoreWindow(handle); }; dialog.Show();
    }
    [RelayCommand] private Task PushToTalk() => Run(async () =>
    {
        if (!Recording) { controller.BeginQuestionRecording(); Recording = true; Notice = "Listening. Click Finish recording to send your question (maximum 30 seconds)."; }
        else { Recording = false; await controller.EndQuestionRecordingAsync(); }
    });

    [RelayCommand] private async Task TogglePromptVoice()
    {
        if (VoiceListening) { await FinishVoicePromptAsync(voiceVersion); return; }
        if (VoiceTranscribing) return;
        if (HasLesson || IsBusy) { PreparedWindow = SelectedSource; Stop(); }
        ActiveView = "setup"; SetupStep = 0;
        if (NeedsSignIn || IsMock) { Notice = "Sign in to use voice with the cloud agent. You can type your task here."; return; }
        if (voicePrompt is null) { Notice = "Voice input is unavailable. Type your task below."; return; }
        CancelVoicePrompt();
        voiceCancellation = new CancellationTokenSource(); var version = voiceVersion;
        try
        {
            voicePrompt.Start(); VoiceListening = true;
            VoiceStatus = "Listening · click Finish when ready. Up to 30 seconds."; Notice = "";
            _ = FinishVoiceAfterLimitAsync(version, voiceCancellation.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { CancelVoicePrompt(); Notice = "Microphone unavailable. Check Windows microphone access, or type your task."; }
        await Task.CompletedTask;
    }
    private async Task FinishVoiceAfterLimitAsync(int version, CancellationToken cancellation)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), cancellation); ui(() => { if (version == voiceVersion && VoiceListening) _ = FinishVoicePromptAsync(version); }); }
        catch (OperationCanceledException) { }
    }
    private async Task FinishVoicePromptAsync(int version)
    {
        if (version != voiceVersion || !VoiceListening || voicePrompt is null || voiceCancellation is null) return;
        VoiceListening = false; VoiceTranscribing = true; VoiceStatus = "Turning your speech into a task…";
        var cancellation = voiceCancellation.Token;
        try
        {
            var transcript = await voicePrompt.FinishAsync(cancellation);
            if (version != voiceVersion || cancellation.IsCancellationRequested) return;
            Goal = transcript; VoiceStatus = "Review your words, then start when ready.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (version == voiceVersion) { VoiceStatus = "Voice input could not finish. Try again or type below."; Notice = ex is SafetyException ? ex.Message : "Voice service unavailable. Your task has not started."; } }
        finally { if (version == voiceVersion) { VoiceTranscribing = false; voiceCancellation?.Cancel(); voiceCancellation?.Dispose(); voiceCancellation = null; } }
    }
    [RelayCommand] private void TypePrompt()
    {
        if (HasLesson || IsBusy) { PreparedWindow = SelectedSource; Stop(); }
        CancelVoicePrompt(); ActiveView = "setup"; SetupStep = 0;
    }
    [RelayCommand] private void CancelVoicePrompt()
    {
        voiceVersion++; voiceCancellation?.Cancel(); voiceCancellation?.Dispose(); voiceCancellation = null;
        if (VoiceListening || VoiceTranscribing) voicePrompt?.Cancel();
        VoiceListening = VoiceTranscribing = false; VoiceStatus = "Click to speak, or type a task below.";
    }
}
