using System.Runtime.InteropServices;
using DesktopGuide.Core;
using DesktopGuide.Platform;

namespace DesktopGuide.Windows;

internal static class InputNative
{
    internal static void Send(params Native.Input[] input)
    {
        if (Native.SendInput((uint)input.Length, input, Marshal.SizeOf<Native.Input>()) != input.Length)
            throw new SafetyException("Windows input was refused; complete the action manually");
    }
    internal static Native.Input Mouse(uint flags, int x = 0, int y = 0, uint data = 0)
        => new() { Type = 0, Union = new() { Mouse = new() { Dx = x, Dy = y, Data = data, Flags = flags, ExtraInfo = Native.InputTag } } };
    internal static Native.Input Key(ushort key, bool up = false)
        => new() { Type = 1, Union = new() { Key = new() { Vk = key, Flags = up ? 2u : 0u, ExtraInfo = Native.InputTag } } };
    internal static Native.Input Unicode(char character, bool up)
        => new() { Type = 1, Union = new() { Key = new() { Scan = character, Flags = 4u | (up ? 2u : 0u), ExtraInfo = Native.InputTag } } };
    internal static void Move(int x, int y)
    {
        int left = Native.GetSystemMetrics(76), top = Native.GetSystemMetrics(77), width = Native.GetSystemMetrics(78), height = Native.GetSystemMetrics(79);
        if (width <= 1 || height <= 1 || x < left || y < top || x >= left + width || y >= top + height)
            throw new SafetyException("Pointer destination is outside the virtual desktop");
        int dx = (int)Math.Round((x - left) * 65535d / (width - 1));
        int dy = (int)Math.Round((y - top) * 65535d / (height - 1));
        Send(Mouse(0x0001 | 0x8000 | 0x4000, dx, dy));
    }
}

