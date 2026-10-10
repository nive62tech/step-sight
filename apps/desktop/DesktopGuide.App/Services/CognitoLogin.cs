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

public sealed class CognitoLogin : IDisposable
{
    private readonly PublicConfiguration config;
    private readonly HttpClient http;
    private readonly string tokenPath;
    public CognitoLogin(PublicConfiguration config) : this(config, new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, null,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopGuide", "refresh-token.bin")) { }
    internal CognitoLogin(PublicConfiguration config, HttpClient http, string? initialRefresh, string tokenPath)
    { this.config = config; this.http = http; refresh = initialRefresh; this.tokenPath = tokenPath; }
    private readonly SemaphoreSlim tokenGate = new(1);
    private string? access, refresh;
    private DateTimeOffset expires;
    private int credentialEpoch;
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
    private string TokenPath => tokenPath;
    private static string Random() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public async Task SignInAsync(CancellationToken cancellation)
    {
        if (config.AuthMode == "mock") return;
        var epoch = Interlocked.Increment(ref credentialEpoch);
        var verifier = Random(); var state = Random(); var nonce = Random();
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code", ["client_id"] = config.CognitoClientId,
            ["redirect_uri"] = config.CallbackUri, ["scope"] = config.Scopes,
            ["state"] = state, ["nonce"] = nonce, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256"
        };
        using var listener = new LoopbackCallback(new Uri(config.CallbackUri));
        Process.Start(new ProcessStartInfo(config.CognitoDomain + "/oauth2/authorize?" + string.Join('&', query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)))) { UseShellExecute = true });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var code = await listener.ReceiveCodeAsync(state, deadline.Token);
            using var response = await http.PostAsync(config.CognitoDomain + "/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["client_id"] = config.CognitoClientId,
                ["code"] = code, ["redirect_uri"] = config.CallbackUri, ["code_verifier"] = verifier
            }), cancellation);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var manager = new ConfigurationManager<OpenIdConnectConfiguration>(config.CognitoIssuer + "/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever());
            var discovery = await manager.GetConfigurationAsync(cancellation);
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(document.RootElement.GetProperty("id_token").GetString(), new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = config.CognitoIssuer,
                ValidateAudience = true, ValidAudience = config.CognitoClientId,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidateIssuerSigningKey = true, IssuerSigningKeys = discovery.SigningKeys,
                ValidAlgorithms = ["RS256"], ClockSkew = TimeSpan.FromSeconds(15)
            }, out _);
            if (principal.FindFirst("nonce")?.Value != nonce) throw new InvalidOperationException("Sign-in nonce mismatch");
            if (epoch != Volatile.Read(ref credentialEpoch)) throw new OperationCanceledException("Sign-in was superseded");
            await tokenGate.WaitAsync(cancellation);
            try
            {
                if (epoch != Volatile.Read(ref credentialEpoch)) throw new OperationCanceledException("Sign-in was superseded");
                AcceptTokens(document.RootElement);
            }
            finally { tokenGate.Release(); }
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
            if (refresh is null) throw new InvalidOperationException("Sign in before starting a cloud lesson");
            var epoch = Volatile.Read(ref credentialEpoch);
            using var response = await http.PostAsync(config.CognitoDomain + "/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["grant_type"] = "refresh_token", ["client_id"] = config.CognitoClientId, ["refresh_token"] = refresh }), cancellation);
            if (epoch != Volatile.Read(ref credentialEpoch)) throw new OperationCanceledException("Credential refresh was superseded");
            if (!response.IsSuccessStatusCode) { ClearTokens(); throw new InvalidOperationException("Sign-in expired; sign in again"); }
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation)); AcceptTokens(document.RootElement);
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
        finally { tokenGate.Release(); }
    }
    private void ClearTokens()
    {
        access = refresh = null; expires = default;
        lock (storageGate) { if (File.Exists(TokenPath)) File.Delete(TokenPath); }
    }
    public void Dispose() { access = refresh = null; http.Dispose(); tokenGate.Dispose(); }
}
