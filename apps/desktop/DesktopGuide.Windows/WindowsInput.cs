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
    public event Action<(int X, int Y)>? TargetShown;
    public event Action? TargetHidden;
    public bool ReducedMotion { get; set; }
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
            int segments = ReducedMotion ? 1 : 12;
            for (int i = 1; i <= segments; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!activity.TrackingAvailable) throw new SafetyException("Input observer unavailable");
                if (!tracker.IsCurrentGeometry(source, frame.Geometry)) throw new SafetyException("Target geometry changed during pointing");
                int x = (int)Math.Round(start.X + (destination.X - start.X) * i / (double)segments);
                int y = (int)Math.Round(start.Y + (destination.Y - start.Y) * i / (double)segments);
                supervisor.GuideBatch(frame, tracker.IsForegroundAndVisible(source), () => InputNative.Move(x, y));
                if (i < segments) await Task.Delay(12, cancellation);
            }
            TargetShown?.Invoke(destination);
        }
        finally { activity.IsAssistantInputActive = false; }
    }
    public void Hide() => TargetHidden?.Invoke();
}

public sealed class WindowsInputExecutor(PermissionSupervisor supervisor, IWindowTracker tracker,
    IUserActivityObserver activity, IScreenCapture capture) : IInputExecutor
{
    private readonly SemaphoreSlim serialized = new(1);
    private readonly object heldGate = new();
    private readonly HashSet<ushort> heldKeys = [];
    private char? heldUnicode;
    private bool heldButton;
    public async Task<ActionReceipt> ExecuteAsync(LocalCapability capability, ActionProposal proposal, ScreenFrame frame,
        CaptureSource source, CancellationToken cancellation)
    {
        await serialized.WaitAsync(cancellation);
        bool dispatched = false;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!tracker.IsCurrentGeometry(source, frame.Geometry)) throw new SafetyException("Target changed before demonstration");
            var lease = supervisor.Consume(capability, proposal, frame, tracker.IsForegroundAndVisible(source));
            activity.IsAssistantInputActive = true;
            void Batch(Action send)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!activity.TrackingAvailable) throw new SafetyException("Input observer unavailable");
                if (!tracker.IsCurrentGeometry(source, frame.Geometry)) throw new SafetyException("Target changed during input");
                supervisor.ExecuteBatch(lease, frame, tracker.IsForegroundAndVisible(source), () =>
                {
                    dispatched = true;
                    send();
                });
            }
            var action = proposal.Action;
            if (action.Kind is "click" or "scroll")
            {
                frame.RequireUnmaskedPoint(action.X!.Value, action.Y!.Value);
                var point = frame.Geometry.Map(action.X!.Value, action.Y!.Value);
                if (!tracker.IsPointVisible(source, point.X, point.Y)) throw new SafetyException("Action target is covered");
                Batch(() => InputNative.Move(point.X, point.Y));
                // Hover can replace a control inside the same root window. Hit testing alone cannot detect that.
                await Task.Delay(80, cancellation);
                var afterHover = await capture.CaptureAsync(source, frame.PrivacyMasks, cancellation);
                if (afterHover.Geometry != frame.Geometry || afterHover.PixelDigest != frame.PixelDigest)
                    throw new SafetyException("Hover changed the screen; reobserve before another approval");
                if (!tracker.IsPointVisible(source, point.X, point.Y)) throw new SafetyException("Hover changed the action target");
                if (action.Kind == "click")
                {
                    Batch(() => { lock (heldGate) { heldButton = true; InputNative.Send(InputNative.Mouse(2), InputNative.Mouse(4)); heldButton = false; } });
                }
                else Batch(() => InputNative.Send(InputNative.Mouse(0x800, data: unchecked((uint)(action.Ticks!.Value * 120)))));
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
                ushort key = action.Chord switch { "Ctrl+A" => 0x41, "Ctrl+C" => 0x43, "Ctrl+S" => 0x53, "Escape" => 0x1B, "Tab" => 9, _ => throw new SafetyException("Unsupported chord") };
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
        finally
        {
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
