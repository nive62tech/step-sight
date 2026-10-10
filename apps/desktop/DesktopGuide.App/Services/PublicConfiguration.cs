using System.Text.Json;

namespace DesktopGuide.App.Services;

public sealed record PublicConfiguration(string ApiOrigin, string AuthMode, string CognitoIssuer,
    string CognitoClientId, string CognitoDomain, string Scopes, string CallbackUri,
    string BrandName, string BuildLabel)
{
    public static PublicConfiguration Load(string? path = null)
    {
        var value = JsonSerializer.Deserialize<PublicConfiguration>(File.ReadAllText(path ?? Path.Combine(AppContext.BaseDirectory, "desktop-config.json")))
                    ?? throw new InvalidOperationException("Public configuration missing");
        var origin = new Uri(value.ApiOrigin);
        if (origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query)) throw new InvalidOperationException("Invalid API origin");
        if (value.AuthMode == "mock")
        {
            if (!origin.IsLoopback) throw new InvalidOperationException("Mock API must be loopback only");
        }
        else if (value.AuthMode != "cognito" || origin.Scheme != "https" || !value.CognitoIssuer.StartsWith("https://cognito-idp.", StringComparison.Ordinal)
                 || !value.CognitoDomain.StartsWith("https://", StringComparison.Ordinal) || string.IsNullOrEmpty(value.CognitoClientId))
            throw new InvalidOperationException("Cloud configuration requires HTTPS and Cognito public identifiers");
        var callback = new Uri(value.CallbackUri);
        if (!callback.IsLoopback || callback.Scheme != "http" || !value.CallbackUri.EndsWith('/')) throw new InvalidOperationException("Only registered loopback callback is supported");
        return value;
    }
}
