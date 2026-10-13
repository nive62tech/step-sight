using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopGuide.Core;

namespace DesktopGuide.App;

public sealed partial class CompanionWindow : Window
{
    private bool expanded;
    private MainViewModel? observed;
    private PhysicalRect? activeTarget;
    private readonly DispatcherTimer presentationTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private Action? settlePresentation;
    private TaskCompletionSource presentationSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WhenPresentationSettled => presentationSettled.Task;
    public event Action? PanelRequested;
    public event Action? ReturnToTask;
    public CompanionWindow()
    {
        InitializeComponent();
        presentationSettled.TrySetResult();
        ToggleButton.Click += (_, _) => { if (expanded) Collapse(); else Expand(); };
        BlobVoiceButton.Click += async (_, _) => { Expand(); if (observed is not null) await observed.TogglePromptVoiceCommand.ExecuteAsync(null); };
        PromptBlobButton.Click += async (_, _) => { if (observed is not null) await observed.TogglePromptVoiceCommand.ExecuteAsync(null); };
        BlobTypeButton.Click += (_, _) => { observed?.TypePromptCommand.Execute(null); Expand(); PromptBox.Focus(); };
        PanelButton.Click += (_, _) => PanelRequested?.Invoke();
        ShowWhereButton.Click += async (_, _) => await RunOnTask(vm => vm.ShowWhereCommand);
        FindTargetButton.Click += async (_, _) => await RunOnTask(vm => vm.FindTargetCommand);
        CompactFindButton.Click += async (_, _) => await RunOnTask(vm => vm.FindTargetCommand);
        CompactCheckButton.Click += async (_, _) => await RunOnTask(vm => vm.ReobserveCommand);
        CheckButton.Click += async (_, _) => await RunOnTask(vm => vm.ReobserveCommand);
        ResumeButton.Click += async (_, _) => await RunOnTask(vm => vm.ResumeCommand);
        CompactResumeButton.Click += async (_, _) => await RunOnTask(vm => vm.ResumeCommand);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Collapse(); ReturnToTask?.Invoke(); e.Handled = true; } };
        Opened += (_, _) => Place();
        DataContextChanged += (_, _) =>
        {
            if (observed is not null) observed.PropertyChanged -= ModelChanged;
            observed = DataContext as MainViewModel;
            if (observed is not null) observed.PropertyChanged += ModelChanged;
            UpdateCompact();
        };
        Closed += (_, _) => { FinishPresentation(); if (observed is not null) observed.PropertyChanged -= ModelChanged; };
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.ReducedMotion) && observed?.ReducedMotion == true) FinishPresentation();
        if (args.PropertyName is nameof(MainViewModel.HasCompactCard) or nameof(MainViewModel.HasNotice) or nameof(MainViewModel.IsPaused)) UpdateCompact();
    }
    private void UpdateCompact()
    {
        if (expanded) return;
        Present(false);
    }
    private async Task RunOnTask(Func<MainViewModel, IAsyncRelayCommand> command)
    {
        if (DataContext is not MainViewModel vm) return;
        Collapse(false); ReturnToTask?.Invoke(); await command(vm).ExecuteAsync(null);
    }
    public void Expand(bool activate = true)
    {
        if (!expanded) { FinishPresentation(); expanded = true; Present(true); }
        Show(); if (activate) { Activate(); if (DataContext is MainViewModel { HasLesson: false }) PromptBox.Focus(); }
    }
    public void Collapse(bool animate = true)
    {
        FinishPresentation(); expanded = false; Present(animate);
    }
    private void FinishPresentation()
    {
        presentationTimer.Stop();
        var settle = settlePresentation; settlePresentation = null; settle?.Invoke();
    }
    private void Present(bool animate)
    {
        FinishPresentation();
        var hasCard = !expanded && observed?.HasCompactCard == true;
        var showFrame = expanded || hasCard;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var width = showFrame ? 374d : 164d;
        var height = expanded ? Math.Min(760, (screen?.WorkingArea.Height ?? 800) / (screen?.Scaling ?? 1) - 32)
            : hasCard ? (observed!.IsPaused ? 400d : observed.HasNotice ? 340d : 270d) : 216d;
        void ApplyBodies() { ExpandedBody.IsVisible = expanded; CompactBody.IsVisible = hasCard; }
        var incoming = showFrame ? (Control)CompanionFrame : BlobLauncher;
        var other = showFrame ? (Control)BlobLauncher : CompanionFrame;
        var outgoing = other.IsVisible ? other : null;
        var canAnimate = animate && IsVisible && observed?.ReducedMotion != true;
        presentationSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Settle()
        {
            ApplyBodies(); CompanionFrame.IsVisible = showFrame; BlobLauncher.IsVisible = !showFrame;
            CompanionFrame.Opacity = BlobLauncher.Opacity = 1;
            CompanionFrame.RenderTransform = BlobLauncher.RenderTransform = null;
            Width = width; Height = height; Place(); presentationSettled.TrySetResult();
        }
        if (!canAnimate) { Settle(); return; }
        settlePresentation = Settle;
        var fromWidth = Width; var fromHeight = Height;
        // Keep the idle blob at its original bottom-right anchor while the deck
        // slides and fades around it. Only resize the native surface after fading.
        if (outgoing is not null) { Width = Math.Max(fromWidth, width); Height = Math.Max(fromHeight, height); Place(); }
        if (showFrame) ApplyBodies(); // Keep the old deck content during its closing fade.
        incoming.IsVisible = true; incoming.Opacity = outgoing is null ? .8 : 0;
        var slide = new TranslateTransform(24, 10); incoming.RenderTransform = slide;
        var outgoingSlide = new TranslateTransform(); if (outgoing is not null) outgoing.RenderTransform = outgoingSlide;
        var clock = Stopwatch.StartNew();
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            var t = Math.Clamp(clock.Elapsed.TotalMilliseconds / 240, 0, 1);
            var eased = 1 - Math.Pow(1 - t, 3);
            incoming.Opacity = outgoing is null ? .8 + .2 * eased : eased;
            slide.X = 24 * (1 - eased); slide.Y = 10 * (1 - eased);
            if (outgoing is not null) { outgoing.Opacity = 1 - eased; outgoingSlide.X = -12 * eased; }
            else { Width = fromWidth + (width - fromWidth) * eased; Height = fromHeight + (height - fromHeight) * eased; Place(); }
            if (t >= 1) FinishPresentation();
        };
        presentationTimer.Tick += tick;
        var finish = settlePresentation;
        settlePresentation = () => { presentationTimer.Tick -= tick; finish!(); };
        presentationTimer.Start();
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
