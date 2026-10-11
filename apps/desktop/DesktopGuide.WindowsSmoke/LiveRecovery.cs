using System.Diagnostics;
using System.Net;
using System.Text.Json;
using DesktopGuide.App.Services;
using DesktopGuide.Core;
using DesktopGuide.Platform;
using DesktopGuide.Windows;

namespace DesktopGuide.WindowsSmoke;

// Opt-in test of the production controller, transport, capture and executor.
// The model only sees this disposable form. No target coordinates enter its prompt.
internal static class LiveRecovery
{
    private sealed class Login(string token) : ILoginSession
    {
        public bool SignedIn => true; public bool IsMock => false; public string? AccountName => null;
        public bool RememberMe { get; set; } public event Action? Changed { add { } remove { } }
        public Task SignInAsync(CancellationToken c) => throw new NotSupportedException();
        public Task SignOutAsync() => Task.CompletedTask;
        public Task<string?> GetAccessAsync(CancellationToken c) => Task.FromResult<string?>(token);
    }
    private sealed class BoundTransport : DelegatingHandler
    {
        public int ModelTurns, TransientFailures;
        public BoundTransport() : base(new HttpClientHandler()) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        {
            if (r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/turns"))
            {
                if (TransientFailures++ == 0) throw new HttpRequestException("Injected loss before upload");
                if (++ModelTurns > 3) throw new SafetyException("Approved live test turn budget reached");
            }
            return base.SendAsync(r, c);
        }
    }
    private sealed class Local : ILocalSessionStore, IAudioCapture, ISpeechPlayback
    {
        public Guid DeviceId { get; } = Guid.NewGuid(); public bool Recording => false;
        public void SaveSummary(string l, int s) { } public (string, int)? ReadSummary() => null;
        public void Clear() { } public void Start() => throw new NotSupportedException();
        public Task<byte[]> StopAsync() => throw new NotSupportedException();
        public void Cancel() { } public void Play(byte[] w) { } public void Stop() { } public void Dispose() { }
    }
    public static int Run()
    {
        var meeting = Environment.GetCommandLineArgs().Contains("--meeting-creation");
        // Access token arrives on a redirected pipe, never arguments, environment, or disk.
        using var setup = JsonDocument.Parse(Console.ReadLine() ?? throw new InvalidOperationException("Missing piped test configuration"));
        var config = PublicConfiguration.Load(setup.RootElement.GetProperty("config").GetString());
        if (config.ApiOrigin != "https://d1gnr0x6ly7io1.cloudfront.net") throw new InvalidOperationException("Unexpected pilot");
        using var transport = new BoundTransport();
        using var http = new HttpClient(transport) { BaseAddress = new(config.ApiOrigin), Timeout = TimeSpan.FromSeconds(65) };
        using var api = new GuidanceApiClient(config, new Login(setup.RootElement.GetProperty("token").GetString()!), http);
        using var form = new Form { Text = "Desktop Guide Training Sandbox - live recovery", Width = 780, Height = 460, StartPosition = FormStartPosition.CenterScreen, TopMost = true, BackColor = Color.White };
        var label = new Label { Text = "Disposable practice task", Left = 45, Top = 45, Width = 670, Height = 80, Font = new Font("Segoe UI", 24) };
        var button = new Button { Text = "Finish practice", Left = 200, Top = 220, Width = 340, Height = 85, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(25, 130, 75), ForeColor = Color.White, Font = new Font("Segoe UI", 19), TabStop = false };
        button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = button.BackColor; button.FlatAppearance.MouseDownBackColor = button.BackColor;
        form.Controls.Add(label); form.Controls.Add(button); int clicks = 0, exit = 1;
        if (meeting) { label.Text = "Disposable meeting creation test"; button.Text = "New meeting"; }
        button.Click += (_, _) =>
        {
            clicks++;
            if (meeting && clicks == 1) { label.Text = "Choose how to create your meeting"; button.Text = "Create a meeting for later"; button.Font = new Font("Segoe UI",16); }
            else { label.Text = meeting ? "Meeting link ready: meet.example.invalid/test-room" : "Practice finished successfully"; button.Visible = false; }
        };
        // A changing clock outside the click neighbourhood must not prevent stable targets.
        var clockLabel = new Label { Left=650, Top=10, Width=80, Height=20 };
        form.Controls.Add(clockLabel); var clock = new System.Windows.Forms.Timer { Interval = 300 }; var ticks = 0;
        clock.Tick += (_, _) => clockLabel.Text = (++ticks).ToString(); if (meeting) clock.Start();
        var report = new Dictionary<string, object> { ["environment"] = "deployed AWS/Gemini + real Windows capture/input; disposable test form only" };
        form.Shown += async (_, _) =>
        {
            using var capture = new WindowsCapture(); using var activity = new WindowsActivity();
            var permissions = new PermissionSupervisor(); var pointer = new WindowsPointer(permissions, capture, activity);
            var input = new WindowsInputExecutor(permissions, capture, activity, capture); var local = new Local();
            using var controller = new LessonController(capture, capture, activity, pointer, input, api, local, local, local, permissions);
            var retries = new List<int>(); controller.RetryChanged += retries.Add;
            string notice = ""; controller.Notice += text => notice = text;
            bool paused = false, stoppingInput = false; int cursorFrames = 0;
            input.CursorChanged += (_, _, phase, _) => { cursorFrames++; if (stoppingInput && phase == "moving") controller.StopLocal(); };
            controller.StateChanged += state => { if (!meeting && state == LessonState.WaitingForLearner && !paused) { paused = true; controller.PauseLocal(); } };
            try
            {
                await Task.Delay(500); form.Activate(); await Task.Delay(200);
                var source = capture.RegisterOwnTestWindow(form.Handle, form.Text);
                await controller.PreviewAsync(source);
                await controller.StartAsync(source, meeting ? "Create a meeting link for later using this disposable practice application. Finish when the meeting link is visible." : "Click Finish practice once and confirm that Practice finished successfully is visible.", "browser", InteractionMode.Autonomous, false, false, false, true);
                if (!meeting)
                {
                    if (!paused || clicks != 0 || controller.State != LessonState.Paused) throw new InvalidOperationException("Pause before dispatch failed");
                    report["pause_before_dispatch"] = true; report["automatic_transport_retry"] = retries.Contains(1);
                    await controller.ResumeAsync(true);
                }
                if (controller.State != LessonState.Completed || clicks != (meeting ? 2 : 1) || controller.Mode != InteractionMode.Autonomous) throw new InvalidOperationException("Live task did not complete: " + controller.State);
                report[meeting ? "meeting_creation_without_manual_handoff" : "live_model_resume_and_verified_click"] = true;
                report["verified_clicks"] = clicks;
                report["automatic_transport_retry"] = retries.Contains(1);
                report["autonomous_mode_preserved"] = true;
                report["controlled_cursor_animation_events"] = cursorFrames;
                // Stop during the first movement batch of a second, synthetic local approval.
                // This oracle is exclusively a safety test and is never given to the model.
                permissions.GrantAutonomousTaskFromLocalConsent(source.ScopeId, source.TargetId, true);
                activity.ObservationEnabled = true; activity.AutonomousControlActive = true;
                var frame = await capture.CaptureAsync(source, [], permissions.Cancellation);
                var action = new ActionProposal(Guid.NewGuid(), frame.Observation.Id, frame.Geometry.Id, new("click", .4, .4, "left"), "Synthetic Stop check", "low", false);
                var approval = permissions.ApproveAutonomousAction(action, frame); stoppingInput = true;
                var timer = Stopwatch.StartNew();
                var result = await input.ExecuteAsync(approval, action, frame, source, permissions.Cancellation); timer.Stop();
                if (result.Outcome != "not_dispatched" || controller.State != LessonState.Stopped || clicks != (meeting ? 2 : 1) || capture.CaptureSessionActive) throw new InvalidOperationException("Stop during movement failed");
                report["stop_during_real_movement_no_click"] = true; report["stop_test_ms"] = timer.Elapsed.TotalMilliseconds;
                report["capture_released_on_stop"] = true; report["result"] = "passed"; exit = 0;
            }
            catch (Exception e) { report["state"] = controller.State.ToString(); report["local_notice"] = notice; controller.StopLocal(); report["result"] = "failed"; report["diagnostic"] = e.GetType().Name + ": " + e.Message; report["clicks"] = clicks; }
            finally { clock.Stop(); clock.Dispose(); input.ReleaseHeldControls(); report["model_turns"] = Math.Min(3, transport.ModelTurns); Console.WriteLine(JsonSerializer.Serialize(report)); form.Close(); }
        };
        Application.Run(form); return exit;
    }
}
