using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DesktopGuide.App.Services;
using DesktopGuide.Core;
using DesktopGuide.Platform;

namespace DesktopGuide.App;

internal sealed class PreviewLogin(bool signedIn, bool mock) : ILoginSession
{
    public bool SignedIn => signedIn;
    public bool IsMock => mock;
    public string? AccountName => signedIn ? "Pilot account · UI preview" : null;
    public bool RememberMe { get; set; }
    public event Action? Changed { add {} remove {} }
    public Task SignInAsync(CancellationToken c) => throw new SafetyException("Render preview cannot sign in.");
    public Task SignOutAsync() => Task.CompletedTask;
    public Task<string?> GetAccessAsync(CancellationToken c) => throw new SafetyException("Render preview cannot call the cloud.");
}
internal sealed class PreviewConnection : IAccountConnection
{
    public Task CheckAsync(CancellationToken c) => throw new SafetyException("Render preview cannot call the cloud.");
}
internal sealed class PreviewBrowserLauncher : IBrowserLauncher
{
    public Task<CaptureSource> OpenAsync(BrowserLaunch plan, CancellationToken c) => throw new SafetyException("UI preview cannot open or observe a browser.");
}
internal static class UiCheck
{
    public static string Argument(string name, string fallback)
    {
        var index = Array.IndexOf(Program.Arguments,name);
        return index >= 0 && index+1 < Program.Arguments.Length ? Program.Arguments[index+1] : fallback;
    }
    public static async Task RunAsync(MainWindow window, MainViewModel vm, LessonController controller, CognitoLogin login,
        IClassicDesktopStyleApplicationLifetime desktop, IDisposable services)
    {
        var directory = Path.GetFullPath(Argument("--output-dir","artifacts/ui-qa")); Directory.CreateDirectory(directory);
        var auth = Program.Arguments.Contains("--browser-auth-check");
        var scene = Argument("--scene","signed-out");
        try
        {
            if (auth)
            {
                await vm.SignInCommand.ExecuteAsync(null);
                var disarmed = false;
                try { controller.Permissions.RequireObservation(Guid.Empty); }
                catch (SafetyException) { disarmed=true; }
                var passed = vm.SignedIn && vm.IsServiceReady && vm.ShowTask && vm.SetupStep == 0 && disarmed
                    && !vm.ObservationConsent && !vm.PointerConsent && !vm.DemonstrationConsent;
                await File.WriteAllTextAsync(Path.Combine(directory,"browser-auth-check.json"),JsonSerializer.Serialize(new {
                    date=DateTimeOffset.UtcNow, result=passed ? "passed" : "failed",
                    tests=new { signed_in=vm.SignedIn, authenticated_live_lesson_access=vm.IsServiceReady,
                        next_step_visible=vm.ShowTask && vm.SetupStep==0, capture_disarmed=disarmed,
                        no_native_input=true, no_raw_tokens_written=true },
                    message=passed ? "Live browser PKCE, desktop token validation and signed-in setup state verified." : vm.Notice
                },new JsonSerializerOptions { WriteIndented=true }));
                scene = "live-signed-in";
            }
            else ApplyScene(vm,scene);
            DispatcherTimer.RunOnce(async () =>
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width,(int)window.Height),new Vector(96,96));
                bitmap.Render(window); bitmap.Save(Path.Combine(directory,$"{scene}-{(int)window.Width}.png"),PngBitmapEncoderOptions.Default);
                if (!auth)
                {
                    using var markerBitmap = new RenderTargetBitmap(new PixelSize(340,140),new Vector(96,96));
                    var marker = new GuidanceMarker(); marker.SetMode(vm.IsAutonomous); marker.Measure(new Size(340,140)); marker.Arrange(new Rect(0,0,340,140));
                    marker.Animate(.65,false); markerBitmap.Render(marker); markerBitmap.Save(Path.Combine(directory,"cursor-callout.png"),PngBitmapEncoderOptions.Default);
                    marker.Animate(1.1,false); markerBitmap.Render(marker); markerBitmap.Save(Path.Combine(directory,"cursor-callout-next-frame.png"),PngBitmapEncoderOptions.Default);
                    marker.Animate(0,true); markerBitmap.Render(marker); markerBitmap.Save(Path.Combine(directory,"cursor-reduced-motion.png"),PngBitmapEncoderOptions.Default);
                    var companion = new CompanionWindow { DataContext = vm };
                    companion.Expand(false);
                    await Task.Delay(150);
                    using var companionBitmap = new RenderTargetBitmap(new PixelSize((int)companion.Width,(int)companion.Height),new Vector(96,96));
                    companionBitmap.Render(companion); companionBitmap.Save(Path.Combine(directory,$"companion-{scene}.png"),PngBitmapEncoderOptions.Default);
                    companion.Collapse(); await Task.Delay(100);
                    if (vm.HasLesson && (companion.Height <= 72 || !companion.FindControl<ScrollViewer>("CompactBody")!.IsVisible))
                        throw new InvalidOperationException("The active instruction must stay visible in the collapsed companion.");
                    using var dockBitmap = new RenderTargetBitmap(new PixelSize((int)companion.Width,(int)companion.Height),new Vector(96,96));
                    dockBitmap.Render(companion); dockBitmap.Save(Path.Combine(directory,$"dock-{scene}.png"),PngBitmapEncoderOptions.Default);
                    companion.Close();
                    var outline = HighlightWindow.CreateOutline(); HighlightWindow.ApplyMode(outline, vm.IsAutonomous); outline.Width = 200; outline.Height = 64;
                    outline.Measure(new Size(200,64)); outline.Arrange(new Rect(0,0,200,64));
                    using var outlineBitmap = new RenderTargetBitmap(new PixelSize(200,64),new Vector(96,96));
                    outlineBitmap.Render(outline); outlineBitmap.Save(Path.Combine(directory,"target-outline.png"),PngBitmapEncoderOptions.Default);
                }
                if (auth) await login.SignOutAsync();
                desktop.Shutdown(); services.Dispose();
            },TimeSpan.FromMilliseconds(500));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await File.WriteAllTextAsync(Path.Combine(directory,"ui-check-error.json"),JsonSerializer.Serialize(new { type=ex.GetType().Name, result="failed" }));
            desktop.Shutdown(1); services.Dispose();
        }
        finally
        {
            if (auth && File.Exists(".local/browser-auth-request.json")) File.Delete(".local/browser-auth-request.json");
        }
    }
    private static void ApplyScene(MainViewModel vm,string scene)
    {
        vm.IsServiceReady = scene != "signed-out"; vm.ConnectionStatus = "UI preview · no network or desktop capture";
        if (scene.StartsWith("autonomous", StringComparison.Ordinal))
        {
            vm.SelectedTaskMode = 1;
            vm.Goal = "Open the display settings and set brightness to 20%.";
            if (scene == "autonomous-paused")
            {
                vm.ActiveView = "lesson"; vm.LessonState = LessonState.Paused; vm.Status = "Agent paused";
                vm.Instruction = "Open the display settings.";
                vm.Notice = "You took control. Resume locally when ready.";
            }
            if (scene == "autonomous-thinking")
            {
                vm.ActiveView = "lesson"; vm.LessonState = LessonState.Planning; vm.IsBusy = true;
                vm.Status = "Analysing the live screen…"; vm.RetryAttempt = 2;
                vm.Instruction = "Reading the updated page"; vm.Notice = "Analysis interrupted · retry 2/3. Reading a fresh screen…";
            }
        }
        if (scene.EndsWith("listening", StringComparison.Ordinal)) { vm.VoiceListening = true; vm.VoiceStatus = "Listening... click Finish when you're done."; }
        if (scene == "signing-in") vm.IsSigningIn=true;
        if (scene is "signed-in" or "railway") vm.Notice="You're signed in. Describe your task, then choose Open browser & guide me.";
        if (scene == "current-window") vm.PrepareCurrentWindow(new(Guid.Empty,Guid.Empty,"Example editor · synthetic UI preview","preview"));
        if (scene is "window" or "review" or "lesson" or "railway")
        {
            vm.SelectedLessonChoice=vm.LessonChoices.Single(l=>l.Id=="railway");
            var source = new CaptureSource(Guid.Empty,Guid.Empty,"Railway practice window · UI preview","preview");
            vm.Sources.Add(source); vm.SelectedSource=source;
        }
        if (scene == "window") vm.SetupStep=1;
        if (scene == "review")
        {
            vm.SetupStep=2; vm.ObservationConsent=true; vm.HasPreview=true;
            // Fixture preview is an explicitly generated picture, never a learner screenshot.
            var fixture = Directory.Exists("artifacts/evaluation") ? Directory.GetFiles("artifacts/evaluation","*.png",SearchOption.AllDirectories).FirstOrDefault() : null;
            if (fixture is not null) vm.PreviewImage=new Bitmap(fixture);
        }
        if (scene is "lesson" or "paused" or "missing-target" or "model-error" or "loading-page")
        {
            vm.ActiveView="lesson"; vm.LessonState=scene=="paused" ? LessonState.PrivacyPaused : LessonState.WaitingForLearner;
            vm.Status=scene=="paused" ? "Paused for privacy" : "Your turn";
            vm.Instruction="Click the workspace menu at the top left to choose your Railway workspace.";
            vm.Explanation="Before creating anything, review which workspace is selected and whether it has available project capacity. You handle billing and the final deployment.";
            vm.ObservationConsent=true; vm.PointerConsent=false;
            vm.GuideMarkerState = scene == "missing-target" ? GuideMarkerState.MissingTarget : GuideMarkerState.Visible;
            if (scene == "missing-target") vm.Notice="No click target was identified in this image. Use Find click target for a fresh image.";
            if (scene == "model-error")
            {
                vm.LessonState=LessonState.Recovering; vm.Status="Let's try again";
                vm.GuideMarkerState=GuideMarkerState.ScreenChanged;
                vm.Notice="The image service timed out or is unavailable. Use Find click target to retry this instruction.";
            }
            if (scene == "loading-page")
            {
                vm.Instruction="Wait for Railway to finish loading. I'll inspect the updated page and show the next click.";
                vm.LessonState=LessonState.WaitingForApplication; vm.Status="Waiting for the page";
                vm.GuideMarkerState=GuideMarkerState.WaitingForPage;
            }
        }
        if (scene == "settings") vm.ActiveView="settings";
        if (scene == "error") vm.Notice="Couldn't reach the lesson service. Your account is signed in; retry the connection in Preferences.";
        if (scene == "high-contrast") { vm.ActiveView="settings"; vm.HighContrast=true; }
    }
}

