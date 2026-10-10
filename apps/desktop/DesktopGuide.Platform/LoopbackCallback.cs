using System.Net;
using System.Net.Sockets;
using System.Text;
using DesktopGuide.Core;

namespace DesktopGuide.Platform;

// Uses a user-owned loopback socket; HttpListener URL ACLs and elevation are unnecessary.
public sealed class LoopbackCallback : IDisposable
{
    private readonly TcpListener listener;
    private readonly Uri callback;
    public LoopbackCallback(Uri callback)
    {
        if (!callback.IsLoopback || callback.Scheme != "http" || callback.Port <= 0)
            throw new SafetyException("OAuth callback must be HTTP loopback");
        this.callback = callback;
        listener = new TcpListener(IPAddress.IPv6Loopback, callback.Port);
        // Listen on both loopback families, never on a wildcard interface.
        if (callback.Host == "127.0.0.1" || callback.Host == "localhost") listener = new TcpListener(IPAddress.Loopback, callback.Port);
        listener.Start(4);
    }
    public Task<string> ReceiveCodeAsync(string expectedState, CancellationToken cancellation) => ReceiveAsync(expectedState, null, cancellation);

    // The browser receives success only after the desktop has exchanged and verified the tokens.
    public async Task CompleteSignInAsync(string expectedState, Func<string, CancellationToken, Task> complete, CancellationToken cancellation)
        => await ReceiveAsync(expectedState, complete, cancellation);

    private async Task<string> ReceiveAsync(string expectedState, Func<string, CancellationToken, Task>? complete, CancellationToken cancellation)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
        using var client = await listener.AcceptTcpClientAsync(cancellation);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await using var stream = client.GetStream();
        var bytes = new List<byte>(); var one = new byte[1];
        while (bytes.Count < 8192)
        {
            if (await stream.ReadAsync(one, deadline.Token) == 0) break;
            bytes.Add(one[0]);
            if (bytes.Count >= 4 && bytes[^4] == 13 && bytes[^3] == 10 && bytes[^2] == 13 && bytes[^1] == 10) break;
        }
        var line = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n")[0].Split(' ');
        if (bytes.Count >= 8192 || line.Length != 3 || line[0] != "GET" || !line[1].StartsWith('/') || line[1].StartsWith("//") || line[2] != "HTTP/1.1")
        {
            await RespondAsync(stream, false, "This isn't a valid sign-in request. Try again from Desktop Guide.", cancellation);
            throw new SafetyException("Invalid OAuth callback request");
        }
        var uri = new Uri(callback, line[1]);
        if (uri.AbsolutePath != callback.AbsolutePath)
        {
            await RespondAsync(stream, false, "Return to Desktop Guide to start sign-in.", cancellation);
            continue; // Browser favicon/prefetch requests must not consume the pending OAuth callback.
        }
        try
        {
        var query = new Dictionary<string, string>();
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || !query.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1])))
                throw new SafetyException("Duplicate or invalid OAuth callback parameter");
        }
        if (uri.Host != callback.Host || uri.Port != callback.Port
            || !query.TryGetValue("state", out var state) || state != expectedState)
            throw new SafetyException("Sign-in callback validation failed");
        if (query.ContainsKey("error")) throw new SafetyException("Sign-in was cancelled in your browser. Try again from Desktop Guide.");
        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code) || code.Length > 4096)
            throw new SafetyException("Sign-in callback validation failed");
        if (complete is not null) await complete(code, cancellation);
        await RespondAsync(stream, true, complete is null ? "Return to Desktop Guide to finish sign-in." : "Your account is ready. Return to Desktop Guide to choose your lesson.", cancellation);
        return code;
        }
        catch
        {
            try { await RespondAsync(stream, false, "Sign-in couldn't be completed. Return to Desktop Guide for the next step, then try again.", cancellation); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
            throw;
        }
        }
        throw new SafetyException("Too many unrelated callback requests. Start sign-in again.");
    }
    private static async Task RespondAsync(Stream stream, bool success, string message, CancellationToken cancellation)
    {
        var heading = success ? "Return to your guide." : "Let's try that again.";
        var body = "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Desktop Guide · Sign in</title>"
            + "<style>body{margin:0;background:#F5F4EE;color:#193B34;font:16px 'Segoe UI',sans-serif;display:grid;min-height:100vh;place-items:center}main{max-width:540px;padding:48px}strong{font-size:18px}h1{font:48px Georgia,serif;letter-spacing:-1px;margin:36px 0 18px}p{line-height:1.7;color:#546660}ol{padding-left:22px;line-height:2.2}small{display:block;margin-top:36px;color:#546660;border-top:1px solid #DADFD4;padding-top:20px}</style></head><body><main><strong>Desktop Guide</strong><h1>"
            + heading + "</h1><p>" + message + "</p>"
            + (success ? "<ol><li>Return to the Desktop Guide window.</li><li>Choose what you'd like to learn.</li><li>Preview your selected window, then start.</li></ol>" : "<p>Start a new sign-in from the app. You can close this tab.</p>")
            + "<small>Signing in doesn't start screen observation or grant control.</small></main></body></html>";
        var status = success ? "200 OK" : "400 Bad Request";
        var response = Encoding.UTF8.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; frame-ancestors 'none'\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}");
        await stream.WriteAsync(response, cancellation);
    }
    public void Dispose() => listener.Stop();
}
