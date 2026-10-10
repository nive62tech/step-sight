using Avalonia;
using DesktopGuide.App;
using DesktopGuide.App.Services;
using DesktopGuide.Core;
using DesktopGuide.Platform;
using DesktopGuide.Windows;
using Xunit;

namespace DesktopGuide.AppTests;

public sealed class OnboardingTests
{
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
        public Fixture(IBrowserLauncher? launcher = null)
        {
            var env = new PreviewEnvironment(); var permissions = new PermissionSupervisor();
            var pointer = new WindowsPointer(permissions,env,env);
            Controller = new(env,env,env,pointer,env,new Api(),env,env,env,permissions);
            Vm = new(Controller,Login,new("https://example.invalid","cognito","https://cognito-idp.ap-south-1.amazonaws.com/test","client","https://example.invalid","openid","http://localhost:43827/callback/","Desktop Guide","test"),
                pointer,env,Connection,action=>action(),launcher);
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

