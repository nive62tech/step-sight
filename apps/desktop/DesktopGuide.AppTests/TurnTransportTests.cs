using System.Net;
using System.Text.Json;
using DesktopGuide.App.Services;
using DesktopGuide.Core;
using Xunit;

namespace DesktopGuide.AppTests;

public sealed class TurnTransportTests
{
    private sealed class Login : ILoginSession
    {
        public bool SignedIn => true; public bool IsMock => false; public string? AccountName => null;
        public bool RememberMe { get; set; }
        public event Action? Changed { add {} remove {} }
        public Task SignInAsync(CancellationToken c) => Task.CompletedTask;
        public Task SignOutAsync() => Task.CompletedTask;
        public Task<string?> GetAccessAsync(CancellationToken c) => Task.FromResult<string?>(null);
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken c) => send(request, c);
    }
    private static readonly PublicConfiguration Config = new("https://example.invalid", "mock", "", "", "", "", "http://localhost:43827/callback/", "Test", "test");
    private static ScreenFrame Frame()
    {
        var scope = Guid.NewGuid(); var geometry = new Geometry(Guid.NewGuid(), scope, new(0,0,800,450),800,450,96,"test",Guid.NewGuid(),new(0,0,1,1),new(0,0,1,1));
        return new(new(Guid.NewGuid(),DateTimeOffset.UtcNow,scope,geometry.Id,800,450,true),geometry,[1],"synthetic");
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task LostResponseRecoversSameTurnWithGetAndNeverResubmitsImage(bool networkFailure)
    {
        var frame = Frame(); var session = Guid.NewGuid(); var requestId = Guid.NewGuid(); var posts=0; var gets=0;
        var result = new TurnResponse(1,session,1,Guid.NewGuid(),frame.Observation.Id,"gemini",null,null,null,false);
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posts++; if (networkFailure) throw new HttpRequestException("Synthetic lost connection");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.GatewayTimeout));
            }
            Assert.Equal($"/v1/sessions/{session}/turns/{requestId}",request.RequestUri!.AbsolutePath);
            Assert.Null(request.Content); gets++;
            return Task.FromResult(gets == 1 ? new(HttpStatusCode.Accepted) : new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(JsonSerializer.Serialize(result, WireJson.Options)) });
        })) { BaseAddress=new Uri(Config.ApiOrigin) };
        using var api = new GuidanceApiClient(Config,new Login(),http);
        var actual = await api.TurnAsync(session,new(1,requestId,0,frame.Observation,"start",null),frame,CancellationToken.None);
        Assert.Equal(result,actual); Assert.Equal(1,posts); Assert.Equal(2,gets);
    }
    [Fact]
    public async Task StopCancelsRecoveryPollingWithoutAnotherUpload()
    {
        using var stop = new CancellationTokenSource(); var posts=0; var gets=0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Post) { posts++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.GatewayTimeout)); }
            gets++; stop.Cancel(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        })) { BaseAddress=new Uri(Config.ApiOrigin) };
        using var api = new GuidanceApiClient(Config,new Login(),http); var frame = Frame();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.TurnAsync(Guid.NewGuid(),new(1,Guid.NewGuid(),0,frame.Observation,"start",null),frame,stop.Token));
        Assert.Equal(1,posts); Assert.Equal(1,gets);
    }
}
