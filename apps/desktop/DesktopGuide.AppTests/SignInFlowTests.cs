using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopGuide.App.Services;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace DesktopGuide.AppTests;

public sealed class SignInFlowTests
{
    private sealed class TokenHandler(Func<string> token) : HttpMessageHandler
    {
        public bool Reject;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Assert.Equal("/oauth2/token", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(Reject ? HttpStatusCode.BadRequest : HttpStatusCode.OK) {
                Content = new StringContent(JsonSerializer.Serialize(new {access_token="synthetic-access", refresh_token="synthetic-refresh", expires_in=3600, id_token=token()}))
            });
        }
    }
    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); return port;
    }
    private static Dictionary<string,string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&').Select(p=>p.Split('=',2)).ToDictionary(p=>p[0],p=>Uri.UnescapeDataString(p[1]));
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task BrowserSuccessRequiresVerifiedTokenAndRaisesAccountChange(bool validNonce, bool rejectExchange)
    {
        using var rsa = RSA.Create(2048); var key = new RsaSecurityKey(rsa) { KeyId="synthetic-key" };
        var port = FreePort();
        var config = new PublicConfiguration("https://example.invalid","cognito","https://cognito-idp.ap-south-1.amazonaws.com/test","client","https://example.invalid",
            "openid desktop-guide/lessons",$"http://127.0.0.1:{port}/callback/","test","test");
        var opened = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        string nonce = "";
        string Token() => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(config.CognitoIssuer, config.CognitoClientId,
            [new Claim("sub","synthetic-user"),new Claim("email","learner@example.invalid"), new Claim("nonce",validNonce ? nonce : "wrong"),new Claim("token_use","id")],
            DateTime.UtcNow.AddSeconds(-5),DateTime.UtcNow.AddMinutes(5),new SigningCredentials(key,SecurityAlgorithms.RsaSha256)));
        using var handler = new TokenHandler(Token) { Reject=rejectExchange };
        var discovery = new OpenIdConnectConfiguration(); discovery.SigningKeys.Add(key);
        var path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".bin");
        using var login = new CognitoLogin(config,new HttpClient(handler),null,path,uri=>opened.SetResult(uri),_=>Task.FromResult(discovery));
        var changed = 0; login.Changed += () => changed++;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var signIn = login.SignInAsync(timeout.Token);
        var authorization = Query(await opened.Task.WaitAsync(timeout.Token)); nonce = authorization["nonce"];
        Assert.Equal("S256",authorization["code_challenge_method"]); Assert.Equal("code",authorization["response_type"]);
        using var browser = new HttpClient();
        var callback = browser.GetAsync(config.CallbackUri+"?state="+Uri.EscapeDataString(authorization["state"])+"&code=synthetic-code",timeout.Token);
        if (validNonce && !rejectExchange)
        {
            await signIn; using var response = await callback;
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            Assert.Contains("Your account is ready",await response.Content.ReadAsStringAsync());
            Assert.True(login.SignedIn); Assert.Equal("learner@example.invalid",login.AccountName); Assert.Equal(1,changed);
            Assert.Equal("synthetic-access",await login.GetAccessAsync(timeout.Token));
        }
        else
        {
            await Assert.ThrowsAsync<SignInException>(()=>signIn); using var response = await callback;
            Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
            Assert.Contains("Sign-in couldn't be completed",await response.Content.ReadAsStringAsync());
            Assert.False(login.SignedIn); Assert.Equal(0,changed);
        }
        Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task OccupiedCallbackPortHasActionableError()
    {
        var listener = new TcpListener(IPAddress.Loopback,0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var config = new PublicConfiguration("https://example.invalid","cognito","https://cognito-idp.ap-south-1.amazonaws.com/test","client","https://example.invalid",
            "openid",$"http://127.0.0.1:{port}/callback/","test","test");
        try
        {
            using var login = new CognitoLogin(config,new HttpClient(),null,Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".bin"),_=>Assert.Fail("Browser must not open"));
            var error = await Assert.ThrowsAsync<SignInException>(()=>login.SignInAsync(CancellationToken.None));
            Assert.Contains("Close other Desktop Guide instances",error.Message); Assert.False(login.SignedIn);
        }
        finally { listener.Stop(); }
    }
}

