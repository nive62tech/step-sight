using System.Threading.Channels;
using DesktopGuide.Core;
using DesktopGuide.Platform;
using Xunit;

namespace DesktopGuide.Tests;

public sealed class RemoteControlTests
{
    private sealed class Api : IRemoteAgentClient
    {
        public Channel<RemoteCommand?> Responses = Channel.CreateUnbounded<RemoteCommand?>();
        public Channel<RemotePoll> Requests = Channel.CreateUnbounded<RemotePoll>();
        public TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IgnoreCancellation;
        public Task<RemoteLease> RegisterAsync(RemoteRegistration r, CancellationToken c) => Task.FromResult(new RemoteLease(1, Guid.NewGuid(), "synthetic", DateTimeOffset.UtcNow.AddHours(1)));
        public async Task<RemotePollResponse> PollAsync(RemoteLease l, RemotePoll r, CancellationToken c)
        { await Requests.Writer.WriteAsync(r, c); return new(1, await Responses.Reader.ReadAsync(IgnoreCancellation ? CancellationToken.None : c)); }
        public Task DisconnectAsync(RemoteLease l, CancellationToken c) { Released.TrySetResult(); return Task.CompletedTask; }
    }
    private static RemoteCommand Command(string mode = "guide") => new(Guid.NewGuid(), "start", mode, "Synthetic task", DateTimeOffset.UtcNow.AddSeconds(30));
    private static Task<RemoteStatus> Status(CancellationToken c) => Task.FromResult(new RemoteStatus("Idle", "guide", false, "", "", false, false));

    [Fact] public async Task RetriedDeliveryRunsOnceAndAcknowledgesOriginalResult()
    {
        var api = new Api(); var calls = 0;
        using var bridge = new RemoteControlBridge(api, Guid.NewGuid(), Status,
            (cmd, c) => { calls++; return Task.FromResult(new RemoteAcknowledgement(cmd.Id, true, "Received")); }, (_, _) => { }, TimeSpan.FromMilliseconds(1));
        await bridge.EnableAsync("PC", false); var cmd = Command();
        await api.Requests.Reader.ReadAsync(); await api.Responses.Writer.WriteAsync(cmd);
        var second = await api.Requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(second.Acknowledgement!.Accepted); await api.Responses.Writer.WriteAsync(cmd);
        var third = await api.Requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(second.Acknowledgement, third.Acknowledgement); Assert.Equal(1, calls);
        bridge.Disable(); await api.Released.Task.WaitAsync(TimeSpan.FromSeconds(2)); Assert.False(bridge.Enabled);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task DisconnectedOrUnauthorizedAutonomousCommandNeverRuns(bool disconnected)
    {
        var api = new Api { IgnoreCancellation = disconnected }; var calls = 0;
        using var bridge = new RemoteControlBridge(api, Guid.NewGuid(), Status,
            (cmd, c) => { calls++; return Task.FromResult(new RemoteAcknowledgement(cmd.Id, true, "Received")); }, (_, _) => { }, TimeSpan.FromMilliseconds(1));
        await bridge.EnableAsync("PC", false); await api.Requests.Reader.ReadAsync();
        if (disconnected) bridge.Disable();
        await api.Responses.Writer.WriteAsync(Command("autonomous"));
        if (!disconnected)
        {
            var result = await api.Requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(result.Acknowledgement!.Accepted); bridge.Disable();
        }
        await api.Released.Task.WaitAsync(TimeSpan.FromSeconds(2)); Assert.Equal(0, calls);
    }
}
