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
    public async Task<string> ReceiveCodeAsync(string expectedState, CancellationToken cancellation)
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
        if (bytes.Count >= 8192 || line.Length != 3 || line[0] != "GET" || !line[1].StartsWith('/') || line[2] != "HTTP/1.1")
            throw new SafetyException("Invalid OAuth callback request");
        var uri = new Uri(callback, line[1]);
        var query = new Dictionary<string, string>();
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || !query.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1])))
                throw new SafetyException("Duplicate or invalid OAuth callback parameter");
        }
        if (uri.Host != callback.Host || uri.Port != callback.Port || uri.AbsolutePath != callback.AbsolutePath
            || !query.TryGetValue("state", out var state) || state != expectedState
            || !query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code) || code.Length > 4096)
            throw new SafetyException("Sign-in callback validation failed");
        var body = "Return to Desktop Guide to finish sign-in. This tab can be closed.";
        var response = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'\r\nConnection: close\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}");
        await stream.WriteAsync(response, deadline.Token);
        return code;
    }
    public void Dispose() => listener.Stop();
}
