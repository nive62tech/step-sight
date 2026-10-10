using System.Net;
using DesktopGuide.App.Services;
using Xunit;

namespace DesktopGuide.AppTests;

public sealed class CredentialTests
{
    private static PublicConfiguration Config => new("https://example.invalid", "cognito", "https://cognito-idp.ap-south-1.amazonaws.com/ap-south-1_Test", "client", "https://example.invalid", "openid desktop-guide/lessons", "http://localhost:43827/callback/", "test", "test");
    private sealed class Handler(bool valid) : HttpMessageHandler
    {
        public int Calls;
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Calls++; Body = await request.Content!.ReadAsStringAsync(cancellation);
            Assert.Equal("/oauth2/token", request.RequestUri!.AbsolutePath);
            return new(valid ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
            { Content = new StringContent("{\"access_token\":\"synthetic-access\",\"expires_in\":3600}") };
        }
    }
    [Fact]
    public async Task RefreshUsesPublicClientAndCachesAccessInMemory()
    {
        var handler = new Handler(true);
        using var login = new CognitoLogin(Config, new HttpClient(handler), "synthetic-refresh", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin"));
        Assert.Equal("synthetic-access", await login.GetAccessAsync(CancellationToken.None));
        Assert.Equal("synthetic-access", await login.GetAccessAsync(CancellationToken.None));
        Assert.Equal(1, handler.Calls); Assert.Contains("grant_type=refresh_token", handler.Body); Assert.DoesNotContain("client_secret", handler.Body);
    }
    [Fact]
    public async Task RejectedRefreshClearsAccessAndRequiresSignIn()
    {
        var handler = new Handler(false);
        using var login = new CognitoLogin(Config, new HttpClient(handler), "synthetic-refresh", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin"));
        await Assert.ThrowsAsync<SignInException>(() => login.GetAccessAsync(CancellationToken.None));
        Assert.False(login.SignedIn);
        await Assert.ThrowsAsync<SignInException>(() => login.GetAccessAsync(CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task MissingCredentialsNeverCallTokenEndpoint()
    {
        var handler = new Handler(true);
        using var login = new CognitoLogin(Config, new HttpClient(handler), null, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin"));
        await Assert.ThrowsAsync<SignInException>(() => login.GetAccessAsync(CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public async Task RemovingRememberConsentDeletesStoredTokenAndKeepsCurrentLogin()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            using var login = new CognitoLogin(Config, new HttpClient(new Handler(true)), "synthetic-refresh", path);
            login.RememberMe = true;
            await login.GetAccessAsync(CancellationToken.None);
            Assert.True(File.Exists(path));
            login.RememberMe = false;
            Assert.False(File.Exists(path));
            Assert.True(login.SignedIn);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
