using System.Text.RegularExpressions;
using DesktopGuide.Core;

namespace DesktopGuide.Platform;

public enum BrowserTargetStatus { NotAvailable, Matched, Rejected }
public sealed record BrowserTargetResolution(BrowserTargetStatus Status, Target? Target, string Message)
{
    public static BrowserTargetResolution Unavailable { get; } = new(BrowserTargetStatus.NotAvailable, null, "Image-based target; browser inspection is unavailable.");
    public static BrowserTargetResolution Reject(string message) => new(BrowserTargetStatus.Rejected, null, message);
}

// Local, read-only inspection. This interface grants no input, and no browser data is uploaded.
public interface IBrowserTargetResolver
{
    Task<BrowserTargetResolution> ResolveAsync(CaptureSource source, ScreenFrame frame, GuidanceStep step, CancellationToken cancellation);
}
public sealed class NoBrowserTargetResolver : IBrowserTargetResolver
{
    public Task<BrowserTargetResolution> ResolveAsync(CaptureSource source, ScreenFrame frame, GuidanceStep step, CancellationToken cancellation)
        => Task.FromResult(BrowserTargetResolution.Unavailable);
}

public sealed record BrowserControl(string Id, string Label, string Role, PhysicalRect Rectangle,
    bool Enabled = true, bool Offscreen = false, bool Password = false);
public sealed record BrowserControlMatch(BrowserControl? Control, Target? Target, string Message);

public static class BrowserTargetMatcher
{
    private static string Normalize(string label) => Regex.Replace(label.Normalize(), @"\s+", " ").Trim();

    // UIA supplies physical desktop pixels, independent of browser zoom or display scaling.
    // Invert Geometry.Map instead of applying DPI a second time. Reject clipped/masked controls.
    public static Bounds? ImageBounds(ScreenFrame frame, PhysicalRect rectangle)
    {
        var g = frame.Geometry; var s = g.Source;
        if (s.Width < 2 || s.Height < 2 || rectangle.Width < 2 || rectangle.Height < 2
            || !s.Contains(rectangle.Left, rectangle.Top)
            || !s.Contains(rectangle.Left + rectangle.Width - 1, rectangle.Top + rectangle.Height - 1)) return null;
        var c = g.CropBounds; var content = g.ContentBounds;
        if (c.Right <= c.Left || c.Bottom <= c.Top) return null;
        double X(int x) => content.Left + ((x - s.Left) / (double)(s.Width - 1) - c.Left) / (c.Right - c.Left) * (content.Right - content.Left);
        double Y(int y) => content.Top + ((y - s.Top) / (double)(s.Height - 1) - c.Top) / (c.Bottom - c.Top) * (content.Bottom - content.Top);
        var b = new Bounds(X(rectangle.Left), Y(rectangle.Top), X(rectangle.Left + rectangle.Width - 1), Y(rectangle.Top + rectangle.Height - 1));
        if (!new[] { b.Left, b.Top, b.Right, b.Bottom }.All(double.IsFinite)
            || b.Left < content.Left || b.Top < content.Top || b.Right > content.Right || b.Bottom > content.Bottom
            || frame.PrivacyMasks.Any(m => b.Left <= m.Right && b.Right >= m.Left && b.Top <= m.Bottom && b.Bottom >= m.Top)) return null;
        return b;
    }

    public static BrowserControlMatch Match(ScreenFrame frame, GuidanceStep step, IReadOnlyList<BrowserControl> controls, bool complete)
    {
        if (!complete) return new(null, null, "Browser inspection was incomplete. Check the screen again before pointing.");
        if (step.SourceObservationId != frame.Observation.Id || step.Target is null)
            return new(null, null, "The browser target needs a current image and a visible label.");
        step.Target.Validate(frame.Geometry.Id);
        var label = Normalize(step.Target.VisibleLabel);
        var anchor = step.Target.Bounds;
        if (label.Length == 0 && anchor.Left == anchor.Right && anchor.Top == anchor.Bottom)
        {
            // Dedicated computer-use locators return a point, not a text label or box.
            // Use the control containing that exact point, then require the native hit-test.
            var point = frame.Geometry.Map(anchor.Left, anchor.Top);
            var hits = controls.Where(c => c.Enabled && !c.Offscreen && !c.Password
                && Compatible(step.ExpectedUserAction, c.Role) && c.Rectangle.Contains(point.X, point.Y))
                .Select(c => (Control: c, Bounds: ImageBounds(frame, c.Rectangle))).Where(c => c.Bounds is not null).ToArray();
            if (hits.Length != 1) return new(null, null, "The proposed point does not identify one visible browser control. No cursor is shown.");
            return new(hits[0].Control, step.Target with { Bounds = hits[0].Bounds!, VisibleLabel = hits[0].Control.Label },
                "Target checked against the browser control at the proposed point.");
        }
        if (label.Length is 0 or > 256) return new(null, null, "The image did not identify a usable control label.");
        var matches = controls.Where(c => c.Enabled && !c.Offscreen && !c.Password
            && Compatible(step.ExpectedUserAction, c.Role)
            && string.Equals(Normalize(c.Label), label, StringComparison.OrdinalIgnoreCase))
            .Select(c => (Control: c, Bounds: ImageBounds(frame, c.Rectangle))).Where(c => c.Bounds is not null).ToArray();
        // Do not use the model's approximate coordinates to break a same-label tie.
        if (matches.Length > 1) return new(null, null, "Several visible browser controls have this label. Ask for a more specific target; no cursor is shown.");
        if (matches.Length == 0) return new(null, null, "The browser could not confirm an enabled, visible control with this label. Use Find click target to recheck.");
        var match = matches[0];
        return new(match.Control, step.Target with { Bounds = match.Bounds! }, "Target checked against a live browser control.");
    }

    private static bool Compatible(string action, string role) => action switch
    {
        "type" => role is "edit" or "combobox",
        "click" or "manual" => role is "button" or "link" or "tab" or "menuitem" or "checkbox" or "radio" or "edit" or "combobox" or "listitem" or "treeitem",
        _ => false
    };
}
