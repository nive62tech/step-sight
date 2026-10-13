using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using DesktopGuide.Windows;

namespace DesktopGuide.WindowsSmoke;

// Captures only known swatches in disposable fixture windows; no model, user
// media, cursor movement, clicks, focus authorization or recording file.
internal static class RecordingVisibility
{
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
    public static int Run()
    {
        WindowStyles.EnableDpiAwareness();
        var report = new Dictionary<string, object> { ["scope"] = "synthetic fixture windows only; no input or model", ["real_input"] = false };
        using var fixture = new Form { Text = "Desktop Guide Training Sandbox — recording visibility", FormBorderStyle = FormBorderStyle.None,
            BackColor = Color.White, Width = 620, Height = 420, TopMost = true, StartPosition = FormStartPosition.Manual,
            Location = new(Screen.PrimaryScreen!.WorkingArea.Left + 48, Screen.PrimaryScreen.WorkingArea.Top + 48) };
        var exit = 1;
        fixture.Shown += async (_, _) =>
        {
            using var capture = new WindowsCapture();
            try
            {
                var source = capture.RegisterOwnTestWindow(fixture.Handle, fixture.Text);
                foreach (var surface in new[] { "blob_and_dock", "cursor_and_outline", "approval_card" })
                {
                    using var helper = new Form { Text = "Synthetic helper", FormBorderStyle = FormBorderStyle.None, BackColor = Color.Magenta,
                        Width = 180, Height = 100, Opacity = .98, TopMost = true, StartPosition = FormStartPosition.Manual,
                        Location = fixture.PointToScreen(new(180,140)) };
                    helper.Show();
                    if (surface == "cursor_and_outline") WindowStyles.ConfigureOverlay(helper.Handle);
                    else if (surface == "approval_card") WindowStyles.ConfigureApproval(helper.Handle);
                    else if (!WindowStyles.ShowInRecordings(helper.Handle)) throw new Exception("Recording visibility refused");
                    await Task.Delay(350);
                    if (!GetWindowDisplayAffinity(helper.Handle, out var affinity) || affinity != 0) throw new Exception("Capture exclusion is still enabled");
                    var point = helper.PointToScreen(new(90,50));
                    using var recorded = new Bitmap(5,5,PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(recorded)) g.CopyFromScreen(point.X,point.Y,0,0,recorded.Size,CopyPixelOperation.SourceCopy);
                    var visible = recorded.GetPixel(2,2);
                    if (visible.R < 245 || visible.G > 10 || visible.B < 245) throw new Exception("The helper is missing from desktop recording capture");
                    var frame = await capture.CaptureAsync(source, [], CancellationToken.None);
                    using var isolated = new Bitmap(new MemoryStream(frame.Png));
                    var x = (int)((point.X-frame.Geometry.Source.Left)*isolated.Width/(double)frame.Geometry.Source.Width);
                    var y = (int)((point.Y-frame.Geometry.Source.Top)*isolated.Height/(double)frame.Geometry.Source.Height);
                    var taskPixel = isolated.GetPixel(x,y);
                    if (taskPixel.R < 245 || taskPixel.G < 245 || taskPixel.B < 245) throw new Exception("The helper leaked into the isolated agent window image");
                    report[surface] = new { display_affinity=affinity, visible_in_desktop_capture=true, excluded_from_agent_window_image=true };
                }
                report["result"] = "passed"; exit = 0;
            }
            catch (Exception ex) { report["result"] = "failed"; report["error"] = ex.GetType().Name + ": " + ex.Message; }
            finally { capture.StopCapture(); fixture.Close(); }
        };
        Application.Run(fixture);
        Directory.CreateDirectory("artifacts");
        File.WriteAllText("artifacts/windows-recording-visibility-r21.json", JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine(JsonSerializer.Serialize(report)); return exit;
    }
}
