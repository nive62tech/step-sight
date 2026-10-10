using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using DesktopGuide.Core;
using DesktopGuide.Platform;

namespace DesktopGuide.Windows;

public sealed class WindowsCapture : IScreenCapture, IWindowTracker
{
    private readonly Dictionary<Guid, (nint Handle, bool Monitor, uint ProcessId)> sources = [];
    private readonly Dictionary<nint, Guid> targetIds = [];
    private readonly Dictionary<Guid, Geometry> geometries = [];
    private readonly HashSet<nint> ownTestWindows = [];
    private readonly SemaphoreSlim captureGate = new(1);
    private readonly WgcCapture wgc = new();
    public int CaptureSessionStarts => wgc.SessionStarts;
    public bool CaptureSessionActive => wgc.Active;
    public bool IsLocked
    {
        get
        {
            var desktop = Native.OpenInputDesktop(0, false, 1);
            if (desktop == 0) return true;
            Native.CloseDesktop(desktop); return false;
        }
    }

    public IReadOnlyList<CaptureSource> ListSources()
    {
        StopCapture();
        sources.Clear(); geometries.Clear();
        var result = new List<CaptureSource>();
        Native.EnumDisplayMonitors(0, 0, (nint monitor, nint _, ref Native.Rect rect, nint parameter) =>
        {
            var scope = Guid.NewGuid(); var target = TargetId(monitor);
            sources[scope] = (monitor, true, 0);
            result.Add(new(scope, target, $"Monitor {result.Count + 1} — {rect.Right - rect.Left}×{rect.Bottom - rect.Top} (GDI monitor capture)", "monitor"));
            return true;
        }, 0);
        if (WgcCapture.Supported)
            Native.EnumWindows((hwnd, _) =>
            {
                Native.GetWindowThreadProcessId(hwnd, out var pid);
                var title = new StringBuilder(512); Native.GetWindowText(hwnd, title, title.Capacity);
                if (pid == Environment.ProcessId || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) || title.Length == 0) return true;
                var scope = Guid.NewGuid(); sources[scope] = (hwnd, false, pid);
                result.Add(new(scope, TargetId(hwnd), title.ToString() + " (isolated WGC window)", "window"));
                return true;
            }, 0);
        return result;
    }

    private Guid TargetId(nint handle)
    {
        if (!targetIds.TryGetValue(handle, out var id)) targetIds[handle] = id = Guid.NewGuid();
        return id;
    }
    public CaptureSource RegisterLaunchedWindow(nint handle, uint expectedProcessId, string label)
    {
        Native.GetWindowThreadProcessId(handle, out var pid);
        if (pid != expectedProcessId || pid == Environment.ProcessId || !Native.IsWindow(handle)
            || !Native.IsWindowVisible(handle) || Native.IsIconic(handle) || !WgcCapture.Supported)
            throw new SafetyException("The opened browser window is no longer available. Try opening it again.");
        var scope = Guid.NewGuid(); sources[scope] = (handle, false, pid);
        return new(scope, TargetId(handle), label, "window");
    }
    public CaptureSource? ForegroundSource()
    {
        if (IsLocked) return null;
        var handle = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(handle, out var pid);
        if (handle == 0 || pid == Environment.ProcessId) return null;
        var title = new StringBuilder(512); Native.GetWindowText(handle, title, title.Capacity);
        if (title.Length == 0) return null;
        try { return RegisterLaunchedWindow(handle, pid, title.ToString()); }
        catch (SafetyException) { return null; }
    }
    public bool FocusSource(CaptureSource source)
    {
        if (IsLocked || !sources.TryGetValue(source.ScopeId, out var entry) || entry.Monitor
            || !Native.IsWindowVisible(entry.Handle) || Native.IsIconic(entry.Handle)) return false;
        Native.GetWindowThreadProcessId(entry.Handle, out var pid);
        if (pid != entry.ProcessId || source.TargetId != TargetId(entry.Handle)) return false;
        return Native.SetForegroundWindow(entry.Handle);
    }
    // Isolated harness only: ordinary source enumeration excludes the assistant's process.
    public CaptureSource RegisterOwnTestWindow(nint handle, string expectedTitle)
    {
        Native.GetWindowThreadProcessId(handle, out var pid);
        var title = new StringBuilder(512); Native.GetWindowText(handle, title, title.Capacity);
        if (pid != Environment.ProcessId || title.ToString() != expectedTitle
            || !expectedTitle.StartsWith("Desktop Guide Training Sandbox", StringComparison.Ordinal)
            || !Native.IsWindowVisible(handle) || !WgcCapture.Supported)
            throw new SafetyException("Only this process's visible disposable test window can be registered");
        var scope = Guid.NewGuid(); sources[scope] = (handle, false, pid);
        ownTestWindows.Add(handle);
        return new(scope, TargetId(handle), expectedTitle + " (isolated WGC window)", "window");
    }
    private PhysicalRect Rect(CaptureSource source)
    {
        if (!sources.TryGetValue(source.ScopeId, out var entry)) throw new SafetyException("Capture scope lost");
        Native.Rect rect;
        if (entry.Monitor)
        {
            var info = new Native.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>() };
            if (!Native.GetMonitorInfo(entry.Handle, ref info)) throw new SafetyException("Monitor unavailable");
            rect = info.Monitor;
        }
        else
        {
            Native.GetWindowThreadProcessId(entry.Handle, out var pid);
            if (!Native.IsWindow(entry.Handle) || !Native.IsWindowVisible(entry.Handle) || Native.IsIconic(entry.Handle))
                throw new SafetyException("Selected window is unavailable");
            if (pid != entry.ProcessId || source.TargetId != TargetId(entry.Handle))
                throw new SafetyException("Selected window identity changed. Choose it again.");
            if (Native.DwmGetWindowAttribute(entry.Handle, 9, out rect, System.Runtime.InteropServices.Marshal.SizeOf<Native.Rect>()) != 0
                && !Native.GetWindowRect(entry.Handle, out rect)) throw new SafetyException("Window geometry unavailable");
        }
        return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public bool IsForegroundAndVisible(CaptureSource source)
    {
        if (IsLocked || !sources.TryGetValue(source.ScopeId, out var entry)) return false;
        var foreground = Native.GetForegroundWindow();
        if (foreground == 0 || !Native.IsWindowVisible(foreground) || Native.IsIconic(foreground)) return false;
        Native.GetWindowThreadProcessId(foreground, out var pid);
        return (pid != Environment.ProcessId || ownTestWindows.Contains(foreground))
            && (entry.Monitor ? Native.MonitorFromWindow(foreground, 2) == entry.Handle : foreground == entry.Handle && pid == entry.ProcessId);
    }
    public bool IsCurrentGeometry(CaptureSource source, Geometry geometry)
    {
        try
        {
            if (Rect(source) != geometry.Source || !IsForegroundAndVisible(source)) return false;
            var entry = sources[source.ScopeId];
            var target = entry.Monitor ? Native.GetForegroundWindow() : entry.Handle;
            return TargetId(target) == geometry.LocalTargetId && Native.GetDpiForWindow(target) == geometry.Dpi;
        }
        catch (SafetyException) { return false; }
    }
    public bool IsPointVisible(CaptureSource source, int x, int y)
        => IsForegroundAndVisible(source) && Rect(source).Contains(x, y)
           && Native.GetAncestor(Native.WindowFromPoint(new() { X = x, Y = y }), 2) == Native.GetForegroundWindow();

    public bool HasMeaningfulChange(ScreenFrame before, ScreenFrame after)
    {
        if (before.Geometry != after.Geometry) return true;
        if (before.PixelDigest == after.PixelDigest) return false;
        using var original = new Bitmap(new MemoryStream(before.Png));
        using var current = new Bitmap(new MemoryStream(after.Png));
        int changed = 0;
        for (int y = 0; y < 36; y++) for (int x = 0; x < 64; x++)
        {
            var a = original.GetPixel(x * original.Width / 64, y * original.Height / 36);
            var b = current.GetPixel(x * current.Width / 64, y * current.Height / 36);
            if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 90) changed++;
        }
        return changed > 46; // >2% sampled pixels; caret/spinner noise alone should not spend tokens.
    }

    public bool IsGuidanceTargetCurrent(ScreenFrame before, ScreenFrame after, Target target)
    {
        target.Validate(before.Geometry.Id);
        if (before.Geometry != after.Geometry || HasMeaningfulChange(before, after)) return false;
        if (before.PixelDigest == after.PixelDigest) return true;
        using var original = new Bitmap(new MemoryStream(before.Png));
        using var current = new Bitmap(new MemoryStream(after.Png));
        var bounds = target.Bounds;
        // Compare the target and its immediate surroundings. Unrelated caret/spinner changes
        // must not suppress visual guidance; action execution still requires whole-frame equality.
        int left = Math.Max(0, (int)(bounds.Left * original.Width) - 12);
        int top = Math.Max(0, (int)(bounds.Top * original.Height) - 12);
        int right = Math.Min(original.Width, (int)Math.Ceiling(bounds.Right * original.Width) + 13);
        int bottom = Math.Min(original.Height, (int)Math.Ceiling(bounds.Bottom * original.Height) + 13);
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
        {
            var a = original.GetPixel(x, y); var b = current.GetPixel(x, y);
            if (Math.Abs(a.R-b.R) + Math.Abs(a.G-b.G) + Math.Abs(a.B-b.B) > 18) return false;
        }
        return true;
    }

    public async Task<ScreenFrame> CaptureAsync(CaptureSource source, IReadOnlyList<Bounds> masks, CancellationToken cancellation)
    {
        await captureGate.WaitAsync(cancellation);
        try
        {
            if (IsLocked) throw new SafetyException("Desktop is locked or unavailable");
            var rect = Rect(source);
            if (rect.Width <= 0 || rect.Height <= 0 || (long)rect.Width * rect.Height > 16_000_000)
                throw new SafetyException("Capture dimensions exceed the supported limit");
            var entry = sources[source.ScopeId];
            cancellation.ThrowIfCancellationRequested();
            using var original = entry.Monitor ? await Task.Run(() => Gdi(rect), cancellation) : await wgc.CaptureAsync(entry.Handle, cancellation);
            if (!entry.Monitor && (original.Width != rect.Width || original.Height != rect.Height))
                throw new SafetyException("Window capture size changed; select and preview again");
            var scale = Math.Min(1, 1600d / Math.Max(original.Width, original.Height));
            int width = Math.Max(1, (int)Math.Round(original.Width * scale)), height = Math.Max(1, (int)Math.Round(original.Height * scale));
            using var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(output))
            {
                graphics.DrawImage(original, 0, 0, width, height);
                foreach (var mask in masks)
                {
                    mask.Validate();
                    graphics.FillRectangle(Brushes.Black, (float)(mask.Left * width), (float)(mask.Top * height),
                        (float)Math.Ceiling((mask.Right - mask.Left) * width), (float)Math.Ceiling((mask.Bottom - mask.Top) * height));
                }
            }
            using var stream = new MemoryStream(); output.Save(stream, ImageFormat.Png);
            var png = stream.ToArray();
            cancellation.ThrowIfCancellationRequested();
            if (Rect(source) != rect) throw new SafetyException("Window moved or resized during capture. Observe it again.");
            var capturedTarget = entry.Monitor ? Native.GetForegroundWindow() : entry.Handle;
            var geometry = new Geometry(Guid.Empty, source.ScopeId, rect, width, height, Math.Max(96, Native.GetDpiForWindow(capturedTarget)),
                entry.Handle.ToString(), TargetId(capturedTarget), new(0, 0, 1, 1), new(0, 0, 1, 1));
            if (geometries.TryGetValue(source.ScopeId, out var previous) && previous == geometry with { Id = previous.Id }) geometry = previous;
            else geometry = geometry with { Id = Guid.NewGuid() };
            geometries[source.ScopeId] = geometry;
            var frame = new ScreenFrame(new(Guid.NewGuid(), DateTimeOffset.UtcNow, source.ScopeId, geometry.Id, width, height, true),
                geometry, png, Convert.ToHexString(SHA256.HashData(png))) { PrivacyMasks = masks.ToArray() };
            frame.Validate(); return frame;
        }
        catch { wgc.Stop(); throw; }
        finally { captureGate.Release(); }
    }
    private static Bitmap Gdi(PhysicalRect rect)
    {
        var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        try { using var graphics = Graphics.FromImage(bitmap); graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }
    public void StopCapture() => wgc.Stop();
    public void Dispose() { wgc.Dispose(); captureGate.Dispose(); }
}
