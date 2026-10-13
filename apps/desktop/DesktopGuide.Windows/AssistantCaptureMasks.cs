using DesktopGuide.Core;

namespace DesktopGuide.Windows;

// Only affects images produced by Desktop Guide, not the desktop compositor or
// another recorder. Window WGC already isolates the task from separate UI windows.
public static class AssistantCaptureMasks
{
    internal static IReadOnlyDictionary<nint, PhysicalRect> Snapshot(IReadOnlySet<nint> testWindows)
    {
        var windows = new SortedDictionary<nint, PhysicalRect>();
        var geometryUnavailable = false;
        if (!Native.EnumWindows((handle, _) =>
        {
            Native.GetWindowThreadProcessId(handle, out var pid);
            if (pid != Environment.ProcessId || testWindows.Contains(handle)
                || !Native.IsWindowVisible(handle) || Native.IsIconic(handle)) return true;
            if (!Native.GetWindowRect(handle, out var rect)) { geometryUnavailable = true; return false; }
            // Include the shadow and antialiased edge outside the native rectangle.
            var padding = (int)Math.Ceiling(24 * Math.Max(96, Native.GetDpiForWindow(handle)) / 96d);
            if (rect.Right > rect.Left && rect.Bottom > rect.Top)
                windows[handle] = new(rect.Left - padding, rect.Top - padding, rect.Right - rect.Left + padding * 2, rect.Bottom - rect.Top + padding * 2);
            return true;
        }, 0)) throw new SafetyException("Desktop Guide's visible windows could not be checked. Try again.");
        if (geometryUnavailable) throw new SafetyException("Desktop Guide's recording-visible window could not be masked. Try again.");
        return windows;
    }

    public static IReadOnlyList<Bounds> Project(PhysicalRect source, IEnumerable<PhysicalRect> windows)
    {
        if (source.Width <= 0 || source.Height <= 0) throw new SafetyException("Invalid capture geometry");
        var masks = new List<Bounds>();
        foreach (var window in windows)
        {
            var left = Math.Max(source.Left, window.Left); var top = Math.Max(source.Top, window.Top);
            var right = Math.Min((long)source.Left + source.Width, (long)window.Left + window.Width);
            var bottom = Math.Min((long)source.Top + source.Height, (long)window.Top + window.Height);
            if (right <= left || bottom <= top) continue;
            masks.Add(new((left - source.Left) / (double)source.Width, (top - source.Top) / (double)source.Height,
                (right - source.Left) / (double)source.Width, (bottom - source.Top) / (double)source.Height));
        }
        return masks;
    }
}
