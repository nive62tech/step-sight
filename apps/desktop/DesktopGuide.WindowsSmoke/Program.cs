using System.Diagnostics;
using System.Text.Json;
using DesktopGuide.Core;
using DesktopGuide.Windows;

namespace DesktopGuide.WindowsSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!args.Contains("--allow-real-input") || !args.Contains("--disposable-session"))
        {
            Console.Error.WriteLine("Not run. Requires --allow-real-input and --disposable-session. This test moves the real cursor and clicks its own disposable window once.");
            return 2;
        }
        WindowStyles.EnableDpiAwareness();
        var report = new Dictionary<string, object> { ["date"] = DateTimeOffset.UtcNow, ["provider"] = "synthetic fixture; no model", ["scope"] = "test window only" };
        var title = "Desktop Guide Training Sandbox — smoke " + Guid.NewGuid().ToString("N");
        using var form = new Form { Text = title, Width = 650, Height = 390, StartPosition = FormStartPosition.CenterScreen };
        var button = new Button { Text = "Save practice", Left = 70, Top = 140, Width = 210, Height = 55 };
        var label = new Label { Text = "Explicit disposable smoke: one real cursor movement and click.\nUse Ctrl+Alt+Shift+S or move the mouse to stop.", Left = 35, Top = 35, Width = 550, Height = 65 };
        bool clicked = false;
        button.Click += (_, _) => { clicked = true; label.Text = "Practice saved — test-window event confirmed"; };
        form.Controls.Add(button); form.Controls.Add(label);
        int exit = 1;
        form.Shown += async (_, _) =>
        {
            using var capture = new WindowsCapture(); using var activity = new WindowsActivity();
            var permissions = new PermissionSupervisor();
            activity.Takeover += permissions.Revoke; activity.EmergencyStop += permissions.Revoke;
            activity.SessionUnavailable += permissions.Revoke;
            var pointer = new WindowsPointer(permissions, capture, activity);
            var input = new WindowsInputExecutor(permissions, capture, activity, capture);
            try
            {
                await Task.Delay(300); form.Activate();
                report["stage"] = "register own disposable window";
                var source = capture.RegisterOwnTestWindow(form.Handle, title);
                permissions.GrantFromLocalConsent(source.ScopeId, InteractionMode.Guide, true, true, false);
                activity.ObservationEnabled = true;
                report["stage"] = "WGC capture";
                var frame = await capture.CaptureAsync(source, [], permissions.Cancellation);
                report["window_capture"] = "passed";
                // Isolated test oracle supplies a fixed test target, never exposed to the production assistant/model.
                var point = button.PointToScreen(new Point(button.Width / 2, button.Height / 2));
                double x = (point.X - frame.Geometry.Source.Left) / (double)(frame.Geometry.Source.Width - 1);
                double y = (point.Y - frame.Geometry.Source.Top) / (double)(frame.Geometry.Source.Height - 1);
                var target = new Target("rectangle", "normalized_source_image", frame.Geometry.Id, new(x, y, x, y), "Save practice");
                report["observer_available"] = activity.TrackingAvailable;
                report["foreground"] = capture.IsForegroundAndVisible(source);
                report["current_geometry"] = capture.IsCurrentGeometry(source, frame.Geometry);
                report["point_visible"] = capture.IsPointVisible(source, point.X, point.Y);
                report["stage"] = "pointer";
                await pointer.PointAsync(frame, target, source, permissions.Cancellation);
                report["real_pointer"] = "passed";
                var current = await capture.CaptureAsync(source, [], permissions.Cancellation);
                var proposal = new ActionProposal(Guid.NewGuid(), current.Observation.Id, current.Geometry.Id, new("click", x, y, "left"), "Click this test window's Save practice once", "low", false);
                try { permissions.ApproveFromLocalEvent(proposal, current); throw new InvalidOperationException("Guide mode authorized input"); }
                catch (SafetyException) { report["guide_input_rejected"] = "passed"; }
                permissions.GrantFromLocalConsent(source.ScopeId, InteractionMode.Demonstrate, true, true, true);
                var capability = permissions.ApproveFromLocalEvent(proposal, current);
                report["stage"] = "single click";
                var receipt = await input.ExecuteAsync(capability, proposal, current, source, permissions.Cancellation);
                await Task.Delay(200);
                if (receipt.Outcome != "dispatched" || !clicked) throw new InvalidOperationException("Practice click not established");
                report["single_real_click"] = "passed";
                var after = await capture.CaptureAsync(source, [], permissions.Cancellation);
                if (after.PixelDigest == current.PixelDigest) throw new InvalidOperationException("Visible outcome did not change");
                report["visible_outcome_change"] = "passed; isolated oracle confirms test target only";
                var stopwatch = Stopwatch.StartNew(); permissions.Revoke(); stopwatch.Stop();
                report["local_stop_invalidation_ms"] = stopwatch.Elapsed.TotalMilliseconds;
                try { permissions.ApproveFromLocalEvent(proposal, after); throw new InvalidOperationException("Stop did not revoke input"); }
                catch (SafetyException) { report["stop_disarmed"] = "passed"; }
                report["hotkey_registered"] = activity.HotkeyRegistered;
                report["result"] = "passed"; exit = 0;
            }
            catch (Exception ex) { permissions.Revoke(); report["result"] = "failed"; report["error_type"] = ex.GetType().Name; report["test_window_clicked"] = clicked; report["diagnostic"] = ex.Message; }
            finally
            {
                input.ReleaseHeldControls(); activity.ObservationEnabled = false;
                Directory.CreateDirectory("artifacts");
                File.WriteAllText("artifacts/windows-smoke.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                form.Close();
            }
        };
        Application.Run(form);
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return exit;
    }
}
