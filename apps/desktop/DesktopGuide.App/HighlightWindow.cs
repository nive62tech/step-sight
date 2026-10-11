using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DesktopGuide.Core;
using DesktopGuide.Windows;

namespace DesktopGuide.App;

public readonly record struct HighlightPlacement(PixelPoint Position, double Width, double Height)
{
    public static HighlightPlacement Calculate(PhysicalRect bounds, double scale)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || !double.IsFinite(scale) || scale <= 0)
            throw new SafetyException("Invalid highlight geometry");
        // A physical target is converted to DIP exactly once, including negative monitor coordinates.
        return new(new(bounds.Left - 4, bounds.Top - 4), (bounds.Width + 8) / scale, (bounds.Height + 8) / scale);
    }
}

public sealed class HighlightWindow : Window
{
    public HighlightWindow()
    {
        CanResize = false; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        WindowDecorations = WindowDecorations.None; Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent]; IsHitTestVisible = false;
        Content = CreateOutline();
        Opened += (_, _) => { if (TryGetPlatformHandle()?.Handle is { } handle) WindowStyles.ConfigureOverlay(handle); };
    }
    public void SetMode(bool autonomous) { if (Content is Border outline) ApplyMode(outline, autonomous); }
    internal static void ApplyMode(Border outline, bool autonomous)
    {
        outline.BorderBrush = Brush.Parse(autonomous ? "#EF5270" : "#48CA83");
        outline.Background = Brush.Parse(autonomous ? "#14EF5270" : "#1448CA83");
    }
    internal static Border CreateOutline() => new() { Margin = new(2), CornerRadius = new(7), BorderThickness = new(2),
        BorderBrush = Brush.Parse("#48CA83"), Background = Brush.Parse("#1448CA83"),
        BoxShadow = BoxShadows.Parse("0 0 0 1 #DDFFFFFF") };
    public void ShowTarget(PhysicalRect bounds)
    {
        var screen = Screens.ScreenFromPoint(new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
        var placement = HighlightPlacement.Calculate(bounds, screen?.Scaling ?? 1);
        Position = placement.Position; Width = placement.Width; Height = placement.Height; Show();
    }
}
