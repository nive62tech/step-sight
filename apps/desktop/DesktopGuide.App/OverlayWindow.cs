using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DesktopGuide.Windows;

namespace DesktopGuide.App;

public readonly record struct MarkerPlacement(PixelPoint Position, double AnchorX, double AnchorY)
{
    public static MarkerPlacement Calculate(PixelPoint target, PixelRect screen, double scaling)
    {
        scaling = Math.Clamp(scaling, .5, 8);
        var width = (int)Math.Ceiling(340 * scaling); var height = (int)Math.Ceiling(140 * scaling);
        var left = target.X + 310 * scaling > screen.Right ? target.X - (int)(312 * scaling) : target.X - (int)(28 * scaling);
        var top = target.Y - (int)(50 * scaling);
        left = Math.Clamp(left, screen.X, Math.Max(screen.X, screen.Right - width));
        top = Math.Clamp(top, screen.Y, Math.Max(screen.Y, screen.Bottom - height));
        return new(new PixelPoint(left, top), (target.X - left) / scaling, (target.Y - top) / scaling);
    }
}

public sealed class GuidanceMarker : Canvas
{
    private readonly Border ripple, ring, bubble;
    private readonly Avalonia.Controls.Shapes.Path cursor;
    private readonly TextBlock action, comment;
    private double anchorX = 28, anchorY = 50;
    public GuidanceMarker()
    {
        Width = 340; Height = 140; IsHitTestVisible = false;
        ripple = new Border { Width = 48, Height = 48, CornerRadius = new(24), BorderThickness = new(2), BorderBrush = Brush.Parse("#3984FF"), RenderTransformOrigin = RelativePoint.Center };
        ring = new Border { Width = 38, Height = 38, CornerRadius = new(19), BorderThickness = new(2.5), BorderBrush = Brush.Parse("#3984FF"), Background = Brush.Parse("#293984FF"),
            BoxShadow = BoxShadows.Parse("0 0 0 1 #E6FFFFFF"), RenderTransformOrigin = RelativePoint.Center };
        cursor = new Avalonia.Controls.Shapes.Path {
            Data = Geometry.Parse("M 0,0 L 1,30 L 9,23 L 16,39 L 23,35 L 16,22 L 30,21 Z"),
            Fill = Brush.Parse("#151A23"), Stroke = Brushes.White, StrokeThickness = 2.5, Width = 34, Height = 43
        };
        action = new TextBlock { FontFamily = "Bahnschrift", FontWeight = FontWeight.SemiBold, FontSize = 16, Foreground = Brushes.White };
        comment = new TextBlock { FontFamily = "Bahnschrift", FontSize = 13, Foreground = Brush.Parse("#DFE6F2"), TextWrapping = TextWrapping.Wrap, MaxLines = 3 };
        bubble = new Border {
            Width = 246, MinHeight = 80, CornerRadius = new(12), Padding = new(15,12),
            Background = Brush.Parse("#18202D"), BorderBrush = Brush.Parse("#6A9FFF"), BorderThickness = new(1),
            BoxShadow = BoxShadows.Parse("0 4 14 0 #380A1322"),
            Child = new StackPanel { Spacing = 6, Children = { action, comment } }
        };
        Children.Add(ripple); Children.Add(ring); Children.Add(cursor); Children.Add(bubble);
        SetMode(false); SetTarget(28, 50, "Open Settings to change your workspace.", "click"); Animate(0, true);
    }
    public void SetMode(bool autonomous)
    {
        ripple.BorderBrush = ring.BorderBrush = bubble.BorderBrush = Brush.Parse(autonomous ? "#EF5270" : "#48CA83");
        ring.Background = Brush.Parse(autonomous ? "#29EF5270" : "#2948CA83");
        cursor.Fill = bubble.Background = Brush.Parse(autonomous ? "#491524" : "#123F2A");
        comment.Foreground = Brush.Parse(autonomous ? "#FFE4EA" : "#DDF7E8");
    }
    public void SetTarget(double x, double y, string instruction, string expectedAction, bool showCallout = true)
    {
        anchorX = x; anchorY = y;
        bubble.IsVisible = showCallout;
        action.Text = expectedAction switch { "agent_click" => "Agent clicking", "agent_scroll" => "Agent scrolling", "agent_type" => "Agent typing", "click" => "Click here", "type" => "Type here", "scroll" => "Scroll here", "key_chord" => "Use your keyboard", _ => "Look here" };
        comment.Text = instruction.Length > 180 ? instruction[..177] + "…" : instruction;
        SetLeft(ripple, x - 24); SetTop(ripple, y - 24);
        SetLeft(ring, x - 19); SetTop(ring, y - 19);
        SetLeft(bubble, Math.Clamp(x < 170 ? x + 45 : x - 285, 2, 92));
        SetTop(bubble, Math.Clamp(y - 24, 2, 54));
        Animate(0, true);
    }
    public void Animate(double seconds, bool reducedMotion)
    {
        var phase = reducedMotion ? 0 : (seconds % 1.6) / 1.6;
        var scale = reducedMotion ? 1 : .8 + .35 * phase;
        ripple.RenderTransform = new ScaleTransform(scale, scale); ripple.Opacity = reducedMotion ? .5 : .85 * (1 - phase);
        var press = reducedMotion ? 1 : 1 - .1 * Math.Pow(Math.Max(0, 1 - phase * 5), 2);
        ring.RenderTransform = new ScaleTransform(press, press);
        // Animate the click ring, never move the tip away from the grounded point.
        SetLeft(cursor, anchorX); SetTop(cursor, anchorY);
    }
}

