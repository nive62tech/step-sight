using Avalonia;
using DesktopGuide.App;
using DesktopGuide.App.Services;
using DesktopGuide.Core;
using DesktopGuide.Platform;
using DesktopGuide.Windows;
using Xunit;
using System.Threading.Channels;

namespace DesktopGuide.AppTests;

public sealed class OnboardingTests
{
    private sealed class PhoneApi : IRemoteAgentClient
    {
        public Channel<RemoteCommand?> Responses = Channel.CreateUnbounded<RemoteCommand?>();
        public Channel<RemotePoll> Requests = Channel.CreateUnbounded<RemotePoll>();
        public Task<RemoteLease> RegisterAsync(RemoteRegistration r, CancellationToken c) => Task.FromResult(new RemoteLease(1, Guid.NewGuid(), "synthetic", DateTimeOffset.UtcNow.AddHours(1)));
        public async Task<RemotePollResponse> PollAsync(RemoteLease l, RemotePoll r, CancellationToken c)
        { await Requests.Writer.WriteAsync(r, c); return new(1, await Responses.Reader.ReadAsync(c)); }
        public Task DisconnectAsync(RemoteLease l, CancellationToken c) => Task.CompletedTask;
    }
    [Fact] public async Task PhoneTaskUsesLocallyAllowedModeAndLocalStopRejectsLateBrowserLaunch()
    {
        var launcher = new Launcher(); var phone = new PhoneApi(); using var f = new Fixture(launcher);
        await f.Vm.SignInCommand.ExecuteAsync(null);
        f.Vm.AttachRemote(phone, "https://example.invalid"); f.Vm.AllowAutonomousPhoneTasks = true;
        await f.Vm.EnablePhoneControlCommand.ExecuteAsync(null);
        Assert.True(f.Vm.PhoneControlEnabled); Assert.False(f.Vm.CanSubmitTask);
        await phone.Requests.Reader.ReadAsync();
        await phone.Responses.Writer.WriteAsync(new(Guid.NewGuid(), "start", "autonomous", "Find trains", DateTimeOffset.UtcNow.AddSeconds(30)));
        var ack = await phone.Requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4));
        Assert.True(ack.Acknowledgement!.Accepted); Assert.True(f.Vm.IsAutonomous); Assert.Equal(1, launcher.Opens);
        f.Vm.StopCommand.Execute(null);
        launcher.Complete.SetResult(new(Guid.NewGuid(), Guid.NewGuid(), "synthetic browser", "window"));
        for (var i = 0; i < 100 && f.Vm.IsBusy; i++) await Task.Delay(10);
        Assert.False(f.Vm.IsBusy); Assert.False(f.Vm.PhoneControlEnabled); Assert.False(f.Vm.ObservationConsent);
        Assert.Equal(LessonState.Stopped, f.Controller.State); Assert.Null(f.Vm.SelectedSource);
    }
    private sealed class Voice : IVoicePromptRecorder
    {
        public int Starts, Cancels;
        public TaskCompletionSource<string> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Start() => Starts++;
        public Task<string> FinishAsync(CancellationToken c) => Result.Task; // Simulate a late transcription.
        public void Cancel() => Cancels++;
    }
    [Fact] public async Task BlobVoiceProducesAnEditableDraftWithoutStartingActions()
    {
        var voice = new Voice(); using var f = new Fixture(voice: voice);
        await f.Vm.SignInCommand.ExecuteAsync(null); f.Vm.AutonomousModeSelected = true;
        await f.Vm.TogglePromptVoiceCommand.ExecuteAsync(null);
        Assert.True(f.Vm.VoiceListening); Assert.False(f.Vm.CanSubmitTask); Assert.Equal(1, voice.Starts);
        var finishing = f.Vm.TogglePromptVoiceCommand.ExecuteAsync(null);
        Assert.True(f.Vm.VoiceTranscribing); f.Vm.AutonomousConsent = true;
        voice.Result.SetResult("Find available trains tomorrow"); await finishing;
        Assert.Equal("Find available trains tomorrow", f.Vm.Goal);
        Assert.False(f.Vm.AutonomousConsent); Assert.False(f.Vm.ObservationConsent); Assert.False(f.Vm.HasLesson);
        Assert.True(f.Vm.CanSubmitTask);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task StopOrModeChangeDiscardsLateVoiceDraft(bool modeChange)
    {
        var voice = new Voice(); using var f = new Fixture(voice: voice);
        await f.Vm.SignInCommand.ExecuteAsync(null); var original = f.Vm.Goal;
        await f.Vm.TogglePromptVoiceCommand.ExecuteAsync(null);
        var finishing = f.Vm.TogglePromptVoiceCommand.ExecuteAsync(null);
        if (modeChange) f.Vm.AutonomousModeSelected = true; else f.Vm.StopCommand.Execute(null);
        voice.Result.SetResult("A late draft"); await finishing;
        Assert.Equal(original, f.Vm.Goal); Assert.False(f.Vm.VoiceListening); Assert.False(f.Vm.VoiceTranscribing);
        Assert.True(voice.Cancels > 0); Assert.False(f.Vm.AutonomousConsent);
    }
    [Fact] public async Task AutonomousSelectionNeedsConsentBeforeAnySignInOrLaunch()
    {
        var launcher = new Launcher(); using var f = new Fixture(launcher);
        Assert.Equal(0, f.Vm.SelectedTaskMode); Assert.False(f.Vm.IsAutonomous);
        f.Vm.SelectedTaskMode = 1; Assert.True(f.Vm.IsAutonomous); Assert.False(f.Vm.AutonomousConsent);
        await f.Vm.ContinueSetupCommand.ExecuteAsync(null);
        Assert.Contains("Allow autonomous control", f.Vm.Notice); Assert.False(f.Login.SignedIn); Assert.Equal(0, launcher.Opens);
        f.Vm.AutonomousConsent = true; f.Vm.SelectedTaskMode = 0;
        Assert.False(f.Vm.AutonomousConsent); Assert.False(f.Vm.IsAutonomous);
        f.Vm.SelectedTaskMode = 1; f.Vm.AutonomousConsent = true; f.Vm.StopCommand.Execute(null);
        Assert.False(f.Vm.AutonomousConsent); Assert.False(f.Vm.ResumeConsent);
    }
    [Fact] public void YourTurnKeepsInstructionAndFailureVisibleInCompactCard()
    {
        using var f = new Fixture();
        f.Vm.LessonState = LessonState.WaitingForLearner;
        f.Vm.Instruction = "Click the visible Settings button.";
        f.Vm.GuideMarkerState = GuideMarkerState.MissingTarget;
        Assert.True(f.Vm.HasCompactCard); Assert.Equal(f.Vm.Instruction, f.Vm.CompactInstruction);
        Assert.Contains("No visible", f.Vm.MarkerStatus);
        f.Vm.Notice = "The image service is unavailable. Retry Find click target.";
        Assert.True(f.Vm.HasNotice); Assert.True(f.Vm.HasCompactCard);
        f.Vm.LessonState = LessonState.Verifying;
        Assert.Contains("Reading", f.Vm.CompactInstruction);
    }
    [Theory] [InlineData(0, 1.0)] [InlineData(-1920, 1.5)] [InlineData(0, 2.0)]
    public void CompanionMovesAwayFromTargetAtDifferentDpi(int monitorX, double scale)
    {
        var screen = new PixelRect(monitorX, 0, 1920, 1080);
        var target = new PhysicalRect(monitorX + 1720, 980, 100, 50);
        var size = new PixelSize((int)(374 * scale), (int)(340 * scale));
        var position = CompanionPlacement.AvoidTarget(new(screen.Right-size.Width-16,screen.Bottom-size.Height-16),size,screen,target);
        var card = new PixelRect(position,size);
        Assert.False(card.Intersects(new PixelRect(target.Left-36,target.Top-36,target.Width+72,target.Height+72)));
        Assert.True(screen.Contains(card));
    }
    private sealed class Login : ILoginSession
    {
        public bool SignedIn { get; private set; }
        public bool IsMock => false;
        public string? AccountName => SignedIn ? "learner@example.invalid" : null;
        public bool RememberMe { get; set; }
        public bool Wait;
        public event Action? Changed;
        public async Task SignInAsync(CancellationToken cancellation) { if (Wait) await Task.Delay(Timeout.Infinite,cancellation); SignedIn=true; Changed?.Invoke(); }
        public Task SignOutAsync() { SignedIn=false; Changed?.Invoke(); return Task.CompletedTask; }
        public Task<string?> GetAccessAsync(CancellationToken cancellation) => Task.FromResult<string?>(null);
    }
    private sealed class Connection : IAccountConnection
    {
        public bool Fail;
        public Task CheckAsync(CancellationToken cancellation) => Fail ? Task.FromException(new HttpRequestException("synthetic outage")) : Task.CompletedTask;
    }
    private sealed class Api : IGuidanceApiClient
    {
        public Task<SessionView> CreateAsync(SessionCreate request,CancellationToken c) => throw new Exception("No session may be created by signing in");
        public Task<SessionView> GetAsync(Guid id,CancellationToken c) => throw new NotSupportedException();
        public Task<TurnResponse> TurnAsync(Guid id,TurnRequest request,ScreenFrame frame,CancellationToken c) => throw new NotSupportedException();
        public Task<SessionView> IntentAsync(Guid id,string intent,int version,CancellationToken c) => throw new NotSupportedException();
        public Task<QuestionResponse> QuestionAsync(Guid id,int version,string text,CancellationToken c) => throw new NotSupportedException();
        public Task EventAsync(Guid id,LearnerEvent e,CancellationToken c) => throw new NotSupportedException();
        public Task<string> TranscribeAsync(byte[] wav,CancellationToken c) => throw new NotSupportedException();
        public Task<byte[]> SynthesizeAsync(string text,CancellationToken c) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id,CancellationToken c) => throw new NotSupportedException();
    }
    private sealed class Fixture : IDisposable
    {
        public Login Login = new(); public Connection Connection = new();
        public LessonController Controller; public MainViewModel Vm;
        public Fixture(IBrowserLauncher? launcher = null, IBrowserPlanner? planner = null, IVoicePromptRecorder? voice = null)
        {
            var env = new PreviewEnvironment(); var permissions = new PermissionSupervisor();
            var pointer = new WindowsPointer(permissions,env,env);
            Controller = new(env,env,env,pointer,env,new Api(),env,env,env,permissions);
            Vm = new(Controller,Login,new("https://example.invalid","cognito","https://cognito-idp.ap-south-1.amazonaws.com/test","client","https://example.invalid","openid","http://localhost:43827/callback/","Desktop Guide","test"),
                pointer,env,Connection,action=>action(),launcher,planner ?? new Planner(),voice);
            Vm.Goal = "Help me with my website task";
        }
        public void Dispose() => Controller.Dispose();
    }
    private sealed class Launcher : IBrowserLauncher
    {
        public int Opens;
        public BrowserLaunch? Plan;
        public TaskCompletionSource<CaptureSource> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CaptureSource> OpenAsync(BrowserLaunch plan, CancellationToken cancellation)
        { Opens++; Plan = plan; return Complete.Task; } // Deliberately ignores Stop to test late completion.
    }
    private sealed class Planner : IBrowserPlanner
    {
        public BrowserPlanResponse Result = new(1, "open_browser", "edge", "https://example.org/", "Open the site.", "gemini");
        public TaskCompletionSource<BrowserPlanResponse>? Pending;
        public string? Goal;
        public Task<BrowserPlanResponse> PlanAsync(string goal, CancellationToken cancellation)
        { Goal = goal; return Pending?.Task ?? Task.FromResult(Result); }
    }
    [Fact] public async Task StopDuringBrowserLaunchRejectsLateWindowAndNeverArmsCapture()
    {
        var launcher = new Launcher(); using var f = new Fixture(launcher);
        await f.Vm.SignInCommand.ExecuteAsync(null);
        f.Vm.Goal = "Open Edge and guide me on Railway";
        var pending = f.Vm.ContinueSetupCommand.ExecuteAsync(null);
        Assert.Equal(1, launcher.Opens); Assert.Equal("edge", launcher.Plan!.Browser);
        Assert.False(f.Vm.ObservationConsent);
        f.Vm.StopCommand.Execute(null);
        launcher.Complete.SetResult(new(Guid.NewGuid(),Guid.NewGuid(),"new browser", "window")); await pending;
        Assert.False(f.Vm.ObservationConsent); Assert.Null(f.Vm.SelectedSource);
        Assert.Equal(LessonState.Stopped, f.Controller.State);
    }
    [Fact] public async Task IrctcPromptReachesBrowserLaunchWithoutRequestingAUrl()
    {
        var launcher = new Launcher(); var planner = new Planner { Result = new(1, "open_browser", "chrome", "https://www.irctc.co.in/nget/train-search", "Open the train booking site.", "gemini") };
        using var f = new Fixture(launcher, planner);
        await f.Vm.SignInCommand.ExecuteAsync(null);
        f.Vm.Goal = "Open Chrome and guide me to book a train ticket on irtc";
        var pending = f.Vm.ContinueSetupCommand.ExecuteAsync(null);
        Assert.Equal(1, launcher.Opens);
        Assert.Equal("https://www.irctc.co.in/nget/train-search", launcher.Plan!.Address.AbsoluteUri);
        Assert.Equal("browser", launcher.Plan.LessonId);
        Assert.Equal(f.Vm.Goal, planner.Goal);
        Assert.False(f.Vm.ObservationConsent); Assert.False(f.Vm.DemonstrationConsent);
        f.Vm.StopCommand.Execute(null);
        launcher.Complete.SetResult(new(Guid.NewGuid(), Guid.NewGuid(), "IRCTC", "window"));
        await pending;
        Assert.Equal(LessonState.Stopped, f.Controller.State);
        Assert.False(f.Vm.ObservationConsent);
    }
    [Fact] public async Task StopDuringIntentPlanningRejectsLatePlanBeforeOpeningOrCapture()
    {
        var launcher = new Launcher(); var planner = new Planner { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var f = new Fixture(launcher, planner);
        await f.Vm.SignInCommand.ExecuteAsync(null);
        f.Vm.Goal = "Help with an arbitrary website task";
        var pending = f.Vm.ContinueSetupCommand.ExecuteAsync(null);
        Assert.Equal(f.Vm.Goal, planner.Goal); Assert.Equal(0, launcher.Opens);
        f.Vm.StopCommand.Execute(null);
        planner.Pending.SetResult(planner.Result); await pending;
        Assert.Equal(0, launcher.Opens); Assert.False(f.Vm.ObservationConsent); Assert.Null(f.Vm.SelectedSource);
    }
    [Fact] public async Task PlannerClarificationStaysVisibleWithoutOpeningOrCapture()
    {
        var launcher = new Launcher(); var planner = new Planner { Result = new(1, "clarify", "chrome", null, "Which website do you mean?", "gemini") };
        using var f = new Fixture(launcher, planner);
        await f.Vm.SignInCommand.ExecuteAsync(null);
        await f.Vm.ContinueSetupCommand.ExecuteAsync(null);
        Assert.Equal(0, launcher.Opens); Assert.Contains("Which website", f.Vm.Notice);
        Assert.False(f.Vm.ObservationConsent); Assert.False(f.Vm.DemonstrationConsent);
    }
    [Fact] public async Task StopDuringPromptSignInPreventsAutomaticLaunch()
    {
        var launcher = new Launcher(); using var f = new Fixture(launcher); f.Login.Wait = true;
        var pending = f.Vm.ContinueSetupCommand.ExecuteAsync(null);
        Assert.True(f.Vm.IsSigningIn); f.Vm.StopCommand.Execute(null); await pending;
        Assert.Equal(0, launcher.Opens); Assert.False(f.Vm.ObservationConsent); Assert.False(f.Vm.IsBusy);
    }
    [Fact] public async Task SummoningCurrentWindowDoesNotCaptureAndStopCancelsItsPendingSignIn()
    {
        using var f = new Fixture(); var source = new CaptureSource(Guid.NewGuid(),Guid.NewGuid(),"Synthetic editor","window");
        f.Vm.PrepareCurrentWindow(source);
        Assert.True(f.Vm.HasPreparedWindow); Assert.Equal(source, f.Vm.PreparedWindow);
        Assert.Null(f.Vm.SelectedSource); Assert.False(f.Vm.ObservationConsent);
        f.Login.Wait = true; var starts = 0; f.Vm.GuideStarting += _ => starts++;
        var pending = f.Vm.HelpCurrentWindowCommand.ExecuteAsync(null);
        Assert.True(f.Vm.IsSigningIn); f.Vm.StopCommand.Execute(null); await pending;
        Assert.Equal(0, starts); Assert.Null(f.Vm.SelectedSource); Assert.False(f.Vm.ObservationConsent);
    }
    [Theory] [InlineData(1.0)] [InlineData(1.5)] [InlineData(2.0)]
    public void HighlightMapsPhysicalTargetWithoutDoubleApplyingDpi(double scale)
    {
        var p = HighlightPlacement.Calculate(new(-1750, 400, 120, 48), scale);
        Assert.Equal(new PixelPoint(-1754, 396), p.Position);
        Assert.Equal(128, p.Width * scale, precision: 5); Assert.Equal(56, p.Height * scale, precision: 5);
    }
    [Fact] public async Task FailedBrowserLaunchLeavesAllPermissionsRevoked()
    {
        var launcher = new Launcher(); using var f = new Fixture(launcher);
        await f.Vm.SignInCommand.ExecuteAsync(null);
        launcher.Complete.SetException(new SafetyException("Synthetic browser unavailable"));
        await f.Vm.ContinueSetupCommand.ExecuteAsync(null);
        Assert.Contains("browser unavailable", f.Vm.Notice); Assert.False(f.Vm.ObservationConsent);
        Assert.False(f.Vm.PointerConsent); Assert.False(f.Vm.DemonstrationConsent);
    }
    [Fact] public async Task SignInShowsNextActionWithoutGrantingCaptureOrInput()
    {
        using var f = new Fixture(); await f.Vm.SignInCommand.ExecuteAsync(null);
        Assert.True(f.Vm.SignedIn); Assert.True(f.Vm.IsServiceReady); Assert.True(f.Vm.ShowTask);
        Assert.Equal("You're signed in",f.Vm.AccountTitle); Assert.Equal("Open browser & guide me",f.Vm.ContinueLabel);
        Assert.Contains("Describe your task",f.Vm.Notice); Assert.False(f.Vm.ObservationConsent);
        Assert.False(f.Vm.PointerConsent); Assert.False(f.Vm.DemonstrationConsent); Assert.False(f.Vm.CanStart);
        f.Vm.ChooseExistingWindowCommand.Execute(null); Assert.True(f.Vm.ShowWindow);
    }
    [Fact] public async Task BackendFailureDoesNotHideSuccessfulSignInAndCanBeRetried()
    {
        using var f = new Fixture(); f.Connection.Fail=true; await f.Vm.SignInCommand.ExecuteAsync(null);
        Assert.True(f.Vm.SignedIn); Assert.False(f.Vm.IsServiceReady); Assert.Contains("Retry",f.Vm.Notice);
        Assert.False(f.Vm.IsSigningIn); f.Connection.Fail=false; await f.Vm.CheckConnectionCommand.ExecuteAsync(null);
        Assert.True(f.Vm.IsServiceReady);
    }
    [Fact] public async Task StopCancelsPendingSignInAndRevokesAllUiConsent()
    {
        using var f = new Fixture(); f.Login.Wait=true; f.Vm.ObservationConsent=true; f.Vm.PointerConsent=true;
        var pending = f.Vm.SignInCommand.ExecuteAsync(null); Assert.True(f.Vm.IsSigningIn);
        f.Vm.StopCommand.Execute(null); await pending;
        Assert.False(f.Vm.SignedIn); Assert.False(f.Vm.IsSigningIn); Assert.False(f.Vm.ObservationConsent); Assert.False(f.Vm.PointerConsent);
    }
    [Fact] public void SourceAndMaskEditsInvalidateAcceptedPreview()
    {
        using var f = new Fixture(); f.Vm.HasPreview=true; f.Vm.PreviewAccepted=true;
        f.Vm.SelectedSource=new(Guid.NewGuid(),Guid.NewGuid(),"another window","window");
        Assert.False(f.Vm.HasPreview); Assert.False(f.Vm.PreviewAccepted);
        f.Vm.HasPreview=true; f.Vm.PreviewAccepted=true; f.Vm.Masks="0,0,.2,.2";
        Assert.False(f.Vm.HasPreview); Assert.False(f.Vm.PreviewAccepted);
    }
    [Fact] public void PracticeAndRailwayCannotInheritDemonstrationMode()
    {
        using var f = new Fixture(); f.Vm.PointerConsent=true; f.Vm.DemonstrationConsent=true; f.Vm.SelectedMode=2;
        Assert.False(f.Vm.PointerConsent); Assert.False(f.Vm.DemonstrationConsent);
        f.Vm.SelectedMode=1; f.Vm.ChooseRailwayCommand.Execute(null);
        Assert.Equal("railway",f.Vm.SelectedLesson); Assert.Equal(0,f.Vm.SelectedMode); Assert.True(f.Vm.IsRailwayLesson);
    }
    [Theory]
    [InlineData(-1900,10,1.0)] [InlineData(-1,1060,1.5)] [InlineData(-960,540,2.0)]
    public void MarkerStaysOnScreenAtDpiAndNegativeMonitorCoordinates(int x,int y,double scale)
    {
        var screen = new PixelRect(-1920,0,1920,1080);
        var p = MarkerPlacement.Calculate(new(x,y),screen,scale);
        Assert.InRange(p.Position.X,screen.X,screen.Right-(int)Math.Ceiling(340*scale));
        Assert.InRange(p.Position.Y,screen.Y,screen.Bottom-(int)Math.Ceiling(140*scale));
        Assert.Equal(x,p.Position.X+p.AnchorX*scale,precision:4);
        Assert.Equal(y,p.Position.Y+p.AnchorY*scale,precision:4);
    }
}

