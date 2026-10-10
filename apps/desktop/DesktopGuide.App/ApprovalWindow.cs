using Avalonia.Controls;
using Avalonia.Layout;
using DesktopGuide.Core;
using DesktopGuide.Windows;

namespace DesktopGuide.App;

public sealed class ApprovalWindow : Window
{
    public ApprovalWindow(ActionProposal proposal, Func<Task> approve)
    {
        Width = 410; SizeToContent = SizeToContent.Height; MaxHeight = 650; Topmost = true; CanResize = false; ShowInTaskbar = false;
        Title = "Approve one action"; ShowActivated = false;
        var allow = new Button { Content = "Approve this exact action once", HorizontalAlignment = HorizontalAlignment.Stretch };
        var cancel = new Button { Content = "Keep it manual", HorizontalAlignment = HorizontalAlignment.Stretch };
        allow.Click += async (_, _) => { allow.IsEnabled = false; Close(); await approve(); };
        cancel.Click += (_, _) => Close();
        Content = new StackPanel { Margin = new(18), Spacing = 12, Children =
        {
            new TextBlock { Text = "One action only", FontSize = 22 },
            new TextBlock { Text = $"{proposal.Action.Kind}: {proposal.Effect}", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            new TextBlock { Text = proposal.Action.Kind == "type" ? $"Exact text: {proposal.Action.Text}" : proposal.Action.Kind == "key_chord" ? $"Exact keys: {proposal.Action.Chord}" : $"Point: {proposal.Action.X:F3}, {proposal.Action.Y:F3}; scroll ticks: {proposal.Action.Ticks}", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            new TextBlock { Text = "Focus the selected application first. Screen changes invalidate approval.", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, allow, cancel
        }};
        Opened += (_, _) => { if (TryGetPlatformHandle()?.Handle is { } handle) WindowStyles.ConfigureApproval(handle); };
    }
}
