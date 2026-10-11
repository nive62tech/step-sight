using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DesktopGuide.Core;
using DesktopGuide.Platform;

namespace DesktopGuide.App.Services;

public sealed class GuidanceApiClient(PublicConfiguration config, ILoginSession login, HttpClient? transport = null) : IGuidanceApiClient, IBrowserPlanner, IRemoteAgentClient, IDisposable
{
    // Must exceed CloudFront's 60s origin timeout and the API's 35s total provider budget.
    private readonly HttpClient http = transport ?? new() { BaseAddress = new Uri(config.ApiOrigin), Timeout = TimeSpan.FromSeconds(65) };
    private sealed class LostTurnResponse : Exception;
    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellation, bool recoverTurn = false)
    {
        var token = await login.GetAccessAsync(cancellation);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode; response.Dispose();
            if (recoverTurn && status is System.Net.HttpStatusCode.BadGateway or System.Net.HttpStatusCode.GatewayTimeout or System.Net.HttpStatusCode.ServiceUnavailable)
                throw new LostTurnResponse();
            throw new SafetyException(status switch
            {
                System.Net.HttpStatusCode.Conflict => "Session or target changed. Pause and resume to reobserve.",
                System.Net.HttpStatusCode.TooManyRequests => "Usage limit reached. Progress is preserved; continue manually.",
                System.Net.HttpStatusCode.Unauthorized => "Sign-in expired. Local input is disarmed; sign in again.",
                System.Net.HttpStatusCode.BadGateway => "The AI response didn't match the current image. Use Check screen to retry, or Find click target for the current instruction.",
                System.Net.HttpStatusCode.ServiceUnavailable => "Live image analysis timed out or is unavailable. Use Check screen to retry, or Find click target for the current instruction.",
                System.Net.HttpStatusCode.GatewayTimeout => "The gateway timed out waiting for the live image analysis. Use Check screen to retry.",
                System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity => "The screen request was rejected. Stop and start a new guide; report HTTP " + (int)status + " if it repeats.",
                _ => "The service could not process this request (HTTP " + (int)status + "). Pause and retry."
            });
        }
        return response;
    }
    private async Task<string> ReadBounded(HttpResponseMessage response, CancellationToken cancellation)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream(); var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellation)) > 0)
        {
            if (buffer.Length + count > 65536) throw new SafetyException("Server response exceeds contract limit");
            buffer.Write(chunk, 0, count);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
    private async Task<T> Json<T>(HttpMethod method, string path, object? body, CancellationToken cancellation, string? desktopKey = null) where T : IVersioned
    {
        using var request = new HttpRequestMessage(method, path);
        if (desktopKey is not null) request.Headers.Add("X-Desktop-Lease", desktopKey);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body, WireJson.Options), Encoding.UTF8, "application/json");
        using var response = await Send(request, cancellation);
        return WireJson.Read<T>(await ReadBounded(response, cancellation));
    }
    public Task<SessionView> CreateAsync(SessionCreate request, CancellationToken cancellation) => Json<SessionView>(HttpMethod.Post, "/v1/sessions", request, cancellation);
    public Task<RemoteLease> RegisterAsync(RemoteRegistration request, CancellationToken cancellation)
        => Json<RemoteLease>(HttpMethod.Post, "/v1/remote/connections", request, cancellation);
    public Task<RemotePollResponse> PollAsync(RemoteLease lease, RemotePoll request, CancellationToken cancellation)
        => Json<RemotePollResponse>(HttpMethod.Post, $"/v1/remote/connections/{lease.ConnectionId}/poll", request, cancellation, lease.DesktopKey);
    public async Task DisconnectAsync(RemoteLease lease, CancellationToken cancellation)
        => _ = await Json<RemoteOk>(HttpMethod.Delete, $"/v1/remote/connections/{lease.ConnectionId}", null, cancellation, lease.DesktopKey);
    public async Task<BrowserPlanResponse> PlanAsync(string goal, CancellationToken cancellation)
    {
        try { return await Json<BrowserPlanResponse>(HttpMethod.Post, "/v1/browser/plan", new BrowserPlanRequest(1, goal), cancellation); }
        catch (SafetyException) { throw new SafetyException("The agent couldn't plan the starting website. Check your sign-in and connection, then retry."); }
    }
    public Task<SessionView> GetAsync(Guid session, CancellationToken cancellation) => Json<SessionView>(HttpMethod.Get, $"/v1/sessions/{session}", null, cancellation);
    public Task<SessionView> IntentAsync(Guid session, string intent, int version, CancellationToken cancellation)
    {
        if (intent is not ("pause" or "resume" or "stop")) throw new ArgumentException("Invalid intent", nameof(intent));
        return Json<SessionView>(HttpMethod.Post, $"/v1/sessions/{session}/{intent}", new SessionIntent(1, version), cancellation);
    }
    public async Task<TurnResponse> TurnAsync(Guid session, TurnRequest metadata, ScreenFrame frame, CancellationToken cancellation)
    {
        frame.Validate();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/sessions/{session}/turns");
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(JsonSerializer.Serialize(metadata, WireJson.Options), Encoding.UTF8, "application/json"), "metadata");
        var image = new ByteArrayContent(frame.Png); image.Headers.ContentType = new("image/png"); multipart.Add(image, "screenshot", "frame.png");
        request.Content = multipart;
        try
        {
            using var response = await Send(request, cancellation, recoverTurn: true);
            return WireJson.Read<TurnResponse>(await ReadBounded(response, cancellation));
        }
        catch (LostTurnResponse) { return await RecoverTurnAsync(session, metadata.RequestId, cancellation); }
        catch (HttpRequestException) { return await RecoverTurnAsync(session, metadata.RequestId, cancellation); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { return await RecoverTurnAsync(session, metadata.RequestId, cancellation); }
    }
    private async Task<TurnResponse> RecoverTurnAsync(Guid session, Guid requestId, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/sessions/{session}/turns/{requestId}");
                using var response = await Send(request, deadline.Token);
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    return WireJson.Read<TurnResponse>(await ReadBounded(response, deadline.Token));
                if (response.StatusCode != System.Net.HttpStatusCode.Accepted) break;
                await Task.Delay(750, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or SafetyException) { }
        throw new SafetyException("The analysis result could not be recovered. Pause and resume to synchronize, then check the screen again.");
    }
    public async Task<QuestionResponse> QuestionAsync(Guid session, int version, string text, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/sessions/{session}/questions")
        { Content = new StringContent(JsonSerializer.Serialize(new QuestionRequest(1, version, text), WireJson.Options), Encoding.UTF8, "application/json") };
        using var response = await Send(request, cancellation);
        return JsonSerializer.Deserialize<QuestionResponse>(await ReadBounded(response, cancellation), WireJson.Options) ?? throw new SafetyException("Empty answer");
    }
    public async Task EventAsync(Guid session, LearnerEvent value, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/sessions/{session}/events")
        { Content = new StringContent(JsonSerializer.Serialize(value, WireJson.Options), Encoding.UTF8, "application/json") };
        using var response = await Send(request, cancellation);
    }
    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/speech/transcribe") { Content = new ByteArrayContent(wav) };
        request.Content.Headers.ContentType = new("audio/wav"); using var response = await Send(request, cancellation);
        using var document = JsonDocument.Parse(await ReadBounded(response, cancellation));
        return document.RootElement.GetProperty("text").GetString() ?? "";
    }
    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/speech/synthesize")
        { Content = new StringContent(JsonSerializer.Serialize(new { text, language = "en-US" }), Encoding.UTF8, "application/json") };
        using var response = await Send(request, cancellation);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream(); var chunk = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(chunk, cancellation)) > 0)
        { if (buffer.Length + count > 2_000_000) throw new SafetyException("Narration too large"); buffer.Write(chunk, 0, count); }
        return buffer.ToArray();
    }
    public void Dispose() => http.Dispose();
    public async Task DeleteAsync(Guid session, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/v1/sessions/{session}");
        using var response = await Send(request, cancellation);
    }
}
