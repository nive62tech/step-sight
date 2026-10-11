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
    public string[] Modes { get; } = ["Guide me", "Show me this action", "Let me try"];
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
    public bool CanFindTarget => CanInteract && controller.CanLocateCurrentTarget && SelectedMode != 2;
    public bool HasCompactCard => HasLesson || HasNotice || IsBusy;
    public string CompactInstruction => LessonState is LessonState.Observing or LessonState.Planning or LessonState.Verifying
        ? "Reading the live screen. Your next instruction will appear here."
        : HasLesson ? Instruction : IsBusy ? "Preparing your guide…" : "Your guide needs attention";
    public bool HasPreparedWindow => PreparedWindow is not null;
    public string PreparedWindowLabel => PreparedWindow?.Label ?? "Use Ctrl + Alt + Space from the app you want help with.";
    public string CompanionHotkeyStatus => activity is ICompanionHotkey { CompanionHotkeyRegistered: true }
        ? "Summon your guide · Ctrl + Alt + Space" : "Shortcut unavailable · use this companion or the tray.";
    public string CompanionStatus => IsBusy && !HasLesson ? "Preparing your guide…" : HasLesson ? Status : "Ask. See. Do.";

    public bool SignedIn => login.SignedIn;
    public bool NeedsSignIn => !login.IsMock && !SignedIn;
    public bool IsMock => login.IsMock;
    public bool IsRailwayLesson => SelectedLesson == "railway";
    public bool ShowDemoPermission => SelectedMode == 1;
    public bool ShowPointerPermission => SelectedMode != 2;
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
    public bool CanPoint => CanInteract && ObservationConsent && SelectedMode != 2;
    public bool CanPreview => !IsBusy && ObservationConsent && SelectedSource is not null;
    public bool CanStart => !IsBusy && SignedIn && IsServiceReady && ObservationConsent && HasPreview && PreviewAccepted
        && SelectedSource is not null && !string.IsNullOrWhiteSpace(Goal) && (SelectedMode != 1 || DemonstrationConsent && SelectedLesson == "sandbox")
        && (SelectedMode != 2 || !PointerConsent);
    public bool CanSignIn => !IsSigningIn && !IsBusy;
    public string AccountTitle => IsSigningIn ? "Finish sign-in in your browser" : IsMock ? "Development test mode" : SignedIn ? "You're signed in" : "Sign in to start learning";
    public string AccountDetail => IsSigningIn ? "Keep this app open. It will confirm when sign-in is complete." : IsMock ? "Live screen understanding is unavailable in this build." : SignedIn ? login.AccountName ?? "Your Desktop Guide account is connected." : "Use the email and password from your pilot invitation.";
    public string AccountShort => IsMock ? "Local / mock" : SignedIn ? "Signed in" : IsSigningIn ? "Signing in…" : "Signed out";
    public string PageTitle => ShowSettings ? "Make it yours." : ShowLesson ? "One step at a time." : SetupStep == 1 ? "Choose your window." : SetupStep == 2 ? "Take a look first." : "Learn by doing.";
    public string PageDescription => ShowSettings ? "Your account, comfort and privacy preferences." : ShowLesson ? "Each step is chosen from your live screen. You click and type." : SetupStep == 1 ? "Pick the app you want help with. Preview it before sharing." : SetupStep == 2 ? "Confirm exactly what your guide can see and do." : "Describe your task. I'll open the browser and guide you from what I can see.";
    public string SetupProgress => SetupStep == 0 ? "Prompt → open browser → live guidance" : "Optional · existing window setup";
    public string ContinueLabel => IsBusy ? "Preparing your live guide…" : NeedsSignIn ? "Sign in & open my browser" : "Open browser & guide me";
    public string SourceSummary => SelectedSource?.Label ?? "No window selected";
    public string LessonTitle => SelectedLessonChoice?.Title ?? "Your lesson";
    public string ModeHelp => SelectedMode == 2 ? "You try independently. The guide observes and gives feedback; it won't move your pointer." : SelectedMode == 1 ? "Practice sandbox only. Each exact action needs your separate approval." : "The guide explains. You click and type. Pointer guidance is optional.";
    public string StartHint => NeedsSignIn ? "Sign in before starting a cloud lesson." : !IsServiceReady ? "Check the service connection before starting." : !HasPreview ? "Preview your selected window first." : !PreviewAccepted ? "Review the preview and accept its scope to continue." : SelectedMode == 1 && SelectedLesson != "sandbox" ? "Choose Guide me or Let me try for this lesson." : SelectedMode == 1 && !DemonstrationConsent ? "Allow separate one-action approvals to use demonstrations." : "Ready. Your first step will appear after the window is observed.";
    public string ObservationSummary => LessonState is LessonState.Idle or LessonState.Stopped or LessonState.Completed || IsPaused ? "Observation off" : "Live · observing your task window";
    public string RecordLabel => Recording ? "Finish recording" : "Speak a question";

    public MainViewModel(LessonController controller, ILoginSession login, PublicConfiguration config, WindowsPointer pointer,
        IUserActivityObserver activity, IAccountConnection connection, Action<Action>? dispatch = null, IBrowserLauncher? browserLauncher = null, IBrowserPlanner? browserPlanner = null)
    {
        (this.controller, this.login, this.pointer, this.activity, this.connection) = (controller, login, pointer, activity, connection);
        this.browserLauncher = browserLauncher;
        this.browserPlanner = browserPlanner;
        ui = dispatch ?? (action => { if (Dispatcher.UIThread.CheckAccess()) action(); else Dispatcher.UIThread.Post(action); });
        BrandName = config.BrandName; BuildLabel = config.BuildLabel;
        EnvironmentLabel = config.AuthMode == "mock" ? "LOCAL / MOCK · no live screen understanding" : "Cloud pilot";
        RefreshSources();
        login.Changed += () => ui(RefreshUi);
        activity.EmergencyStop += () => ui(Stop);
        activity.SessionUnavailable += () => ui(() => { taskCancellation?.Cancel(); });
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
                LessonState = state; Status = StatusText(state);
                if (state is LessonState.Stopped or LessonState.Paused or LessonState.PrivacyPaused or LessonState.NetworkInterrupted) { Recording = false; ResumeConsent = false; }
                if (state == LessonState.Stopped && !internalReset) { taskCancellation?.Cancel(); ClearConsent(); }
            });
        };
        controller.ProposalChanged += proposal => ui(() => { CurrentProposal = proposal; ProposalSummary = proposal is null ? "" : $"{proposal.Action.Kind}: {proposal.Effect}"; });
        controller.TimingsRecorded += timings => ui(() => ResponseTiming = $"Screen: {timings["capture_preprocess"]:F0} ms · Guidance: {timings["roundtrip"] / 1000:F1} s");
    }
    private void RefreshUi()
    {
        foreach (var name in new[] { nameof(SignedIn), nameof(NeedsSignIn), nameof(AccountTitle), nameof(AccountDetail), nameof(AccountShort), nameof(ContinueLabel), nameof(CanSignIn),
            nameof(ShowSetup), nameof(ShowLesson), nameof(ShowSettings), nameof(ShowTask), nameof(ShowWindow), nameof(ShowReview), nameof(PageTitle), nameof(PageDescription), nameof(SetupProgress),
            nameof(CanPreview), nameof(CanStart), nameof(StartHint), nameof(HasNotice), nameof(SourceSummary), nameof(LessonTitle), nameof(ModeHelp), nameof(HasLesson),
            nameof(IsPaused), nameof(CanInteract), nameof(CanPoint), nameof(ObservationSummary), nameof(IsRailwayLesson), nameof(ShowDemoPermission), nameof(ShowPointerPermission), nameof(HasProposal), nameof(HotkeyStatus), nameof(CompanionStatus), nameof(CanFindTarget), nameof(HasCompactCard), nameof(CompactInstruction) }) OnPropertyChanged(name);
    }
    private static string StatusText(LessonState state) => state switch
    {
        LessonState.Observing => "Looking at your window…", LessonState.Planning => "Preparing your next step…", LessonState.Verifying => "Checking what changed…",
        LessonState.WaitingForLearner => "Your turn", LessonState.PrivacyPaused => "Paused for privacy", LessonState.Paused => "Lesson paused",
        LessonState.WaitingForApplication => "Waiting for the page", LessonState.Recovering => "Guidance needs attention",
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
    partial void OnGoalChanged(string value) => RefreshUi();
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
    partial void OnSelectedModeChanged(int value) { if (value == 2) { PointerConsent = false; DemonstrationConsent = false; } RefreshUi(); }
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
    private void ClearConsent() { ObservationConsent = false; PointerConsent = false; DemonstrationConsent = false; PreviewAccepted = false; HasPreview = false; ResumeConsent = false; }
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
    private async Task Run(Func<Task> action)
    {
        if (IsBusy) return; IsBusy = true; Notice = "";
        try { await action(); }
        catch (OperationCanceledException) { Notice = "Cancelled. You can try again when you're ready."; }
        catch (SignInException ex) { Notice = ex.Message; }
        catch (SafetyException ex) { Notice = ex.Message; }
        catch (HttpRequestException) { Notice = "Couldn't reach the service. Check your connection, then retry."; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Notice = "This step couldn't be completed. Check your selected window and permissions, then try again."; }
        finally { IsBusy = false; RefreshUi(); }
    }
    [RelayCommand] private void DismissNotice() => Notice = "";
    [RelayCommand] private void RefreshSources()
    {
        if (controller.State is not (LessonState.Idle or LessonState.Stopped)) { Notice = "Stop this lesson before choosing a different window."; return; }
        var selected = SelectedSource?.TargetId; Sources.Clear(); PreparedWindow = null;
        try { foreach (var source in controller.Sources()) Sources.Add(source); SelectedSource = Sources.FirstOrDefault(s => s.TargetId == selected); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Notice = "Windows couldn't list your open apps. Open the app you want help with, then refresh."; }
    }
    [RelayCommand] private Task ContinueSetup() => Run(async () =>
    {
        var prompt = Goal;
        if (string.IsNullOrWhiteSpace(prompt)) throw new SafetyException("Describe what you want to do first.");
        if (prompt.Length > 1000) throw new SafetyException("Keep your task description under 1,000 characters.");
        if (IsMock) throw new SafetyException("Live guidance needs the connected cloud build. No offline walkthrough is available.");
        if (browserLauncher is null) throw new SafetyException("Browser launch is unavailable in this build.");
        if (browserPlanner is null) throw new SafetyException("Live intent planning is unavailable in this build.");
        Stop();
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
            SelectedSource = launched; SelectedLesson = plan.LessonId; SelectedMode = 0;
            ObservationConsent = true; controller.Masks = ReadMasks();
            await controller.PreviewAsync(launched); task.Token.ThrowIfCancellationRequested();
            PreviewAccepted = true; ActiveView = "lesson"; controller.DetailLevel = SelectedDetail;
            Notice = "";
            GuideStarting?.Invoke(launched);
            ignoreStartReset = true;
            await controller.StartAsync(launched, prompt, plan.LessonId, InteractionMode.Guide, false, false, SaveProgress);
        }
        catch { controller.StopLocal(); ClearConsent(); throw; }
        finally { ignoreStartReset = false; if (taskCancellation == task) taskCancellation = null; }
    });
    [RelayCommand] private void ChooseExistingWindow() { Stop(); RefreshSources(); SelectedLesson = "browser"; SetupStep = 1; ActiveView = "setup"; }
    [RelayCommand] private Task HelpCurrentWindow() => Run(async () =>
    {
        var selected = PreparedWindow ?? throw new SafetyException("Press Ctrl + Alt + Space while your task window is in front.");
        var prompt = Goal;
        if (string.IsNullOrWhiteSpace(prompt)) throw new SafetyException("Describe what you want to do in this window.");
        if (IsMock) throw new SafetyException("Live screen help needs the connected cloud build.");
        Stop(); using var task = new CancellationTokenSource(); taskCancellation = task;
        try
        {
            if (!SignedIn)
            {
                IsSigningIn = true;
                try { await login.SignInAsync(task.Token); } finally { IsSigningIn = false; }
            }
            task.Token.ThrowIfCancellationRequested(); await CheckConnectionCore(task.Token); task.Token.ThrowIfCancellationRequested();
            SelectedSource = selected; SelectedLesson = "browser"; SelectedMode = 0;
            ObservationConsent = true; controller.Masks = ReadMasks();
            await controller.PreviewAsync(selected); task.Token.ThrowIfCancellationRequested();
            PreviewAccepted = true; ActiveView = "lesson"; controller.DetailLevel = SelectedDetail; ignoreStartReset = true;
            GuideStarting?.Invoke(selected);
            await controller.StartAsync(selected, prompt, "browser", InteractionMode.Guide, false, false, SaveProgress);
        }
        catch { controller.StopLocal(); ClearConsent(); throw; }
        finally { ignoreStartReset = false; if (taskCancellation == task) taskCancellation = null; }
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
        if (!SignedIn) throw new SignInException("Sign in before starting a cloud lesson.");
        if (!IsServiceReady) await CheckConnectionCore();
        if (!ObservationConsent || !HasPreview || !PreviewAccepted || SelectedSource is null || string.IsNullOrWhiteSpace(Goal))
            throw new SafetyException("Preview your selected window and accept its scope before starting.");
        if (SelectedMode == 1 && (!DemonstrationConsent || SelectedLesson != "sandbox")) throw new SafetyException("Demonstrations need separate permission and a practice sandbox lesson.");
        if (SelectedMode == 2 && PointerConsent) throw new SafetyException("Let me try doesn't move the pointer. Turn off pointer guidance first.");
        if (IsMock) throw new SafetyException("Connect the live cloud build to analyse your screen.");
        ActiveView = "lesson"; controller.DetailLevel = SelectedDetail; ignoreStartReset = true;
        GuideStarting?.Invoke(SelectedSource);
        try { await controller.StartAsync(SelectedSource, Goal, SelectedLesson, (InteractionMode)SelectedMode, PointerConsent, DemonstrationConsent, SaveProgress); }
        finally { ignoreStartReset = false; }
    });
    [RelayCommand] private void Stop() { taskCancellation?.Cancel(); signInCancellation?.Cancel(); controller.StopLocal(); ClearConsent(); Status = "Stopped · permissions cleared"; }
    [RelayCommand] private void Pause() => controller.PauseLocal();
    [RelayCommand] private Task Resume() => Run(async () =>
    {
        if (!ResumeConsent) throw new SafetyException("Allow a fresh observation before resuming with the same selected window and lesson permissions.");
        await controller.ResumeAsync(); ResumeConsent = false;
    });
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
}
