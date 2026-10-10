using System.Net;
using System.Net.Sockets;
using System.Text;
using DesktopGuide.Core;
using DesktopGuide.Platform;
using Xunit;

namespace DesktopGuide.Tests;

public sealed class LoopbackTests
{
    [Theory]
    [InlineData("state=correct&code=synthetic-code", true)]
    [InlineData("state=wrong&code=synthetic-code", false)]
    [InlineData("state=correct&state=correct&code=synthetic-code", false)]
    [InlineData("state=correct&error=access_denied", false)]
    public async Task CallbackBindsStateAndRejectsDuplicateParameters(string query, bool valid)
    {
        var portProbe = new TcpListener(IPAddress.Loopback, 0); portProbe.Start();
        int port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
        using var callback = new LoopbackCallback(new Uri($"http://127.0.0.1:{port}/callback/"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var receive = callback.ReceiveCodeAsync("correct", deadline.Token);
        using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"GET /callback/?{query} HTTP/1.1\r\nHost: localhost\r\n\r\n"), deadline.Token);
        if (valid) Assert.Equal("synthetic-code", await receive);
        else await Assert.ThrowsAsync<SafetyException>(() => receive);
    }
    [Fact]
    public void MasksRejectLocalInputTargets()
    {
        var scope = Guid.NewGuid(); var geometry = new Geometry(Guid.NewGuid(), scope, new(0, 0, 100, 100), 100, 100, 96, "test", Guid.NewGuid(), new(0, 0, 1, 1), new(0, 0, 1, 1));
        var frame = new ScreenFrame(new(Guid.NewGuid(), DateTimeOffset.UtcNow, scope, geometry.Id, 100, 100, true), geometry, [1], "test") { PrivacyMasks = [new(.2, .2, .4, .4)] };
        Assert.Throws<SafetyException>(() => frame.RequireUnmaskedPoint(.3, .3));
        frame.RequireUnmaskedPoint(.6, .6);
    }
}
