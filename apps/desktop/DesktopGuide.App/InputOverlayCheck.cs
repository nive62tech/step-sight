using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;
using DesktopGuide.Core;
using DesktopGuide.Windows;
using System.Text.Json;

namespace DesktopGuide.App;

// Explicitly consented native regression: our own disposable window and real product overlay.
// Coordinates are an isolated test oracle, never available to production model guidance.
internal static class InputOverlayCheck
{
    public static void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var button = new Button { Content = "New meeting", Width = 240, Height = 72, Background = Brushes.DarkGreen, Foreground = Brushes.White, FontSize = 22 };
        // No hover change in this case: isolate native overlay occlusion from screenshot freshness.
        button.Styles.Add(new Style(x => x.OfType<Button>().Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        { Setters = { new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, Brushes.DarkGreen) } });
        var label = new TextBlock { Text = "Disposable meeting creation test", FontSize = 24, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        var panel = new StackPanel { Spacing = 50, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Children = { label, button } };
        var window = new Window { Title = "Desktop Guide Training Sandbox - overlay click", Width = 760, Height = 460, Background = Brushes.White, Content = panel, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var overlay = new OverlayWindow(); desktop.MainWindow = window;
        var clicked = 0; button.Click += (_, _) => { clicked++; label.Text = "Meeting created in disposable test"; };
        window.Opened += async (_, _) =>
        {
            using var capture = new WindowsCapture(); using var activity = new WindowsActivity();
            var permissions = new PermissionSupervisor(); var input = new WindowsInputExecutor(permissions, capture, activity, capture);
            activity.Takeover += permissions.Revoke; activity.EmergencyStop += permissions.Revoke; activity.SessionUnavailable += permissions.Revoke;
            var report = new Dictionary<string, object> { ["scope"] = "own disposable Avalonia window; real product cursor overlay; no model", ["clicks"] = 0 };
            input.CursorChanged += (x, y, phase, epoch) => Dispatcher.UIThread.Post(() => { if (epoch == permissions.Epoch) { if (phase == "moving") overlay.ShowTravel((x, y)); else overlay.ShowTarget((x, y), "Creating a disposable meeting", "agent_click", false); } });
            input.CursorHidden += _ => Dispatcher.UIThread.Post(overlay.Hide);
            try
            {
                await Task.Delay(500); window.Activate(); await Task.Delay(200);
                var source = capture.RegisterOwnTestWindow(window.TryGetPlatformHandle()!.Handle, window.Title!);
                permissions.GrantAutonomousTaskFromLocalConsent(source.ScopeId, source.TargetId, true);
                activity.ObservationEnabled = activity.AutonomousControlActive = true;
                var frame = await capture.CaptureAsync(source, [], permissions.Cancellation);
                var point = button.PointToScreen(new Point(button.Bounds.Width/2, button.Bounds.Height/2));
                var x = (point.X-frame.Geometry.Source.Left)/(double)(frame.Geometry.Source.Width-1);
                var y = (point.Y-frame.Geometry.Source.Top)/(double)(frame.Geometry.Source.Height-1);
                overlay.ShowTravel((point.X,point.Y)); await Task.Delay(150);
                report["point_visible_with_overlay"] = capture.IsPointVisible(source, point.X, point.Y);
                report["capture_changed_with_overlay"] = (await capture.CaptureAsync(source, [], permissions.Cancellation)).PixelDigest != frame.PixelDigest;
                overlay.Hide(); await Task.Delay(100);
                var action = new ActionProposal(Guid.NewGuid(), frame.Observation.Id, frame.Geometry.Id, new("click",x,y,"left"), "Create disposable meeting", "low", false);
                var result = await input.ExecuteAsync(permissions.ApproveAutonomousAction(action, frame), action, frame, source, permissions.Cancellation);
                report["first_deferral"] = input.LastDeferralReason ?? "none";
                if (result.Outcome == "not_dispatched")
                {
                    overlay.Hide(); await Task.Delay(250);
                    frame = await capture.CaptureAsync(source, [], permissions.Cancellation);
                    action = action with { Id = Guid.NewGuid(), SourceObservationId = frame.Observation.Id, GeometryId = frame.Geometry.Id };
                    result = await input.ExecuteAsync(permissions.ApproveAutonomousAction(action, frame), action, frame, source, permissions.Cancellation);
                }
                await Task.Delay(100); report["outcome"] = result.Outcome; report["clicks"] = clicked;
                report["last_deferral"] = input.LastDeferralReason ?? "none";
                report["result"] = result.Outcome == "dispatched" && clicked == 1 ? "passed" : "failed";
            }
            catch (Exception e) { report["result"] = "failed"; report["error"] = e.GetType().Name+": "+e.Message; }
            finally
            {
                permissions.Revoke(); input.ReleaseHeldControls(); capture.StopCapture(); overlay.Close();
                var path = UiCheck.Argument("--report", Path.Combine(Path.GetTempPath(), "desktop-guide-input-overlay.json"));
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report)); desktop.Shutdown(report["result"].Equals("passed") ? 0 : 1);
            }
        };
    }
}
