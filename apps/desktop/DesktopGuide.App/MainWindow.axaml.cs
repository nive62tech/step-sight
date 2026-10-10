using Avalonia.Controls;

namespace DesktopGuide.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateLayoutMode();
        DataContextChanged += (_, _) => UpdateLayoutMode();
    }
    private void UpdateLayoutMode()
    {
        var compact = Bounds.Width > 0 ? Bounds.Width < 760 : Width < 760;
        if (DataContext is MainViewModel vm) vm.IsCompact = compact;
        Sidebar.IsVisible = !compact;
        if (Sidebar.Parent is Grid layout) layout.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 190);
    }
}
