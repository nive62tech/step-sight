using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopGuide.App.Services;
using DesktopGuide.Core;
using DesktopGuide.Platform;
using DesktopGuide.Windows;

namespace DesktopGuide.App;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly LessonController controller;
    private readonly CognitoLogin login;
    private readonly WindowsPointer pointer;
    private readonly IUserActivityObserver activity;
    public string BrandName { get; }
    public string BuildLabel { get; }
    public string EnvironmentLabel { get; }
    public string HotkeyStatus => activity.HotkeyRegistered ? "Stop: Ctrl+Alt+Shift+S" : "Global Stop hotkey unavailable — use panel or tray Stop";
    public ObservableCollection<CaptureSource> Sources { get; } = [];
    public string[] Lessons { get; } = ["sandbox", "notepad", "vscode", "railway"];
    public string[] Modes { get; } = ["Guide me", "Show me this action", "Let me try"];
    public string[] DetailLevels { get; } = ["brief", "standard", "detailed"];
    [ObservableProperty] private string selectedDetail = "standard";
    [ObservableProperty] private string responseTiming = "Response timing appears after an observation.";
    [ObservableProperty] private CaptureSource? selectedSource;
    [ObservableProperty] private string selectedLesson = "sandbox";
    [ObservableProperty] private int selectedMode;
    [ObservableProperty] private string goal = "Teach me to configure the practice workspace";
    [ObservableProperty] private bool observationConsent;
    [ObservableProperty] private bool previewAccepted;
    [ObservableProperty] private bool pointerConsent;
    [ObservableProperty] private bool demonstrationConsent;
    [ObservableProperty] private bool saveProgress;
    [ObservableProperty] private bool narration;
    [ObservableProperty] private bool reducedMotion;
    [ObservableProperty] private bool highContrast;
    [ObservableProperty] private double textSize = 14;
    [ObservableProperty] private bool rememberMe;
    [ObservableProperty] private string masks = "";
    [ObservableProperty] private string question = "";
    [ObservableProperty] private string instruction = "A guide beside your work";
    [ObservableProperty] private string explanation = "Start with offline practice, or explicitly select and preview a source. Capture and input stay off until you grant permission.";
    [ObservableProperty] private string status = "Idle — capture off";
    [ObservableProperty] private string notice = "";
    [ObservableProperty] private Bitmap? previewImage;
    [ObservableProperty] private ActionProposal? currentProposal;
    [ObservableProperty] private string proposalSummary = "No action proposed";
    [ObservableProperty] private bool recording;
    [ObservableProperty] private int practiceIndex = -1;
    private static readonly string[] Practice = [
        "Open the practice sandbox. Find Settings yourself. Nothing is being observed or controlled.",
        "Choose Ocean and enter a harmless workspace name. A disabled button does not prove success.",
        "Choose Save workspace. Wait for the application and inspect its explicit success message.",
        "Practice finished by your report. This offline walkthrough did not verify your screen."
    ];
    public MainViewModel(LessonController controller, CognitoLogin login, PublicConfiguration config, WindowsPointer pointer, IUserActivityObserver activity)
    {
        (this.controller, this.login, this.pointer, this.activity) = (controller, login, pointer, activity);
        BrandName = config.BrandName; BuildLabel = config.BuildLabel;
        EnvironmentLabel = config.AuthMode == "mock" ? "LOCAL / MOCK — no live screen understanding" : "CLOUD — AWS + GCP processing";
        RefreshSources();
        controller.Presentation += (instruction, explanation, status) => OnUi(() => { Instruction = instruction; Explanation = explanation; Status = status; });
        controller.Notice += message => OnUi(() => Notice = message);
        controller.Preview += frame => OnUi(() => { PreviewImage?.Dispose(); PreviewImage = new Bitmap(new MemoryStream(frame.Png)); });
        controller.StateChanged += state => OnUi(() => { Status = StatusText(state); if (state is LessonState.Stopped or LessonState.Paused or LessonState.PrivacyPaused or LessonState.NetworkInterrupted) Recording = false; });
        controller.ProposalChanged += proposal => OnUi(() => { CurrentProposal = proposal; ProposalSummary = proposal is null ? "No action proposed" : $"{proposal.Action.Kind}: {proposal.Effect}"; });
        controller.TimingsRecorded += timings => OnUi(() => ResponseTiming = $"Capture: {timings["capture_preprocess"]:F0} ms; response: {timings["roundtrip"]:F0} ms");
    }
    private static void OnUi(Action action) => Dispatcher.UIThread.Post(action);
    private static string StatusText(LessonState state) => state switch
    {
        LessonState.Observing => "Looking at your selected source", LessonState.Planning => "Preparing a step",
        LessonState.Verifying => "Checking the result", LessonState.WaitingForLearner => "Waiting for you",
        LessonState.PrivacyPaused => "Paused for privacy — capture off", LessonState.Stopped => "Stopped — permissions revoked",
        LessonState.NetworkInterrupted => "Network interrupted — input disarmed", _ => state.ToString()
    };
    partial void OnSelectedSourceChanged(CaptureSource? value) => PreviewAccepted = false;
    partial void OnMasksChanged(string value) => PreviewAccepted = false;
    partial void OnNarrationChanged(bool value) => controller.NarrationEnabled = value;
    partial void OnReducedMotionChanged(bool value) => pointer.ReducedMotion = value;
    partial void OnRememberMeChanged(bool value) => login.RememberMe = value;
    partial void OnHighContrastChanged(bool value) { if (Application.Current is { } app) app.RequestedThemeVariant = ThemeVariant.Dark; }
    private IReadOnlyList<Bounds> ReadMasks()
    {
        if (string.IsNullOrWhiteSpace(Masks)) return [];
        var result = Masks.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(text =>
        {
            var values = text.Split(',').Select(v => double.Parse(v.Trim(), CultureInfo.InvariantCulture)).ToArray();
            if (values.Length != 4) throw new SafetyException("Each privacy mask needs left,top,right,bottom between 0 and 1");
            var bounds = new Bounds(values[0], values[1], values[2], values[3]); bounds.Validate(); return bounds;
        }).ToArray();
        if (result.Length > 10) throw new SafetyException("At most ten privacy masks are supported");
        return result;
    }
    private async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { Notice = "Operation cancelled. Local controls remain available."; }
        catch (SafetyException ex) { Notice = ex.Message; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Notice = "Setup or device unavailable. Check configuration and permissions; no action was authorized by this error."; }
    }
    [RelayCommand] private void RefreshSources()
    {
        if (controller.State is not (LessonState.Idle or LessonState.Stopped)) { Notice = "Stop the lesson before refreshing capture sources."; return; }
        Sources.Clear(); foreach (var source in controller.Sources()) Sources.Add(source); SelectedSource = Sources.FirstOrDefault();
    }
    [RelayCommand] private Task Preview() => Run(async () =>
    {
        if (!ObservationConsent || SelectedSource is null) throw new SafetyException("Grant observation permission and select a source first");
        controller.Masks = ReadMasks(); await controller.PreviewAsync(SelectedSource);
    });
    [RelayCommand] private Task Start() => Run(async () =>
    {
        if (!ObservationConsent || !PreviewAccepted || SelectedSource is null || string.IsNullOrWhiteSpace(Goal))
            throw new SafetyException("Preview and accept the exact capture scope before starting");
        if (SelectedMode == 1 && !DemonstrationConsent) throw new SafetyException("Grant demonstration permission separately");
        if (SelectedMode == 2 && PointerConsent) throw new SafetyException("Practice mode does not move the pointer; disable pointer permission");
        controller.DetailLevel = SelectedDetail;
        await controller.StartAsync(SelectedSource, Goal, SelectedLesson, (InteractionMode)SelectedMode, PointerConsent, DemonstrationConsent, SaveProgress);
    });
    [RelayCommand] private void Stop() => controller.StopLocal();
    [RelayCommand] private void Pause() => controller.PauseLocal();
    [RelayCommand] private Task Resume() => Run(controller.ResumeAsync);
    [RelayCommand] private Task ShowWhere() => Run(controller.ShowWhereAsync);
    [RelayCommand] private Task DidThis() => Run(() => controller.ObserveTurnAsync("i_did_this"));
    [RelayCommand] private Task Reobserve() => Run(() => controller.ObserveTurnAsync("reobserve"));
    [RelayCommand] private Task ExplainMore() => Run(() => controller.AskAsync("Explain why this step matters and what its terms mean."));
    [RelayCommand] private Task Ask() => Run(async () => { if (!string.IsNullOrWhiteSpace(Question)) { await controller.AskAsync(Question); Question = ""; } });
    [RelayCommand] private Task SignIn() => Run(async () => { controller.StopLocal(); await login.SignInAsync(CancellationToken.None); });
    [RelayCommand] private Task SignOut() => Run(async () => { controller.StopLocal(); await login.SignOutAsync(); Notice = "Signed out. Local permissions revoked."; });
    [RelayCommand] private Task ForgetProgress() => Run(controller.ForgetProgressAsync);
    [RelayCommand] private void Demonstration()
    {
        if (CurrentProposal is null) { Notice = "No current action proposal. Continue manually or reobserve."; return; }
        var approved = CurrentProposal;
        var dialog = new ApprovalWindow(approved, () => Run(() => controller.DemonstrateAsync(approved)));
        dialog.Opened += (_, _) => { var handle = dialog.TryGetPlatformHandle()?.Handle ?? 0; if (handle != 0) activity.IgnoreWindow(handle); };
        dialog.Show();
    }
    [RelayCommand] private Task PushToTalk() => Run(async () =>
    {
        if (!Recording) { controller.BeginQuestionRecording(); Recording = true; Notice = "Recording — select Push to talk again to finish. Maximum 30 seconds."; }
        else { Recording = false; await controller.EndQuestionRecordingAsync(); }
    });
    [RelayCommand] private void OfflinePractice()
    {
        controller.StopLocal(); PracticeIndex = 0; Instruction = Practice[0]; Explanation = "OFFLINE walkthrough — learner-attested progress, no screen understanding.";
        Process.Start(new ProcessStartInfo("http://127.0.0.1:8765") { UseShellExecute = true });
    }
    [RelayCommand] private void PracticeNext()
    {
        if (PracticeIndex < 0) return;
        PracticeIndex = Math.Min(PracticeIndex + 1, Practice.Length - 1); Instruction = Practice[PracticeIndex]; Status = "OFFLINE — unverified learner report";
    }
}
