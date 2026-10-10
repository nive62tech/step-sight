using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DesktopGuide.Windows;

namespace DesktopGuide.App;

public sealed class OverlayWindow : Window
{
    public OverlayWindow()
    {
        Width = Height = 44; CanResize = false; ShowInTaskbar = false; Topmost = true;
        WindowDecorations = WindowDecorations.None; Background = Brushes.Transparent; ShowActivated = false;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = new Border { CornerRadius = new(22), BorderThickness = new(3), BorderBrush = Brush.Parse("#8BE3CB") };
        Opened += (_, _) => { if (TryGetPlatformHandle()?.Handle is { } handle) WindowStyles.ConfigureOverlay(handle); };
    }
    public void ShowTarget((int X, int Y) point)
    {
        var scaling = Screens.ScreenFromPoint(new PixelPoint(point.X, point.Y))?.Scaling ?? 1;
        Position = new PixelPoint(point.X - (int)(22 * scaling), point.Y - (int)(22 * scaling));
        Show();
    }
}
