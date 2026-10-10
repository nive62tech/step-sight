using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DesktopGuide.App.Services;
using DesktopGuide.Core;
using DesktopGuide.Platform;
using DesktopGuide.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopGuide.App;

public sealed partial class App : Application
{
    private ServiceProvider? services;
    private TrayIcon? tray;
    private OverlayWindow? overlay;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var configIndex = Array.IndexOf(Program.Arguments, "--config");
            var config = PublicConfiguration.Load(configIndex >= 0 ? Program.Arguments[configIndex + 1] : null);
            var registrations = new ServiceCollection();
            registrations.AddSingleton(config); registrations.AddSingleton<PermissionSupervisor>();
            registrations.AddSingleton<WindowsCapture>();
            registrations.AddSingleton<IScreenCapture>(s => s.GetRequiredService<WindowsCapture>());
            registrations.AddSingleton<IWindowTracker>(s => s.GetRequiredService<WindowsCapture>());
            registrations.AddSingleton<IUserActivityObserver, WindowsActivity>();
            registrations.AddSingleton<WindowsPointer>(); registrations.AddSingleton<IPointerGuide>(s => s.GetRequiredService<WindowsPointer>());
            registrations.AddSingleton<IInputExecutor, WindowsInputExecutor>();
            registrations.AddSingleton<IAudioCapture, WindowsAudioCapture>(); registrations.AddSingleton<ISpeechPlayback, WindowsSpeechPlayback>();
            registrations.AddSingleton<ILocalSessionStore, WindowsLocalStore>();
            registrations.AddSingleton<CognitoLogin>(); registrations.AddSingleton<IGuidanceApiClient, GuidanceApiClient>();
            registrations.AddSingleton<LessonController>(); registrations.AddSingleton<MainViewModel>();
            var renderPreview = Program.Arguments.Contains("--render-preview");
            if (renderPreview)
            {
                var preview = new PreviewEnvironment();
                registrations.AddSingleton<IScreenCapture>(preview); registrations.AddSingleton<IWindowTracker>(preview);
                registrations.AddSingleton<IUserActivityObserver>(preview); registrations.AddSingleton<IInputExecutor>(preview);
                registrations.AddSingleton<ILocalSessionStore>(preview); registrations.AddSingleton<IAudioCapture>(preview);
                registrations.AddSingleton<ISpeechPlayback>(preview);
            }
            services = registrations.BuildServiceProvider();
            var vm = services.GetRequiredService<MainViewModel>();
            var window = new MainWindow { DataContext = vm, Title = config.BrandName };
            desktop.MainWindow = window;
            if (renderPreview)
            {
                window.Opened += (_, _) => DispatcherTimer.RunOnce(() =>
                {
                    Directory.CreateDirectory("artifacts");
                    using var bitmap = new RenderTargetBitmap(new PixelSize(460, 820), new Vector(96, 96));
                    bitmap.Render(window); bitmap.Save("artifacts/ui-preview.png", PngBitmapEncoderOptions.Default);
                    desktop.Shutdown(); services.Dispose();
                }, TimeSpan.FromMilliseconds(500));
                base.OnFrameworkInitializationCompleted(); return;
            }
            overlay = new OverlayWindow();
            var pointer = services.GetRequiredService<WindowsPointer>();
            pointer.TargetShown += point => Dispatcher.UIThread.Post(() => overlay.ShowTarget(point));
            pointer.TargetHidden += () => Dispatcher.UIThread.Post(() => overlay.Hide());
            window.Opened += (_, _) =>
            {
                var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
                if (handle != 0)
                {
                    services.GetRequiredService<IUserActivityObserver>().IgnoreWindow(handle);
                    vm.Notice = WindowStyles.ExcludeFromCapture(handle)
                        ? "Capture exclusion requested. Review your preview to verify it on this Windows build."
                        : "Capture exclusion unavailable. Move the panel outside the selected source and check preview.";
                }
            };
            var menu = new NativeMenu();
            var stop = new NativeMenuItem("Stop lesson"); stop.Click += (_, _) => services.GetRequiredService<LessonController>().StopLocal();
            var show = new NativeMenuItem("Show panel"); show.Click += (_, _) => { window.Show(); window.Activate(); };
            var exit = new NativeMenuItem("Exit"); exit.Click += (_, _) => { vm.StopCommand.Execute(null); desktop.Shutdown(); };
            menu.Items.Add(show); menu.Items.Add(stop); menu.Items.Add(exit);
            using var iconBitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
            var icon = new Border { Width = 32, Height = 32, Background = Avalonia.Media.Brushes.Teal,
                Child = new TextBlock { Text = "S", FontSize = 24, Foreground = Avalonia.Media.Brushes.White,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center } };
            icon.Measure(new Size(32, 32)); icon.Arrange(new Rect(0, 0, 32, 32)); iconBitmap.Render(icon);
            using var iconStream = new MemoryStream(); iconBitmap.Save(iconStream, PngBitmapEncoderOptions.Default); iconStream.Position = 0;
            tray = new TrayIcon { Icon = new WindowIcon(iconStream), ToolTipText = config.BrandName + " — Stop available", Menu = menu, IsVisible = true };
            desktop.Exit += (_, _) => { vm.StopCommand.Execute(null); overlay.Close(); tray.Dispose(); services.Dispose(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