public sealed class WindowsPointer(PermissionSupervisor supervisor, IWindowTracker tracker, IUserActivityObserver activity) : IPointerGuide
{
    private readonly object visibilityGate = new();
    private long visibilityVersion;
    private System.Threading.Timer? visibilityWatch;
    public event Action<(int X, int Y)>? TravelShown;
    public event Action<(int X, int Y)>? TargetShown;
    public event Action? TargetHidden;
    public event Action? VisibilityLost;
    public bool ReducedMotion { get; set; }
    public string Instruction { get; private set; } = "";
    public string ExpectedAction { get; private set; } = "click";
    public PhysicalRect? TargetBounds { get; private set; }
    public Func<PhysicalRect, CancellationToken, Task>? PrepareTargetAsync { get; set; }
    private void SetBounds(ScreenFrame frame, Target target)
    {
        var a = frame.Geometry.Map(target.Bounds.Left, target.Bounds.Top);
        var b = frame.Geometry.Map(target.Bounds.Right, target.Bounds.Bottom);
        TargetBounds = new(a.X, a.Y, Math.Max(1, b.X - a.X + 1), Math.Max(1, b.Y - a.Y + 1));
    }
    public void SetHint(string instruction, string expectedAction) { Instruction = instruction; ExpectedAction = expectedAction; }
    // Moves only our instruction dock, never the user's physical pointer or browser controls.
    // Browser grounding calls this before its element hit-test so our own dock cannot block it.
    public async Task PrepareVisualTargetAsync(ScreenFrame frame, Target target, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        target.Validate(frame.Geometry.Id); supervisor.RequireObservation(frame.Observation.ScopeId);
        if (PrepareTargetAsync is not { } prepare) return;
        var a = frame.Geometry.Map(target.Bounds.Left, target.Bounds.Top);
        var b = frame.Geometry.Map(target.Bounds.Right, target.Bounds.Bottom);
        await prepare(new(a.X, a.Y, Math.Max(1, b.X - a.X + 1), Math.Max(1, b.Y - a.Y + 1)), cancellation);
        cancellation.ThrowIfCancellationRequested(); supervisor.RequireObservation(frame.Observation.ScopeId);
    }
    public async Task ShowVisualAsync(ScreenFrame frame, Target target, CaptureSource source, CancellationToken cancellation)
    {
        target.Validate(frame.Geometry.Id);
        var (nx, ny) = target.Bounds.Center; frame.RequireUnmaskedPoint(nx, ny);
        var destination = frame.Geometry.Map(nx, ny);
        supervisor.RequireObservation(source.ScopeId);
        await PrepareVisualTargetAsync(frame, target, cancellation);
        if (!tracker.IsCurrentGeometry(source, frame.Geometry) || !tracker.IsPointVisible(source, destination.X, destination.Y))
            throw new SafetyException("Focus the browser window to see the guide marker.");
        // This is a drawn guide icon only. It never reads or moves the physical cursor.
        var start = frame.Geometry.Map(Math.Clamp(nx - .12, .02, .98), Math.Clamp(ny + .10, .02, .98));
        try
        {
            int segments = ReducedMotion ? 1 : 24;
            for (int i = 1; i <= segments; i++)
            {
                cancellation.ThrowIfCancellationRequested(); supervisor.RequireObservation(source.ScopeId);
                if (!tracker.IsCurrentGeometry(source, frame.Geometry)) throw new SafetyException("Browser position changed. Check the screen again.");
                var t = 1 - Math.Pow(1 - i / (double)segments, 3);
                if (i < segments)
                {
                    TravelShown?.Invoke(((int)Math.Round(start.X + (destination.X - start.X) * t), (int)Math.Round(start.Y + (destination.Y - start.Y) * t)));
                    await Task.Delay(16, cancellation);
                }
            }
            cancellation.ThrowIfCancellationRequested(); supervisor.RequireObservation(source.ScopeId);
            DisplayTarget(frame, target, source, destination);
        }
        catch { Hide(); throw; }
    }
    public async Task PointAsync(ScreenFrame frame, Target target, CaptureSource source, CancellationToken cancellation)
    {
        target.Validate(frame.Geometry.Id);
        var (nx, ny) = target.Bounds.Center; frame.RequireUnmaskedPoint(nx, ny); var destination = frame.Geometry.Map(nx, ny);
        if (!activity.TrackingAvailable || !tracker.IsCurrentGeometry(source, frame.Geometry) || !tracker.IsPointVisible(source, destination.X, destination.Y))
            throw new SafetyException("Target is covered or not foreground; focus the selected application and reobserve");
        if (!Native.GetCursorPos(out var start)) throw new SafetyException("Pointer unavailable");
        activity.IsAssistantInputActive = true;
        try
        {
            int segments = ReducedMotion ? 1 : 24;
            for (int i = 1; i <= segments; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!activity.TrackingAvailable) throw new SafetyException("Input observer unavailable");
                if (!tracker.IsCurrentGeometry(source, frame.Geometry)) throw new SafetyException("Target geometry changed during pointing");
                int x = (int)Math.Round(start.X + (destination.X - start.X) * i / (double)segments);
                int y = (int)Math.Round(start.Y + (destination.Y - start.Y) * i / (double)segments);
                supervisor.GuideBatch(frame, tracker.IsForegroundAndVisible(source), () => InputNative.Move(x, y));
                if (i < segments) { TravelShown?.Invoke((x, y)); await Task.Delay(16, cancellation); }
            }
            cancellation.ThrowIfCancellationRequested();
            DisplayTarget(frame, target, source, destination);
        }
        catch { Hide(); throw; }
        finally { activity.IsAssistantInputActive = false; }
    }
    private void DisplayTarget(ScreenFrame frame, Target target, CaptureSource source, (int X, int Y) destination)
    {
        lock (visibilityGate)
        {
            var epoch = supervisor.Epoch; var version = ++visibilityVersion;
            SetBounds(frame, target); TargetShown?.Invoke(destination);
            visibilityWatch?.Dispose();
            visibilityWatch = new System.Threading.Timer(_ =>
            {
                lock (visibilityGate)
                {
                    // A queued callback belonging to an old target must not hide its replacement.
                    if (version != visibilityVersion) return;
                    try
                    {
                        if (supervisor.Epoch != epoch) Hide();
                        else if (!tracker.IsCurrentGeometry(source, frame.Geometry)) { Hide(); VisibilityLost?.Invoke(); }
                    }
                    catch { Hide(); VisibilityLost?.Invoke(); }
                }
            }, null, 100, 100);
        }
    }
    public void Hide()
    {
        lock (visibilityGate)
        {
            visibilityVersion++; visibilityWatch?.Dispose(); visibilityWatch = null;
            TargetBounds = null; TargetHidden?.Invoke();
        }
    }
}

