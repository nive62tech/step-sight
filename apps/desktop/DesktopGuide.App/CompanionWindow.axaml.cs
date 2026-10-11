using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopGuide.Core;

namespace DesktopGuide.App;

public sealed partial class CompanionWindow : Window
{
    private bool expanded;
    private MainViewModel? observed;
    private PhysicalRect? activeTarget;
    public event Action? PanelRequested;
    public event Action? ReturnToTask;
    public CompanionWindow()
    {
        InitializeComponent();
        ToggleButton.Click += (_, _) => { if (expanded) Collapse(); else Expand(); };
        PanelButton.Click += (_, _) => PanelRequested?.Invoke();
        ShowWhereButton.Click += async (_, _) => await RunOnTask(vm => vm.ShowWhereCommand);
        FindTargetButton.Click += async (_, _) => await RunOnTask(vm => vm.FindTargetCommand);
        CompactFindButton.Click += async (_, _) => await RunOnTask(vm => vm.FindTargetCommand);
        CompactCheckButton.Click += async (_, _) => await RunOnTask(vm => vm.ReobserveCommand);
        CheckButton.Click += async (_, _) => await RunOnTask(vm => vm.ReobserveCommand);
        ResumeButton.Click += async (_, _) => await RunOnTask(vm => vm.ResumeCommand);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Collapse(); ReturnToTask?.Invoke(); e.Handled = true; } };
        Opened += (_, _) => Place();
        DataContextChanged += (_, _) =>
        {
            if (observed is not null) observed.PropertyChanged -= ModelChanged;
            observed = DataContext as MainViewModel;
            if (observed is not null) observed.PropertyChanged += ModelChanged;
            UpdateCompact();
        };
        Closed += (_, _) => { if (observed is not null) observed.PropertyChanged -= ModelChanged; };
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.HasCompactCard) or nameof(MainViewModel.HasNotice)) UpdateCompact();
    }
    private void UpdateCompact()
    {
        CompactBody.IsVisible = !expanded && observed?.HasCompactCard == true;
        if (expanded) return;
        var hasCard = CompactBody.IsVisible;
        var width = hasCard ? 374 : 276;
        var height = hasCard ? (observed!.HasNotice ? 340 : 270) : 72;
        if (Width != width || Height != height) { Width = width; Height = height; Place(); }
    }
    private async Task RunOnTask(Func<MainViewModel, IAsyncRelayCommand> command)
    {
        if (DataContext is not MainViewModel vm) return;
        Collapse(); ReturnToTask?.Invoke(); await command(vm).ExecuteAsync(null);
    }
    public void Expand(bool activate = true)
    {
        expanded = true; ExpandedBody.IsVisible = true; CompactBody.IsVisible = false; Width = 374;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        Height = Math.Min(660, (screen?.WorkingArea.Height ?? 800) / (screen?.Scaling ?? 1) - 32);
        Place(); Show(); if (activate) { Activate(); if (DataContext is MainViewModel { HasLesson: false }) PromptBox.Focus(); }
    }
    public void Collapse()
    {
        expanded = false; ExpandedBody.IsVisible = false; UpdateCompact();
    }
    public void AvoidTarget(PhysicalRect target)
    {
        activeTarget = target;
        var screen = Screens.ScreenFromPoint(new(target.Left, target.Top)) ?? Screens.Primary;
        if (screen is null) return;
        Position = CompanionPlacement.AvoidTarget(Position, new((int)Math.Ceiling(Width * screen.Scaling),
            (int)Math.Ceiling(Height * screen.Scaling)), screen.WorkingArea, target);
    }
    public void ClearTarget() => activeTarget = null;
    private void Place()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        Position = new PixelPoint(screen.WorkingArea.Right - (int)Math.Ceiling((Width + 16) * screen.Scaling),
            screen.WorkingArea.Bottom - (int)Math.Ceiling((Height + 16) * screen.Scaling));
        if (activeTarget is { } target) AvoidTarget(target);
    }
}

public static class CompanionPlacement
{
    public static PixelPoint AvoidTarget(PixelPoint current, PixelSize size, PixelRect screen, PhysicalRect target)
    {
        var protectedArea = new PixelRect(target.Left - 36, target.Top - 36, target.Width + 72, target.Height + 72);
        var candidates = new[] { current, new PixelPoint(screen.X + 16, screen.Bottom - size.Height - 16),
            new PixelPoint(screen.Right - size.Width - 16, screen.Y + 16), new PixelPoint(screen.X + 16, screen.Y + 16) };
        foreach (var candidate in candidates)
            if (!new PixelRect(candidate, size).Intersects(protectedArea)) return candidate;
        return current;
    }
}
