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
    private HighlightWindow? highlight;
    private CompanionWindow? companion;
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
            registrations.AddSingleton<IBrowserLauncher, WindowsBrowserLauncher>();
            // User-selected screenshot-only operation: no UIA/DOM/browser control inspection.
            registrations.AddSingleton<IBrowserTargetResolver, NoBrowserTargetResolver>();
            registrations.AddSingleton<IScreenCapture>(s => s.GetRequiredService<WindowsCapture>());
            registrations.AddSingleton<IWindowTracker>(s => s.GetRequiredService<WindowsCapture>());
            registrations.AddSingleton<IUserActivityObserver, WindowsActivity>();
            registrations.AddSingleton<WindowsPointer>(); registrations.AddSingleton<IPointerGuide>(s => s.GetRequiredService<WindowsPointer>());
            registrations.AddSingleton<IInputExecutor, WindowsInputExecutor>();
            registrations.AddSingleton<IAudioCapture, WindowsAudioCapture>(); registrations.AddSingleton<ISpeechPlayback, WindowsSpeechPlayback>();
            registrations.AddSingleton<ILocalSessionStore, WindowsLocalStore>();
            registrations.AddSingleton<CognitoLogin>(); registrations.AddSingleton<ILoginSession>(s => s.GetRequiredService<CognitoLogin>());
            registrations.AddSingleton<IAccountConnection, CloudConnection>(); registrations.AddSingleton<GuidanceApiClient>();
            registrations.AddSingleton<IGuidanceApiClient>(s => s.GetRequiredService<GuidanceApiClient>());
            registrations.AddSingleton<IBrowserPlanner>(s => s.GetRequiredService<GuidanceApiClient>());
            registrations.AddSingleton<IVoicePromptRecorder, VoicePromptRecorder>();
            registrations.AddSingleton<LessonController>(); registrations.AddSingleton<MainViewModel>();
            var browserAuthCheck = Program.Arguments.Contains("--browser-auth-check");
            var renderPreview = Program.Arguments.Contains("--render-preview") || browserAuthCheck;
            if (renderPreview)
            {
                var preview = new PreviewEnvironment();
                registrations.AddSingleton<IBrowserLauncher, PreviewBrowserLauncher>();
                registrations.AddSingleton<IBrowserTargetResolver, NoBrowserTargetResolver>();
                registrations.AddSingleton<IScreenCapture>(preview); registrations.AddSingleton<IWindowTracker>(preview);
                registrations.AddSingleton<IUserActivityObserver>(preview); registrations.AddSingleton<IInputExecutor>(preview);
                registrations.AddSingleton<ILocalSessionStore>(preview); registrations.AddSingleton<IAudioCapture>(preview);
                registrations.AddSingleton<ISpeechPlayback>(preview);
                if (browserAuthCheck)
                {
                    if (config.AuthMode != "cognito") throw new InvalidOperationException("Browser auth check requires the live pilot configuration.");
                    registrations.AddSingleton(s => new CognitoLogin(config, new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, null,
                        Path.GetFullPath(".local/browser-auth-token.bin"), uri =>
                        {
                            Directory.CreateDirectory(".local");
                            File.WriteAllText(".local/browser-auth-request.json", System.Text.Json.JsonSerializer.Serialize(new { url = uri.AbsoluteUri }));
                        }));
                }
                else
                {
                    registrations.AddSingleton<ILoginSession>(new PreviewLogin(UiCheck.Argument("--scene", "signed-out") != "signed-out", config.AuthMode == "mock"));
                    registrations.AddSingleton<IAccountConnection, PreviewConnection>();
                }
            }
            services = registrations.BuildServiceProvider();
            var vm = services.GetRequiredService<MainViewModel>();
            if (!renderPreview) vm.AttachRemote(services.GetRequiredService<GuidanceApiClient>(), config.ApiOrigin);
            var requestedLesson = UiCheck.Argument("--lesson", "");
            if (vm.LessonChoices.FirstOrDefault(l => l.Id == requestedLesson) is { } initialLesson) vm.SelectedLessonChoice = initialLesson;
            var window = new MainWindow { DataContext = vm, Title = config.BrandName };
            if (renderPreview && int.TryParse(UiCheck.Argument("--width", "960"), out var previewWidth)) window.Width = Math.Clamp(previewWidth, 420, 1400);
            desktop.MainWindow = window;
            if (renderPreview)
            {
                window.Opened += async (_, _) => await UiCheck.RunAsync(window, vm, services.GetRequiredService<LessonController>(),
                    services.GetRequiredService<CognitoLogin>(), desktop, services);
                base.OnFrameworkInitializationCompleted(); return;
            }
            overlay = new OverlayWindow(); highlight = new HighlightWindow();
            companion = new CompanionWindow { DataContext = vm };
            var pointer = services.GetRequiredService<WindowsPointer>();
            var controller = services.GetRequiredService<LessonController>();
            var capture = services.GetRequiredService<WindowsCapture>();
            var activity = services.GetRequiredService<IUserActivityObserver>();
            pointer.PrepareTargetAsync = async (bounds, token) =>
            {
                token.ThrowIfCancellationRequested();
                await Dispatcher.UIThread.InvokeAsync(() => { if (!token.IsCancellationRequested) companion.AvoidTarget(bounds); });
            };
            void FocusTask(CaptureSource source)
            {
                companion.Collapse(); window.Hide();
                if (!capture.FocusSource(source)) vm.Notice = "Bring your task window to the front to see the guide marker.";
            }
            void ShowPanel() { vm.OpenSettingsCommand.Execute(null); window.Show(); window.Activate(); }
            vm.GuideStarting += FocusTask;
            companion.ReturnToTask += () => { if (vm.SelectedSource is { } source && vm.HasLesson) FocusTask(source); };
            companion.PanelRequested += ShowPanel;
            companion.Opened += (_, _) =>
            {
                if (companion.TryGetPlatformHandle()?.Handle is { } handle)
                {
                    activity.IgnoreWindow(handle);
                    if (!WindowStyles.ExcludeFromCapture(handle)) vm.Notice = "Keep the companion outside the window you share so it doesn't cover your task.";
                }
            };
            if (activity is ICompanionHotkey hotkey)
                hotkey.Invoked += () => Dispatcher.UIThread.Post(() =>
                {
                    // Resolving the foreground window does not capture pixels or grant observation.
                    if (!vm.HasLesson && !vm.IsBusy) vm.PrepareCurrentWindow(capture.ForegroundSource());
                    companion.Expand();
                });
            pointer.TravelShown += point =>
            {
                var epoch = controller.Permissions.Epoch;
                Dispatcher.UIThread.Post(() =>
                {
                    if (vm.ObservationConsent && vm.SelectedMode != 2 && !pointer.ReducedMotion && epoch == controller.Permissions.Epoch && controller.State == LessonState.Pointing)
                        overlay.ShowTravel(point);
                });
            };
            pointer.TargetShown += point =>
            {
                var epoch = controller.Permissions.Epoch;
                var instruction = pointer.Instruction; var action = pointer.ExpectedAction; var bounds = pointer.TargetBounds;
                Dispatcher.UIThread.Post(() =>
                {
                    if (vm.ObservationConsent && vm.SelectedMode != 2 && epoch == controller.Permissions.Epoch && controller.State is LessonState.Pointing or LessonState.WaitingForLearner)
                    {
                        if (bounds is not null) highlight.ShowTarget(bounds);
                        overlay.ShowTarget(point, instruction, action, pointer.ReducedMotion);
                    }
                });
            };
            pointer.TargetHidden += () => Dispatcher.UIThread.Post(() => { overlay.Hide(); highlight.Hide(); companion.ClearTarget(); });
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.IsAutonomous))
                {
                    overlay.SetMode(vm.IsAutonomous); highlight.SetMode(vm.IsAutonomous);
                    if (tray is not null) { tray.Icon = CreateTrayIcon(vm.IsAutonomous); tray.ToolTipText = config.BrandName + " - " + vm.ModeLabel + " - Stop available"; }
                }
                if (args.PropertyName == nameof(MainViewModel.IsSigningIn) && !vm.IsSigningIn && vm.SignedIn && !vm.IsBusy)
                    Dispatcher.UIThread.Post(() => { window.Show(); window.Activate(); });
            };
            window.Opened += (_, _) =>
            {
                companion.Show();
                var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
                if (handle != 0)
                {
                    services.GetRequiredService<IUserActivityObserver>().IgnoreWindow(handle);
                    if (!WindowStyles.ExcludeFromCapture(handle))
                        vm.Notice = "Keep this panel outside the window you share so it doesn't cover your task.";
                }
            };
            var exiting = false;
            var menu = new NativeMenu();
            var stop = new NativeMenuItem("Stop lesson"); stop.Click += (_, _) => vm.StopCommand.Execute(null);
            var show = new NativeMenuItem("Open companion"); show.Click += (_, _) => companion.Expand();
            var settings = new NativeMenuItem("Account & preferences"); settings.Click += (_, _) => ShowPanel();
            var exit = new NativeMenuItem("Exit"); exit.Click += (_, _) => { exiting = true; vm.StopCommand.Execute(null); desktop.Shutdown(); };
            menu.Items.Add(show); menu.Items.Add(settings); menu.Items.Add(stop); menu.Items.Add(exit);
            WindowIcon CreateTrayIcon(bool autonomous)
            {
            using var iconBitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
            var icon = new Border { Width = 32, Height = 32, Background = Avalonia.Media.Brush.Parse(autonomous ? "#BB2343" : "#167247"), CornerRadius = new(8),
                Child = new TextBlock { Text = "dg", FontFamily = "Georgia", FontSize = 21, Foreground = Avalonia.Media.Brushes.White,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center } };
            icon.Measure(new Size(32, 32)); icon.Arrange(new Rect(0, 0, 32, 32)); iconBitmap.Render(icon);
            using var iconStream = new MemoryStream(); iconBitmap.Save(iconStream, PngBitmapEncoderOptions.Default); iconStream.Position = 0;
            return new WindowIcon(iconStream);
            }
            tray = new TrayIcon { Icon = CreateTrayIcon(vm.IsAutonomous), ToolTipText = config.BrandName + " - " + vm.ModeLabel + " - Stop available", Menu = menu, IsVisible = true };
            window.Closing += (_, e) => { if (!exiting) { e.Cancel = true; window.Hide(); } };
            companion.Closing += (_, e) => { if (!exiting) { e.Cancel = true; companion.Collapse(); } };
            desktop.ShutdownRequested += (_, _) => exiting = true;
            desktop.Exit += (_, _) => { exiting = true; vm.StopCommand.Execute(null); overlay.Close(); highlight.Close(); companion.Close(); tray.Dispose(); services.Dispose(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
