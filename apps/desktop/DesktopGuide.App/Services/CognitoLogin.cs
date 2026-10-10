using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using DesktopGuide.Platform;

namespace DesktopGuide.App.Services;

public sealed class CognitoLogin : ILoginSession, IDisposable
{
    private readonly PublicConfiguration config;
    private readonly HttpClient http;
    private readonly string tokenPath;
    private readonly Action<Uri> openBrowser;
    private readonly Func<CancellationToken, Task<OpenIdConnectConfiguration>> discover;
    public CognitoLogin(PublicConfiguration config) : this(config, new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, null,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopGuide", "refresh-token.bin")) { }
    internal CognitoLogin(PublicConfiguration config, HttpClient http, string? initialRefresh, string tokenPath,
        Action<Uri>? openBrowser = null, Func<CancellationToken, Task<OpenIdConnectConfiguration>>? discover = null)
    {
        this.config = config; this.http = http; refresh = initialRefresh; this.tokenPath = tokenPath;
        this.openBrowser = openBrowser ?? (uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        this.discover = discover ?? (cancellation => new ConfigurationManager<OpenIdConnectConfiguration>(
            config.CognitoIssuer + "/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever()).GetConfigurationAsync(cancellation));
    }
    private readonly SemaphoreSlim tokenGate = new(1);
    private string? access, refresh;
    private DateTimeOffset expires;
    private int credentialEpoch;
    private int signInRunning;
    private bool rememberMe;
    private readonly object storageGate = new();
    public bool RememberMe
    {
        get { lock (storageGate) return rememberMe; }
        set
        {
            lock (storageGate)
            {
                rememberMe = value;
                if (!value && File.Exists(TokenPath)) File.Delete(TokenPath);
            }
        }
    }
    public bool SignedIn => config.AuthMode == "mock" || access is not null && expires > DateTimeOffset.UtcNow;
    public bool IsMock => config.AuthMode == "mock";
    public string? AccountName { get; private set; }
    public event Action? Changed;
    private string TokenPath => tokenPath;
    private static string Random() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public async Task SignInAsync(CancellationToken cancellation)
    {
        if (IsMock) { Changed?.Invoke(); return; }
        if (Interlocked.CompareExchange(ref signInRunning, 1, 0) != 0)
            throw new SignInException("Sign-in is already open. Finish it in your browser or cancel before trying again.");
        var epoch = Interlocked.Increment(ref credentialEpoch);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
        var verifier = Random(); var state = Random(); var nonce = Random();
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code", ["client_id"] = config.CognitoClientId,
            ["redirect_uri"] = config.CallbackUri, ["scope"] = config.Scopes,
            ["state"] = state, ["nonce"] = nonce, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256"
        };
        using var listener = new LoopbackCallback(new Uri(config.CallbackUri));
        openBrowser(new Uri(config.CognitoDomain + "/oauth2/authorize?" + string.Join('&', query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)))));
        await listener.CompleteSignInAsync(state, async (code, token) =>
        {
            using var response = await http.PostAsync(config.CognitoDomain + "/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["client_id"] = config.CognitoClientId,
                ["code"] = code, ["redirect_uri"] = config.CallbackUri, ["code_verifier"] = verifier
            }), token);
            if (!response.IsSuccessStatusCode) throw new SignInException("The sign-in link expired or was already used. Start sign-in again from Desktop Guide.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var discovery = await discover(token);
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(document.RootElement.GetProperty("id_token").GetString(), new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = config.CognitoIssuer,
                ValidateAudience = true, ValidAudience = config.CognitoClientId,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidateIssuerSigningKey = true, IssuerSigningKeys = discovery.SigningKeys,
                ValidAlgorithms = ["RS256"], ClockSkew = TimeSpan.FromSeconds(15)
            }, out _);
            if (principal.FindFirst("nonce")?.Value != nonce || principal.FindFirst("token_use")?.Value != "id"
                || string.IsNullOrWhiteSpace(principal.FindFirst("sub")?.Value))
                throw new SignInException("The sign-in response could not be verified. Start sign-in again from Desktop Guide.");
            if (epoch != Volatile.Read(ref credentialEpoch)) throw new OperationCanceledException("Sign-in was superseded");
            await tokenGate.WaitAsync(token);
            try
            {
                if (epoch != Volatile.Read(ref credentialEpoch)) throw new OperationCanceledException("Sign-in was superseded");
                AcceptTokens(document.RootElement);
                AccountName = principal.FindFirst("email")?.Value;
            }
            finally { tokenGate.Release(); }
        }, deadline.Token);
        Changed?.Invoke();
        }
        catch (System.Net.Sockets.SocketException ex)
        { throw new SignInException("The sign-in callback port is in use. Close other Desktop Guide instances, then try again.", ex); }
        catch (OperationCanceledException ex) when (!cancellation.IsCancellationRequested)
        { throw new SignInException("Sign-in timed out after three minutes. Click Sign in to try again.", ex); }
        catch (HttpRequestException ex)
        { throw new SignInException("Couldn't reach the sign-in service. Check your connection and try again.", ex); }
        catch (SecurityTokenException ex)
        { throw new SignInException("The sign-in response could not be verified. Start sign-in again from Desktop Guide.", ex); }
        finally { Volatile.Write(ref signInRunning, 0); }
    }
    private void AcceptTokens(JsonElement json)
    {
        var incoming = json.GetProperty("access_token").GetString();
        var lifetime = json.GetProperty("expires_in").GetInt32();
        if (string.IsNullOrWhiteSpace(incoming) || incoming.Length > 16000 || lifetime is < 1 or > 86400)
            throw new InvalidOperationException("Invalid token response");
        access = incoming; expires = DateTimeOffset.UtcNow.AddSeconds(lifetime);
        if (json.TryGetProperty("refresh_token", out var value)) refresh = value.GetString();
        lock (storageGate)
        {
          if (rememberMe && refresh is not null)
          {
            Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
            File.WriteAllBytes(TokenPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(refresh), null, DataProtectionScope.CurrentUser));
          }
        }
    }
    public async Task<string?> GetAccessAsync(CancellationToken cancellation)
    {
        if (config.AuthMode == "mock") return null;
        await tokenGate.WaitAsync(cancellation);
        try
        {
            if (access is not null && expires > DateTimeOffset.UtcNow.AddSeconds(60)) return access;
            if (RememberMe && refresh is null && File.Exists(TokenPath))
                refresh = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(TokenPath), null, DataProtectionScope.CurrentUser));
            if (refresh is null) throw new SignInException("Sign in before starting a cloud lesson.");
            var epoch = Volatile.Read(ref credentialEpoch);
            using var response = await http.PostAsync(config.CognitoDomain + "/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["grant_type"] = "refresh_token", ["client_id"] = config.CognitoClientId, ["refresh_token"] = refresh }), cancellation);
            if (epoch != Volatile.Read(ref credentialEpoch)) throw new OperationCanceledException("Credential refresh was superseded");
            if (!response.IsSuccessStatusCode) { ClearTokens(); Changed?.Invoke(); throw new SignInException("Your sign-in expired. Sign in again to continue."); }
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation)); AcceptTokens(document.RootElement);
            Changed?.Invoke();
            return access;
        }
        finally { tokenGate.Release(); }
    }
    public async Task SignOutAsync()
    {
        Interlocked.Increment(ref credentialEpoch);
        await tokenGate.WaitAsync();
        try
        {
        var previousRefresh = refresh; ClearTokens();
        if (previousRefresh is null) return;
        try
        {
            using var response = await http.PostAsync(config.CognitoDomain + "/oauth2/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["client_id"] = config.CognitoClientId, ["token"] = previousRefresh }));
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        }
        finally { tokenGate.Release(); Changed?.Invoke(); }
    }
    private void ClearTokens()
    {
        access = refresh = null; expires = default; AccountName = null;
        lock (storageGate) { if (File.Exists(TokenPath)) File.Delete(TokenPath); }
    }
    public void Dispose() { access = refresh = null; http.Dispose(); tokenGate.Dispose(); }
}
