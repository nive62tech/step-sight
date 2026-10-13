using System.Diagnostics;
using System.Text.Json;
using DesktopGuide.Core;
using DesktopGuide.Windows;

namespace DesktopGuide.WindowsSmoke;

internal static class CrossProcessOverlay
{
    public static int Run(string appPath)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.GetFullPath(appPath)); start.ArgumentList.Add("--overlay-test-host"); start.ArgumentList.Add("--disposable-session");
        using var host = Process.Start(start) ?? throw new InvalidOperationException("Could not start overlay test host");
        void Draw(string kind, int x=0, int y=0)
        {
            host.StandardInput.WriteLine(JsonSerializer.Serialize(new { kind,x,y })); host.StandardInput.Flush();
            var reply = host.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            if (reply != "ready") throw new InvalidOperationException("Overlay rendering host did not acknowledge");
        }
        using var form = new Form { Text="Desktop Guide Training Sandbox - cross-process overlay", Width=780, Height=460,
            StartPosition=FormStartPosition.CenterScreen, TopMost=true, BackColor=Color.White };
        var button = new Button { Text="New meeting", Left=220, Top=200, Width=300, Height=80, FlatStyle=FlatStyle.Flat,
            BackColor=Color.DarkGreen, ForeColor=Color.White, Font=new Font("Segoe UI",20), TabStop=false };
        button.FlatAppearance.MouseOverBackColor=button.BackColor; button.FlatAppearance.BorderSize=0;
        var clicks=0; button.Click += (_,_) => clicks++; form.Controls.Add(button);
        var report = new Dictionary<string,object> { ["scope"]="own disposable WinForms target; real Avalonia overlay in a separate process; no model" };
        var exit=1;
        form.Shown += async (_,_) =>
        {
            using var capture = new WindowsCapture(); using var activity = new WindowsActivity();
            var permissions = new PermissionSupervisor(); var executor = new WindowsInputExecutor(permissions,capture,activity,capture);
            var clears=0;
            executor.ClearCursorAsync = token => { token.ThrowIfCancellationRequested(); Draw("hide"); clears++; return Task.CompletedTask; };
            var takeovers=0;
            activity.Takeover += () => { Interlocked.Increment(ref takeovers); permissions.Revoke(); };
            activity.EmergencyStop += permissions.Revoke; activity.SessionUnavailable += permissions.Revoke;
            executor.CursorChanged += (x,y,phase,_) => Draw(phase=="moving" ? "travel" : "target",x,y);
            executor.CursorHidden += _ => Draw("hide");
            try
            {
                // Finish child startup before establishing the target's foreground grant.
                Draw("hide"); await Task.Delay(200); form.Activate(); await Task.Delay(200);
                var source=capture.RegisterOwnTestWindow(form.Handle,form.Text);
                capture.FocusSource(source);
                for (var focusTry=0; focusTry<20 && !capture.IsForegroundAndVisible(source); focusTry++)
                { form.Activate(); await Task.Delay(250); }
                permissions.GrantAutonomousTaskFromLocalConsent(source.ScopeId,source.TargetId,true);
                // Creating the fixture/renderer is setup; arm takeover detection
                // for the input operation only after that setup has settled.
                activity.ObservationEnabled=true;
                var frame=await capture.CaptureAsync(source,[],permissions.Cancellation);
                var point=button.PointToScreen(new Point(button.Width/2,button.Height/2));
                report["foreground_before_overlay"]=capture.IsForegroundAndVisible(source);
                report["target_visible_before_overlay"]=capture.IsPointVisible(source,point.X,point.Y);
                if (!capture.IsForegroundAndVisible(source) || !capture.IsPointVisible(source,point.X,point.Y))
                    throw new InvalidOperationException("Disposable target was not foreground/uncovered before drawing the overlay");
                Draw("target",point.X,point.Y); await Task.Delay(150);
                report["foreground_with_overlay"]=capture.IsForegroundAndVisible(source);
                report["target_visible_with_overlay"]=capture.IsPointVisible(source,point.X,point.Y);
                var x=(point.X-frame.Geometry.Source.Left)/(double)(frame.Geometry.Source.Width-1);
                var y=(point.Y-frame.Geometry.Source.Top)/(double)(frame.Geometry.Source.Height-1);
                var action=new ActionProposal(Guid.NewGuid(),frame.Observation.Id,frame.Geometry.Id,new("click",x,y,"left"),"Create disposable meeting","low",false);
                activity.AutonomousControlActive=true;
                var result=await executor.ExecuteAsync(permissions.ApproveAutonomousAction(action,frame),action,frame,source,permissions.Cancellation);
                await Task.Delay(100); report["outcome"]=result.Outcome; report["deferral"]=executor.LastDeferralReason ?? "none";
                report["clicks"]=clicks; report["result"]=result.Outcome=="dispatched" && clicks==1 ? "passed" : "failed";
                exit=report["result"].Equals("passed") ? 0 : 1;
            }
            catch (Exception e) { report["result"]="failed"; report["error"]=e.Message; }
            finally
            {
                permissions.Revoke(); executor.ReleaseHeldControls(); capture.StopCapture();
                report["physical_takeovers"]=takeovers;
                report["awaited_overlay_clears"]=clears;
                Draw("quit"); Console.WriteLine(JsonSerializer.Serialize(report)); form.Close();
            }
        };
        Application.Run(form);
        if (!host.WaitForExit(5000)) host.Kill();
        return exit;
    }
}
