using System.Diagnostics;
using System.Windows.Automation;
using DesktopGuide.Core;
using DesktopGuide.Platform;

namespace DesktopGuide.Windows;

/// <summary>Read-only browser accessibility grounding; never invokes a UIA action pattern.</summary>
public sealed class WindowsBrowserTargets(WindowsCapture capture, WindowsPointer pointer) : IBrowserTargetResolver
{
    private int inspecting;
    private static readonly Dictionary<ControlType, string> Roles = new()
    {
        [ControlType.Button] = "button", [ControlType.Hyperlink] = "link", [ControlType.TabItem] = "tab",
        [ControlType.MenuItem] = "menuitem", [ControlType.CheckBox] = "checkbox", [ControlType.RadioButton] = "radio",
        [ControlType.Edit] = "edit", [ControlType.ComboBox] = "combobox", [ControlType.ListItem] = "listitem", [ControlType.TreeItem] = "treeitem"
    };

    public async Task<BrowserTargetResolution> ResolveAsync(CaptureSource source, ScreenFrame frame, GuidanceStep step, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (source.Kind != "window" || step.Target is null || step.ExpectedUserAction is not ("click" or "type" or "manual")) return BrowserTargetResolution.Unavailable;
        var window = capture.InspectionWindow(source, frame.Geometry);
        if (window == 0) return BrowserTargetResolution.Reject("Bring the selected browser window to the front and check the screen again.");
        Native.GetWindowThreadProcessId(window, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (process.ProcessName is not ("chrome" or "msedge" or "firefox")) return BrowserTargetResolution.Unavailable;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return BrowserTargetResolution.Reject("The selected browser is no longer available."); }
        if (Interlocked.CompareExchange(ref inspecting, 1, 0) != 0)
            return BrowserTargetResolution.Reject("Browser inspection is still busy. Check the screen again shortly.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        var inspectionCancellation = deadline.Token;

        // UIA must not run on Avalonia's UI thread. One bounded background worker prevents a
        // hung accessibility provider from blocking Stop or spawning an unbounded set of workers.
        var completion = new TaskCompletionSource<BrowserTargetResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(Inspect(window, source, frame, step, inspectionCancellation)); }
            catch (OperationCanceledException) { completion.TrySetCanceled(inspectionCancellation); }
            catch (Exception)
            { completion.TrySetResult(BrowserTargetResolution.Reject("Browser controls changed or could not be read. Check the screen again.")); }
            finally { Interlocked.Exchange(ref inspecting, 0); }
        }) { IsBackground = true, Name = "Desktop Guide browser inspection" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellation); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { return BrowserTargetResolution.Reject("Browser inspection timed out. No target has been confirmed; check the screen again."); }
        catch (TimeoutException)
        { deadline.Cancel(); return BrowserTargetResolution.Reject("Browser inspection timed out. No target has been confirmed; check the screen again."); }
    }

    private BrowserTargetResolution Inspect(nint window, CaptureSource source, ScreenFrame frame, GuidanceStep step, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        void Check()
        {
            cancellation.ThrowIfCancellationRequested();
            if (clock.Elapsed > TimeSpan.FromMilliseconds(1700)) throw new InvalidOperationException("Inspection deadline");
        }
        Check();
        var root = AutomationElement.FromHandle(window);
        var condition = new OrCondition(Roles.Keys.Select(t => (Condition)new PropertyCondition(AutomationElement.ControlTypeProperty, t)).ToArray());
        var cache = new CacheRequest { TreeScope = TreeScope.Element };
        cache.Add(AutomationElement.BoundingRectangleProperty); cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.IsEnabledProperty); cache.Add(AutomationElement.IsOffscreenProperty); cache.Add(AutomationElement.IsPasswordProperty);
        AutomationElementCollection elements;
        using (cache.Activate()) elements = root.FindAll(TreeScope.Descendants, condition);
        Check();
        if (elements.Count > 600) return BrowserTargetResolution.Reject("This page exposes too many controls for a complete target check. Narrow the view and check again.");
        var controls = new List<BrowserControl>();
        var references = new Dictionary<string, AutomationElement>();
        foreach (AutomationElement element in elements)
        {
            Check();
            var info = element.Cached;
            if (info.IsOffscreen || !info.IsEnabled || info.IsPassword) continue;
            var rect = Rectangle(info.BoundingRectangle);
            if (rect is null || BrowserTargetMatcher.ImageBounds(frame, rect) is null) continue;
            // Never read text/value from masked, offscreen, disabled or password controls.
            // Read only the accessible label, never ValuePattern, HTML, cookies or field contents.
            var name = element.Current.Name;
            if (name.Length > 256) continue;
            var id = string.Join(".", element.GetRuntimeId());
            controls.Add(new(id, name, Roles[info.ControlType], rect)); references[id] = element;
        }
        Check();
        var match = BrowserTargetMatcher.Match(frame, step, controls, true);
        if (match.Control is null || match.Target is null) return BrowserTargetResolution.Reject(match.Message);
        // Move our non-activating dock before testing occlusion. The callback dispatches to
        // the UI thread; the caller awaits this background worker, so Stop stays responsive.
        pointer.PrepareVisualTargetAsync(frame, match.Target, cancellation).GetAwaiter().GetResult();
        Check();
        var chosen = references[match.Control.Id];
        var current = chosen.Current;
        if (!current.IsEnabled || current.IsOffscreen || current.IsPassword || Rectangle(current.BoundingRectangle) != match.Control.Rectangle
            || current.Name != match.Control.Label || !Roles.TryGetValue(current.ControlType, out var role) || role != match.Control.Role
            || capture.InspectionWindow(source, frame.Geometry) != window)
            return BrowserTargetResolution.Reject("The browser control changed while checking it. Check the screen again.");
        var center = match.Target.Bounds.Center;
        var point = frame.Geometry.Map(center.X, center.Y);
        if (!capture.IsPointVisible(source, point.X, point.Y)) return BrowserTargetResolution.Reject("The browser control is covered by another window.");
        var hit = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
        var same = false;
        for (var depth = 0; hit is not null && depth < 16; depth++)
        {
            Check();
            if (Automation.Compare(hit, chosen)) { same = true; break; }
            if (Automation.Compare(hit, root)) break;
            hit = TreeWalker.ControlViewWalker.GetParent(hit);
        }
        Check();
        return same ? new(BrowserTargetStatus.Matched, match.Target, match.Message)
            : BrowserTargetResolution.Reject("Another page element covers this control's center. Check the screen again.");
    }

    private static PhysicalRect? Rectangle(System.Windows.Rect rect)
    {
        if (rect.IsEmpty || !new[] { rect.Left, rect.Top, rect.Width, rect.Height }.All(double.IsFinite)
            || rect.Width < 2 || rect.Height < 2 || rect.Width > 100000 || rect.Height > 100000
            || Math.Abs(rect.Left) > 100000 || Math.Abs(rect.Top) > 100000) return null;
        var left = (int)Math.Ceiling(rect.Left); var top = (int)Math.Ceiling(rect.Top);
        return new(left, top, (int)Math.Floor(rect.Right) - left, (int)Math.Floor(rect.Bottom) - top);
    }
}