public sealed class WindowsInputExecutor(PermissionSupervisor supervisor, IWindowTracker tracker,
    IUserActivityObserver activity, IScreenCapture capture) : IInputExecutor
{
    public string? LastDeferralReason { get; private set; }
    public bool ReducedMotion { get; set; }
    public event Action<int, int, string, long>? CursorChanged;
    public event Action<long>? CursorHidden;
    private readonly SemaphoreSlim serialized = new(1);
    private readonly object heldGate = new();
    private readonly HashSet<ushort> heldKeys = [];
    private char? heldUnicode;
    private bool heldButton;
    public async Task<ActionReceipt> ExecuteAsync(LocalCapability capability, ActionProposal proposal, ScreenFrame frame,
        CaptureSource source, CancellationToken cancellation)
    {
        var inputEpoch = supervisor.Epoch;
        await serialized.WaitAsync(cancellation);
        bool dispatched = false;
        LastDeferralReason = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!tracker.IsCurrentGeometry(source, frame.Geometry)) throw new SafetyException("Target changed before demonstration");
            var lease = supervisor.Consume(capability, proposal, frame, tracker.IsForegroundAndVisible(source));
            activity.IsAssistantInputActive = true;
            void Batch(Action send, bool changesApplication = true)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!activity.TrackingAvailable) throw new SafetyException("Input observer unavailable");
                if (!tracker.IsCurrentGeometry(source, frame.Geometry)) throw new SafetyException("Target changed during input");
                supervisor.ExecuteBatch(lease, frame, tracker.IsForegroundAndVisible(source), () =>
                {
                    if (changesApplication) dispatched = true;
                    send();
                });
            }
            var action = proposal.Action;
            if (action.Kind is "click" or "scroll")
            {
                frame.RequireUnmaskedPoint(action.X!.Value, action.Y!.Value);
                var point = frame.Geometry.Map(action.X!.Value, action.Y!.Value);
                if (!tracker.IsPointVisible(source, point.X, point.Y)) throw new SafetyException("Action target is covered");
                if (!Native.GetCursorPos(out var start)) throw new SafetyException("Pointer unavailable");
                var segments = ReducedMotion ? 1 : 14;
                for (var i = 1; i <= segments; i++)
                {
                    var t = i / (double)segments; var ease = t * t * (3 - 2 * t);
                    var x = (int)Math.Round(start.X + (point.X-start.X)*ease);
                    var y = (int)Math.Round(start.Y + (point.Y-start.Y)*ease);
                    Batch(() => InputNative.Move(x, y), changesApplication: false);
                    CursorChanged?.Invoke(x, y, "moving", supervisor.Epoch);
                    if (i < segments) await Task.Delay(16, cancellation);
                }
                // Hover can replace a control inside the same root window. Hit testing alone cannot detect that.
                await Task.Delay(80, cancellation);
                var afterHover = await capture.CaptureAsync(source, frame.PrivacyMasks, cancellation);
                if (!tracker.IsActionTargetCurrent(frame, afterHover, action))
                    throw new SafetyException("Hover changed the screen; reobserve before another approval");
                if (!tracker.IsPointVisible(source, point.X, point.Y)) throw new SafetyException("Hover changed the action target");
                CursorChanged?.Invoke(point.X, point.Y, action.Kind, supervisor.Epoch);
                if (action.Kind == "click")
                {
                    Batch(() => { lock (heldGate) { heldButton = true; InputNative.Send(InputNative.Mouse(2), InputNative.Mouse(4)); heldButton = false; } });
                }
                else Batch(() => InputNative.Send(InputNative.Mouse(0x800, data: unchecked((uint)(action.Ticks!.Value * 120)))));
                await Task.Delay(ReducedMotion ? 0 : 180, cancellation);
            }
            else if (action.Kind == "type")
            {
                foreach (char character in action.Text!)
                {
                    Batch(() => { lock (heldGate) { heldUnicode = character; InputNative.Send(InputNative.Unicode(character, false), InputNative.Unicode(character, true)); heldUnicode = null; } });
                    await Task.Delay(8, cancellation);
                }
            }
            else
            {
                ushort key = action.Chord switch { "Ctrl+A" => 0x41, "Ctrl+C" => 0x43, "Ctrl+S" => 0x53, "Escape" => 0x1B, "Tab" => 9,
                    "Enter" => 0x0D, "Backspace" => 8, "Home" => 0x24, "End" => 0x23, "Left" => 0x25, "Up" => 0x26, "Right" => 0x27, "Down" => 0x28,
                    _ => throw new SafetyException("Unsupported chord") };
                bool control = action.Chord!.StartsWith("Ctrl", StringComparison.Ordinal);
                Batch(() =>
                {
                    lock (heldGate)
                    {
                        heldKeys.Add(key); if (control) heldKeys.Add(0x11);
                        InputNative.Send(control ? [InputNative.Key(0x11), InputNative.Key(key), InputNative.Key(key, true), InputNative.Key(0x11, true)] : [InputNative.Key(key), InputNative.Key(key, true)]);
                        heldKeys.Clear();
                    }
                });
            }
            return new(proposal.Id, "dispatched", supervisor.Epoch);
        }
        catch (OperationCanceledException) { return new(proposal.Id, dispatched ? "unknown" : "not_dispatched", supervisor.Epoch); }
        catch (SafetyException) when (dispatched) { return new(proposal.Id, "unknown", supervisor.Epoch); }
        catch (SafetyException ex) { LastDeferralReason = ex.Message; return new(proposal.Id, "not_dispatched", supervisor.Epoch); }
        finally
        {
            CursorHidden?.Invoke(inputEpoch);
            ReleaseHeldControls(); activity.IsAssistantInputActive = false; serialized.Release();
        }
    }
    public void ReleaseHeldControls()
    {
        lock (heldGate)
        {
            foreach (var key in heldKeys) { try { InputNative.Send(InputNative.Key(key, true)); } catch (SafetyException) { } }
            heldKeys.Clear();
            if (heldUnicode.HasValue) { try { InputNative.Send(InputNative.Unicode(heldUnicode.Value, true)); } catch (SafetyException) { } heldUnicode = null; }
            if (heldButton) { try { InputNative.Send(InputNative.Mouse(4)); } catch (SafetyException) { } heldButton = false; }
        }
    }
}
