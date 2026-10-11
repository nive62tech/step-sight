using DesktopGuide.Core;

namespace DesktopGuide.Platform;

public interface IRemoteAgentClient
{
    Task<RemoteLease> RegisterAsync(RemoteRegistration request, CancellationToken cancellation);
    Task<RemotePollResponse> PollAsync(RemoteLease lease, RemotePoll request, CancellationToken cancellation);
    Task DisconnectAsync(RemoteLease lease, CancellationToken cancellation);
}

/// <summary>Volatile local delegation. Restart, disconnect, expiry and Stop revoke it.</summary>
public sealed class RemoteControlBridge(IRemoteAgentClient api, Guid deviceId,
    Func<CancellationToken, Task<RemoteStatus>> readStatus,
    Func<RemoteCommand, CancellationToken, Task<RemoteAcknowledgement>> handle,
    Action<bool, string> changed, TimeSpan? pollInterval = null) : IDisposable
{
    private CancellationTokenSource? lifetime;
    private RemoteLease? lease;
    private bool allowAutonomous;
    public bool Enabled => lifetime is { IsCancellationRequested: false } && lease is not null;
    public bool AllowsAutonomous => Enabled && allowAutonomous;
    public async Task EnableAsync(string label, bool autonomous)
    {
        if (lifetime is not null) throw new SafetyException("Phone control is already connecting or enabled.");
        var run = new CancellationTokenSource(TimeSpan.FromHours(1)); lifetime = run;
        allowAutonomous = autonomous;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            var connected = await api.RegisterAsync(new(1, deviceId, label, autonomous), deadline.Token);
            if (run.IsCancellationRequested || lifetime != run)
            { await ReleaseAsync(connected); run.Dispose(); return; }
            lease = connected;
            changed(true, "Phone control enabled for one hour. Local Stop disconnects it.");
            _ = PumpAsync(run, connected);
        }
        catch
        {
            if (lifetime == run) { lifetime = null; lease = null; }
            run.Cancel(); run.Dispose(); throw;
        }
    }
    private async Task PumpAsync(CancellationTokenSource run, RemoteLease connected)
    {
        var seen = new Dictionary<Guid, RemoteAcknowledgement>();
        RemoteAcknowledgement? acknowledgement = null;
        var endMessage = "Phone control expired. Enable it again to reconnect.";
        try
        {
            while (!run.IsCancellationRequested && DateTimeOffset.UtcNow < connected.ExpiresAt)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(12));
                var status = await readStatus(deadline.Token);
                var response = await api.PollAsync(connected, new(1, status, acknowledgement), deadline.Token);
                run.Token.ThrowIfCancellationRequested();
                if (response.Command is { } command)
                {
                    if (!seen.TryGetValue(command.Id, out acknowledgement))
                    {
                        if (command.ExpiresAt <= DateTimeOffset.UtcNow)
                            acknowledgement = new(command.Id, false, "Expired command; send it again.");
                        else if (command.Kind == "start" && command.Mode == "autonomous" && !allowAutonomous)
                            acknowledgement = new(command.Id, false, "Autonomous phone tasks are not allowed on this PC.");
                        else if (command.Kind is not ("start" or "pause" or "resume" or "stop" or "check"))
                            acknowledgement = new(command.Id, false, "Unsupported remote command.");
                        else
                            acknowledgement = await handle(command, run.Token);
                        seen.Add(command.Id, acknowledgement);
                        // A connection has a bounded one-hour life and at most 120 requests/minute.
                        // Never evict an ID and later execute a duplicate under the same lease.
                        if (seen.Count >= 500) break;
                    }
                }
                await Task.Delay(pollInterval ?? TimeSpan.FromSeconds(2), run.Token);
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested) { }
        catch (OperationCanceledException)
        { endMessage = "Phone connection timed out. The task is paused; enable phone control again on this PC."; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { endMessage = "Phone connection lost. The task is paused; enable phone control again on this PC."; }
        finally
        {
            if (lifetime == run) { lifetime = null; lease = null; run.Cancel(); changed(false, endMessage); }
            await ReleaseAsync(connected); run.Dispose();
        }
    }
    private async Task ReleaseAsync(RemoteLease connected)
    {
        try { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await api.DisconnectAsync(connected, deadline.Token); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }
    public void Disable()
    {
        var prior = lifetime; lifetime = null; lease = null; allowAutonomous = false;
        prior?.Cancel();
        changed(false, "Phone control is off.");
    }
    public void Dispose() => Disable();
}
