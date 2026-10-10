using System.Net;
using System.Net.Http.Headers;

namespace DesktopGuide.App.Services;

public interface IAccountConnection
{
    Task CheckAsync(CancellationToken cancellation);
}

public sealed class CloudConnection(PublicConfiguration config, ILoginSession login) : IAccountConnection
{
    public async Task CheckAsync(CancellationToken cancellation)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(config.ApiOrigin), "/v1/lessons"));
        var token = await login.GetAccessAsync(cancellation);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new SignInException("You're signed in, but lesson access was rejected. Sign out, then sign in again.");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("The lesson service is unavailable. Retry the connection in a moment.");
    }
}
