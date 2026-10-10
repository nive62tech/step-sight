using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;

namespace DesktopGuide.App;

public sealed partial class CompanionWindow : Window
{
    private bool expanded;
    public event Action? PanelRequested;
    public event Action? ReturnToTask;
    public CompanionWindow()
    {
        InitializeComponent();
        ToggleButton.Click += (_, _) => { if (expanded) Collapse(); else Expand(); };
        PanelButton.Click += (_, _) => PanelRequested?.Invoke();
        ShowWhereButton.Click += async (_, _) => await RunOnTask(vm => vm.ShowWhereCommand);
        CheckButton.Click += async (_, _) => await RunOnTask(vm => vm.ReobserveCommand);
        ResumeButton.Click += async (_, _) => await RunOnTask(vm => vm.ResumeCommand);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Collapse(); ReturnToTask?.Invoke(); e.Handled = true; } };
        Opened += (_, _) => Place();
    }
    private async Task RunOnTask(Func<MainViewModel, IAsyncRelayCommand> command)
    {
        if (DataContext is not MainViewModel vm) return;
        Collapse(); ReturnToTask?.Invoke(); await command(vm).ExecuteAsync(null);
    }
    public void Expand(bool activate = true)
    {
        expanded = true; ExpandedBody.IsVisible = true; Width = 374;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        Height = Math.Min(660, (screen?.WorkingArea.Height ?? 800) / (screen?.Scaling ?? 1) - 32);
        Place(); Show(); if (activate) { Activate(); if (DataContext is MainViewModel { HasLesson: false }) PromptBox.Focus(); }
    }
    public void Collapse()
    {
        expanded = false; ExpandedBody.IsVisible = false; Width = 276; Height = 72; Place();
    }
    private void Place()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        Position = new PixelPoint(screen.WorkingArea.Right - (int)Math.Ceiling((Width + 16) * screen.Scaling),
            screen.WorkingArea.Bottom - (int)Math.Ceiling((Height + 16) * screen.Scaling));
    }
}
