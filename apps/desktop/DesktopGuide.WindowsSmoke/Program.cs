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
        if (args.Contains("--capture-only")) return CaptureOnly();
        if (!args.Contains("--allow-real-input") || !args.Contains("--disposable-session"))
        {
            Console.Error.WriteLine("Not run. Requires --allow-real-input and --disposable-session. This test moves the real cursor and clicks its own disposable window once.");
            return 2;
        }
        WindowStyles.EnableDpiAwareness();
        if (args.Contains("--live-recovery")) return LiveRecovery.Run();
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
    private static int CaptureOnly()
    {
        WindowStyles.EnableDpiAwareness();
        var report = new Dictionary<string, object> { ["date"] = DateTimeOffset.UtcNow, ["scope"] = "own synthetic fixture windows only", ["real_input"] = false };
        using var form = new Form { Text = "Desktop Guide Training Sandbox — capture fixture", Width = 700, Height = 420, StartPosition = FormStartPosition.CenterScreen, TopMost = true };
        var label = new Label { Text = "Capture reuse and visual-only cursor fixture", Left = 30, Top = 35, Width = 590 };
        form.Controls.Add(label); int exit = 1;
        form.Shown += async (_, _) =>
        {
            using var capture = new WindowsCapture(); using var activity = new WindowsActivity();
            var permissions = new PermissionSupervisor();
            try
            {
                await Task.Delay(350);
                var source = capture.RegisterOwnTestWindow(form.Handle, form.Text);
                permissions.GrantFromLocalConsent(source.ScopeId, InteractionMode.Guide, true, false, false);
                var first = await capture.CaptureAsync(source, [], permissions.Cancellation);
                for (var i = 0; i < 4; i++) await capture.CaptureAsync(source, [], permissions.Cancellation);
                if (capture.CaptureSessionStarts != 1) throw new Exception("Capture session was recreated between snapshots");
                report["session_reused_for_five_snapshots"] = true;
                using (var second = new Form { Text = "Desktop Guide Training Sandbox — foreground fixture", Width = 300, Height = 200 })
                {
                    second.Show(); second.Activate(); await Task.Delay(250);
                    var background = await capture.CaptureAsync(source, [], permissions.Cancellation);
                    if (first.Geometry != background.Geometry) throw new Exception("Source identity changed with foreground window");
                    report["capture_identity_independent_of_foreground"] = true;
                    second.Close();
                }
                form.Activate(); await Task.Delay(200);
                form.Width += 80; await Task.Delay(500);
                var resized = await capture.CaptureAsync(source, [], permissions.Cancellation);
                if (capture.CaptureSessionStarts != 2 || resized.Geometry.Source.Width == first.Geometry.Source.Width) throw new Exception("Resize did not refresh capture geometry");
                report["resize_recovers_with_one_session_restart"] = true;
                var pointer = new WindowsPointer(permissions, capture, activity);
                var travel = 0; var shown = 0;
                pointer.TravelShown += _ => travel++;
                pointer.TargetShown += _ => shown++;
                var target = new Target("rectangle", "normalized_source_image", resized.Geometry.Id, new(.65, .6, .8, .75), "Synthetic target");
                form.Activate(); await Task.Delay(200);
                var point = resized.Geometry.Map(.725, .675);
                report["foreground"] = capture.IsForegroundAndVisible(source);
                report["geometry_current"] = capture.IsCurrentGeometry(source, resized.Geometry);
                report["target_visible"] = capture.IsPointVisible(source, point.X, point.Y);
                if (capture.IsForegroundAndVisible(source))
                {
                    await pointer.ShowVisualAsync(resized, target, source, permissions.Cancellation);
                    if (travel != 23 || shown != 1 || activity.IsAssistantInputActive) throw new Exception("Visual marker animation failed");
                    report["native_visual_animation"] = "passed";
                }
                else
                {
                    try { await pointer.ShowVisualAsync(resized, target, source, permissions.Cancellation); throw new Exception("Background target was accepted"); }
                    catch (SafetyException) { report["native_visual_animation"] = "not exercised: fixture could not obtain foreground; guard correctly rejected it"; }
                }
                permissions.Revoke(); capture.StopCapture(); pointer.Hide();
                if (capture.CaptureSessionActive) throw new Exception("Capture remained active after stop");
                report["stop_releases_capture"] = true; report["result"] = "passed"; exit = 0;
            }
            catch (Exception ex) { report["result"] = "failed"; report["error"] = ex.GetType().Name + ": " + ex.Message; }
            finally
            {
                Directory.CreateDirectory("artifacts");
                File.WriteAllText("artifacts/windows-capture-only.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                form.Close();
            }
        };
        Application.Run(form);
        Console.WriteLine(JsonSerializer.Serialize(report)); return exit;
    }
}
