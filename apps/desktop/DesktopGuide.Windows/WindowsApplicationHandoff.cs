using System.Diagnostics;
using DesktopGuide.Core;
using DesktopGuide.Platform;
using Windows.Management.Deployment;

namespace DesktopGuide.Windows;

// Launch only the installed, known WhatsApp package, with no URL, recipient,
// message, command line or executable supplied by the model.
public sealed class WindowsApplicationHandoff(PermissionSupervisor permissions, WindowsCapture capture,
    IUserActivityObserver activity) : IApplicationHandoff
{
    private const string Family = "5319275A.WhatsAppDesktop_cv1g1gvanyjgm";
    private static global::Windows.ApplicationModel.Package? Package()
    {
        try { return new PackageManager().FindPackagesForUser("").SingleOrDefault(p => p.Id.FamilyName == Family); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return null; }
    }
    public IReadOnlyList<string> SupportedApplications => Package() is null ? [] : ["whatsapp"];

    public async Task<ApplicationHandoffResult> OpenAsync(LocalCapability capability, ActionProposal proposal, ScreenFrame frame,
        CaptureSource source, CancellationToken cancellation)
    {
        proposal.Action.Validate();
        if (proposal.Action.Kind != "open_app" || proposal.Action.Application != "whatsapp")
            throw new SafetyException("Unsupported application handoff");
        var package = Package() ?? throw new SafetyException("WhatsApp Desktop is not installed. Install it yourself, then resume.");
        var root = Path.GetFullPath(package.InstalledLocation.Path) + Path.DirectorySeparatorChar;
        var entries = await package.GetAppListEntriesAsync();
        var entry = entries.SingleOrDefault(e => e.AppUserModelId == Family + "!App")
            ?? throw new SafetyException("The installed WhatsApp launch identity is unavailable.");
        cancellation.ThrowIfCancellationRequested();
        var fresh = await capture.CaptureAsync(source, frame.PrivacyMasks, cancellation);
        if (!capture.IsActionTargetCurrent(frame, fresh, proposal.Action)) throw new SafetyException("Screen changed before application handoff");
        var oldWindow = capture.InspectionWindow(source,frame.Geometry);
        if (oldWindow == 0) throw new SafetyException("Starting application is no longer focused");
        var epoch = permissions.Epoch;
        var lease = permissions.Consume(capability, proposal, frame, capture.IsForegroundAndVisible(source));
        Task<bool>? launch = null;
        permissions.ExecuteBatch(lease, frame, capture.IsForegroundAndVisible(source), () =>
        {
            if (!activity.TrackingAvailable || capture.IsLocked) throw new SafetyException("Application launch is disarmed");
            launch = entry.LaunchAsync().AsTask(cancellation);
        });
        if (!await launch!) throw new SafetyException("Windows could not open WhatsApp Desktop.");
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            cancellation.ThrowIfCancellationRequested();
            if (permissions.Epoch != epoch || !activity.TrackingAvailable || capture.IsLocked)
                throw new SafetyException("Application handoff was interrupted");
            var windows = new List<(nint Handle, uint Pid)>();
            Native.EnumWindows((window, _) =>
            {
                if (!Native.IsWindowVisible(window) || Native.IsIconic(window)) return true;
                Native.GetWindowThreadProcessId(window, out var pid);
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    var path = process.MainModule?.FileName;
                    if (path is not null && Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase)
                        && Native.GetWindowRect(window, out var rect) && rect.Right-rect.Left >= 300 && rect.Bottom-rect.Top >= 200)
                        windows.Add((window,pid));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException) { }
                return true;
            },0);
            var foreground = Native.GetForegroundWindow();
            var selected = windows.Where(w => w.Handle == foreground).ToArray();
            if (selected.Length == 0 && windows.Count == 1)
            {
                cancellation.ThrowIfCancellationRequested();
                permissions.ExecuteBatch(lease,frame,foreground == oldWindow,() => Native.SetForegroundWindow(windows[0].Handle));
                await Task.Delay(150,cancellation); continue;
            }
            if (selected.Length == 1)
            {
                cancellation.ThrowIfCancellationRequested();
                return new(capture.RegisterLaunchedWindow(selected[0].Handle,selected[0].Pid,"WhatsApp Desktop"),lease);
            }
            if (windows.Count > 1) throw new SafetyException("More than one WhatsApp window is open. Select the intended one yourself.");
            await Task.Delay(250,cancellation);
        }
        throw new SafetyException("WhatsApp opened without a focused task window. Bring it forward and resume.");
    }
}