public sealed class OverlayWindow : Window
{
    private readonly GuidanceMarker marker = new();
    private readonly DispatcherTimer animation;
    private readonly System.Diagnostics.Stopwatch clock = new();
    private bool reducedMotion;
    public void SetMode(bool autonomous) => marker.SetMode(autonomous);
    public OverlayWindow()
    {
        Width = 340; Height = 140; CanResize = false; ShowInTaskbar = false; Topmost = true;
        WindowDecorations = WindowDecorations.None; Background = Brushes.Transparent; ShowActivated = false;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent]; Content = marker;
        animation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        animation.Tick += (_, _) => { if (IsVisible) marker.Animate(clock.Elapsed.TotalSeconds, reducedMotion); };
        Opened += (_, _) => { if (TryGetPlatformHandle()?.Handle is { } handle) WindowStyles.ConfigureOverlay(handle); };
        PropertyChanged += (_, args) => { if (args.Property == IsVisibleProperty && !IsVisible) { animation.Stop(); clock.Stop(); } };
        Closed += (_, _) => animation.Stop();
    }
    public void ShowTarget((int X, int Y) point, string instruction, string expectedAction, bool reducedMotion)
    {
        this.reducedMotion = reducedMotion;
        Place(point, instruction, expectedAction, true);
        Show(); clock.Restart(); if (!reducedMotion) animation.Start(); else { animation.Stop(); marker.Animate(0, true); }
    }
    public void ShowTravel((int X, int Y) point)
    {
        Place(point, "", "click", false);
        animation.Stop(); marker.Animate(0, true); Show();
    }
    private void Place((int X, int Y) point, string instruction, string expectedAction, bool showCallout)
    {
        var target = new PixelPoint(point.X, point.Y); var screen = Screens.ScreenFromPoint(target);
        var placement = MarkerPlacement.Calculate(target, screen?.WorkingArea ?? new PixelRect(point.X-500, point.Y-500, 1000, 1000), screen?.Scaling ?? 1);
        Position = placement.Position; marker.SetTarget(placement.AnchorX, placement.AnchorY, instruction, expectedAction, showCallout);
    }
}
