using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DesktopGuide.Core;
using DesktopGuide.Platform;

namespace DesktopGuide.App.Services;

public sealed class GuidanceApiClient(PublicConfiguration config, CognitoLogin login) : IGuidanceApiClient, IDisposable
{
    private readonly HttpClient http = new() { BaseAddress = new Uri(config.ApiOrigin), Timeout = TimeSpan.FromSeconds(45) };
    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellation)
    {
        var token = await login.GetAccessAsync(cancellation);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode; response.Dispose();
            throw new HttpRequestException(status switch
            {
                System.Net.HttpStatusCode.Conflict => "Session or target changed. Pause and resume to reobserve.",
                System.Net.HttpStatusCode.TooManyRequests => "Usage limit reached. Progress is preserved; continue manually.",
                System.Net.HttpStatusCode.Unauthorized => "Sign-in expired. Local input is disarmed; sign in again.",
                _ => "The service could not process this request. Pause and check configuration."
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
    private async Task<T> Json<T>(HttpMethod method, string path, object? body, CancellationToken cancellation) where T : IVersioned
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body, WireJson.Options), Encoding.UTF8, "application/json");
        using var response = await Send(request, cancellation);
        return WireJson.Read<T>(await ReadBounded(response, cancellation));
    }
    public Task<SessionView> CreateAsync(SessionCreate request, CancellationToken cancellation) => Json<SessionView>(HttpMethod.Post, "/v1/sessions", request, cancellation);
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
        using var response = await Send(request, cancellation);
        return WireJson.Read<TurnResponse>(await ReadBounded(response, cancellation));
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
