using DesktopGuide.App;
using DesktopGuide.App.Services;
using DesktopGuide.Core;
using DesktopGuide.Platform;
using DesktopGuide.Windows;
using Xunit;

namespace DesktopGuide.AppTests;

public sealed class ApplicationHandoffTests
{
    [Fact]
    public async Task UpdatingSelectedWindowFromAgentDoesNotStopTheAutonomousTask()
    {
        var env = new HandoffEnvironment(); var permissions = new PermissionSupervisor();
        env.UsePermissions(permissions);
        using var inert = new PreviewEnvironment(); var pointer = new WindowsPointer(permissions,env,env);
        using var controller = new LessonController(env,env,env,pointer,inert,env,inert,inert,inert,permissions,applicationHandoff:env);
        var vm = new MainViewModel(controller,new Login(),new("https://example.invalid","cognito","https://example.invalid","client","https://example.invalid","openid","http://localhost:43827/callback/","Test","Test"),
            pointer,env,new Connection(),dispatch:action=>action());
        vm.SelectedMode = 3; vm.SelectedSource = env.Source;
        await controller.PreviewAsync(env.Source); // No bitmap/UI capture: this is a fake adapter.
        controller.StateChanged += state => { if (state == LessonState.Consent) vm.ObservationConsent = true; };
        await controller.StartAsync(env.Source,"Open WhatsApp Desktop","browser",InteractionMode.Autonomous,false,false,false,true);
        Assert.Equal(LessonState.Completed,controller.State);
        Assert.Equal(env.NextSource,vm.SelectedSource); Assert.Equal(1,env.Launches);
        Assert.Equal(new[] {"start","app_handoff"},env.Events);
    }
    private sealed class Login : ILoginSession
    {
        public bool SignedIn => true; public bool IsMock => false; public string? AccountName => "synthetic";
        public bool RememberMe { get; set; } public event Action? Changed { add{} remove{} }
        public Task SignInAsync(CancellationToken c) => Task.CompletedTask;
        public Task SignOutAsync() => Task.CompletedTask;
        public Task<string?> GetAccessAsync(CancellationToken c) => Task.FromResult<string?>(null);
    }
    private sealed class Connection : IAccountConnection
    {
        public Task CheckAsync(CancellationToken c) => Task.CompletedTask;
    }
    private sealed class HandoffEnvironment : IScreenCapture,IWindowTracker,IUserActivityObserver,IApplicationHandoff,IGuidanceApiClient
    {
        public CaptureSource Source = new(Guid.NewGuid(),Guid.NewGuid(),"Synthetic browser","window");
        public CaptureSource NextSource = new(Guid.NewGuid(),Guid.NewGuid(),"Synthetic WhatsApp","window");
        private SessionView session = null!;
        private PermissionSupervisor? permissions;
        public int Launches;
        public List<string> Events = [];
        public IReadOnlyList<string> SupportedApplications => ["whatsapp"];
        public IReadOnlyList<CaptureSource> ListSources() => [Source];
        private readonly Dictionary<Guid,Geometry> geometries = [];
        public Task<ScreenFrame> CaptureAsync(CaptureSource source,IReadOnlyList<Bounds> masks,CancellationToken c)
        {
            if (!geometries.TryGetValue(source.ScopeId,out var g)) geometries[source.ScopeId] = g = new(Guid.NewGuid(),source.ScopeId,new(0,0,800,450),800,450,96,"fake",source.TargetId,new(0,0,1,1),new(0,0,1,1));
            return Task.FromResult(new ScreenFrame(new(Guid.NewGuid(),DateTimeOffset.UtcNow,source.ScopeId,g.Id,800,450,true),g,[1],"fake"));
        }
        public bool IsForegroundAndVisible(CaptureSource source) => true;
        public bool IsCurrentGeometry(CaptureSource source,Geometry g) => true;
        public bool IsPointVisible(CaptureSource source,int x,int y) => true;
        public bool IsLocked => false;
        public bool HasMeaningfulChange(ScreenFrame a,ScreenFrame b) => false;
        public bool TrackingAvailable => true; public bool HotkeyRegistered => false;
        public bool IsAssistantInputActive { get; set; } public bool ObservationEnabled { get; set; }
        public bool AutonomousControlActive { get; set; }
        public event Action? Activity {add{} remove{}} public event Action? Takeover {add{} remove{}}
        public event Action? EmergencyStop {add{} remove{}} public event Action? SessionUnavailable {add{} remove{}}
        public void IgnoreWindow(nint h){} public void Dispose(){}
        public Task<ApplicationHandoffResult> OpenAsync(LocalCapability capability,ActionProposal proposal,ScreenFrame frame,CaptureSource source,CancellationToken c)
        {
            // The controller's local supervisor must consume the exact app action.
            var lease = permissions!.Consume(capability,proposal,frame,true); Launches++;
            return Task.FromResult(new ApplicationHandoffResult(NextSource,lease));
        }
        public Task<SessionView> CreateAsync(SessionCreate r,CancellationToken c)
        {
            session = new(1,Guid.NewGuid(),r.DeviceId,r.Goal,r.Mode,"en","standard",r.ScopeId,0,"OBSERVING","browser",false,0,DateTimeOffset.UtcNow,"gemini",null);
            return Task.FromResult(session);
        }
        public Task<TurnResponse> TurnAsync(Guid id,TurnRequest request,ScreenFrame frame,CancellationToken c)
        {
            Events.Add(request.EventKind); session=session with {SessionVersion=request.SessionVersion+1};
            var done=request.EventKind=="app_handoff";
            var step = done ? null : new GuidanceStep(Guid.NewGuid(),frame.Observation.Id,"Open WhatsApp Desktop","Continue task",null,"open_app",[new("visible_state","WhatsApp window visible")],[],[new(frame.Observation.Id,"Synthetic current app")],[]);
            return Task.FromResult(new TurnResponse(1,id,session.SessionVersion,Guid.NewGuid(),frame.Observation.Id,"gemini",step,
                done ? new("CONFIRMED",[new(frame.Observation.Id,"Synthetic WhatsApp window")],[],null,"gemini") : null,
                done ? null : new(Guid.NewGuid(),frame.Observation.Id,frame.Geometry.Id,new("open_app",Application:"whatsapp"),"Open requested app","low",false),done));
        }
        public void UsePermissions(PermissionSupervisor value) => permissions=value;
        public Task<SessionView> GetAsync(Guid id,CancellationToken c) => Task.FromResult(session);
        public Task<SessionView> IntentAsync(Guid id,string intent,int v,CancellationToken c) => Task.FromResult(session with {SessionVersion=v+1,State=intent.ToUpperInvariant()});
        public Task EventAsync(Guid id,LearnerEvent e,CancellationToken c) => Task.CompletedTask;
        public Task<QuestionResponse> QuestionAsync(Guid id,int v,string text,CancellationToken c) => throw new NotSupportedException();
        public Task<string> TranscribeAsync(byte[] wav,CancellationToken c) => throw new NotSupportedException();
        public Task<byte[]> SynthesizeAsync(string text,CancellationToken c) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id,CancellationToken c) => Task.CompletedTask;
    }
}
