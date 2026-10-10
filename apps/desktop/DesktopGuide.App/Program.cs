using Avalonia;
using Avalonia.Headless;
using DesktopGuide.Windows;

namespace DesktopGuide.App;

internal static class Program
{
    public static string[] Arguments { get; private set; } = [];
    [STAThread]
    public static void Main(string[] args)
    {
        Arguments = args;
        if (!args.Contains("--render-preview")) WindowStyles.EnableDpiAwareness();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => Arguments.Contains("--render-preview")
        ? AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia()
        : AppBuilder.Configure<App>().UsePlatformDetect();
}
