using System.Runtime.InteropServices;
using DesktopGuide.Platform;

namespace DesktopGuide.Windows;

public sealed class WindowsActivity : IUserActivityObserver, ICompanionHotkey
{
    private readonly Thread thread;
    private readonly Native.HookProcedure mouseHook, keyboardHook;
    private readonly Native.WindowProcedure windowProcedure;
    private readonly HashSet<nint> ignored = [];
    private readonly ManualResetEventSlim ready = new();
    private nint hwnd, mouseHandle, keyboardHandle;
    private volatile bool active;
    private volatile bool autonomous;
    private volatile bool observationEnabled;
    private long heartbeat;
    private long dispatcherHeartbeat;
    private readonly System.Threading.Timer heartbeatTimer;
    private readonly AutoResetEvent signal = new(false);
    private readonly Thread eventsThread;
    private volatile bool ending;
    private int pendingStop, pendingTakeover, pendingUnavailable, pendingActivity, pendingInvocation;
    public event Action? Invoked;
    public bool CompanionHotkeyRegistered { get; private set; }
    public event Action? Activity;
    public event Action? Takeover;
    public event Action? EmergencyStop;
    public event Action? SessionUnavailable;
    public bool HotkeyRegistered { get; private set; }
    public bool IsAssistantInputActive { get => active; set => active = value; }
    public bool AutonomousControlActive { get => autonomous; set => autonomous = value; }
    public bool ObservationEnabled { get => observationEnabled; set => observationEnabled = value; }
    public bool TrackingAvailable => mouseHandle != 0 && keyboardHandle != 0
        && Environment.TickCount64 - Interlocked.Read(ref heartbeat) < 2000
        && Environment.TickCount64 - Interlocked.Read(ref dispatcherHeartbeat) < 2000;
    public void IgnoreWindow(nint handle) { lock (ignored) ignored.Add(handle); }

    public WindowsActivity()
    {
        mouseHook = Mouse; keyboardHook = Keyboard; windowProcedure = Procedure;
        eventsThread = new Thread(EventsLoop) { IsBackground = true, Name = "Desktop Guide safety dispatcher", Priority = ThreadPriority.AboveNormal };
        eventsThread.Start();
        thread = new Thread(Run) { IsBackground = true, Name = "Desktop Guide local Stop and activity" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        ready.Wait(TimeSpan.FromSeconds(3));
        heartbeatTimer = new(_ => { if (hwnd != 0) Native.PostMessage(hwnd, 0x8001, 0, 0); }, null, 250, 250);
    }
    private void Run()
    {
        var name = "DesktopGuideStop" + Guid.NewGuid().ToString("N");
        var windowClass = new Native.WindowClass { ClassName = name, Procedure = windowProcedure };
        Native.RegisterClass(ref windowClass);
        // Hidden top-level window receives session and power broadcasts.
        hwnd = Native.CreateWindowEx(0, name, "", 0, 0, 0, 0, 0, 0, 0, 0, 0);
        HotkeyRegistered = Native.RegisterHotKey(hwnd, 1, 0x4000 | 1 | 2 | 4, 0x53);
        CompanionHotkeyRegistered = Native.RegisterHotKey(hwnd, 2, 0x4000 | 1 | 2, 0x20);
        Native.WTSRegisterSessionNotification(hwnd, 0);
        mouseHandle = Native.SetWindowsHookEx(14, mouseHook, 0, 0);
        keyboardHandle = Native.SetWindowsHookEx(13, keyboardHook, 0, 0);
        Interlocked.Exchange(ref heartbeat, Environment.TickCount64);
        ready.Set();
        if (mouseHandle == 0 || keyboardHandle == 0) Queue(ref pendingUnavailable);
        while (Native.GetMessage(out var message, 0, 0, 0) > 0)
        { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
        if (mouseHandle != 0) Native.UnhookWindowsHookEx(mouseHandle);
        if (keyboardHandle != 0) Native.UnhookWindowsHookEx(keyboardHandle);
        Native.UnregisterHotKey(hwnd, 1); Native.UnregisterHotKey(hwnd, 2); Native.WTSUnRegisterSessionNotification(hwnd); Native.DestroyWindow(hwnd);
    }
    private nint Procedure(nint handle, uint message, nuint wparam, nint lparam)
    {
        if (message == 0x312 && wparam == 1) Queue(ref pendingStop);
        if (message == 0x312 && wparam == 2) Queue(ref pendingInvocation);
        if (message == 0x8001) Interlocked.Exchange(ref heartbeat, Environment.TickCount64);
        if (message == 0x2B1 && wparam is 2 or 4 or 6 or 7) Queue(ref pendingUnavailable);
        if (message == 0x218 && wparam is 4 or 0x12) Queue(ref pendingUnavailable);
        if (message == 0x10) { Native.PostMessage(0, 0x12, 0, 0); return 0; }
        return Native.DefWindowProc(handle, message, wparam, lparam);
    }
    private bool Ignored(nint window) { lock (ignored) return ignored.Contains(Native.GetAncestor(window, 2)); }
    private nint Mouse(int code, nuint message, nint data)
    {
        if (code >= 0 && observationEnabled)
        {
            var info = Marshal.PtrToStructure<Native.MouseHook>(data);
            if (info.ExtraInfo != Native.InputTag)
            {
                if (active || autonomous) Queue(ref pendingTakeover);
                if (message != 0x200 && !Ignored(Native.WindowFromPoint(info.Point))) Queue(ref pendingActivity);
            }
        }
        return Native.CallNextHookEx(mouseHandle, code, message, data);
    }
    private nint Keyboard(int code, nuint message, nint data)
    {
        if (code >= 0 && observationEnabled && message is 0x100 or 0x104)
        {
            var info = Marshal.PtrToStructure<Native.KeyboardHook>(data);
            if (info.ExtraInfo != Native.InputTag)
            {
                if (active || autonomous) Queue(ref pendingTakeover);
                if (!Ignored(Native.GetForegroundWindow())) Queue(ref pendingActivity);
            }
        }
        return Native.CallNextHookEx(keyboardHandle, code, message, data);
    }
    public void Dispose()
    {
        if (hwnd != 0) Native.PostMessage(hwnd, 0x10, 0, 0);
        heartbeatTimer.Dispose();
        thread.Join(TimeSpan.FromSeconds(2)); ready.Dispose();
        ending = true; signal.Set(); eventsThread.Join(TimeSpan.FromSeconds(2)); signal.Dispose();
    }
    private void Queue(ref int flag) { Interlocked.Exchange(ref flag, 1); signal.Set(); }
    private void EventsLoop()
    {
        while (!ending)
        {
            Interlocked.Exchange(ref dispatcherHeartbeat, Environment.TickCount64);
            signal.WaitOne(250);
            try
            {
                if (Interlocked.Exchange(ref pendingStop, 0) != 0) EmergencyStop?.Invoke();
                if (Interlocked.Exchange(ref pendingUnavailable, 0) != 0) SessionUnavailable?.Invoke();
                if (Interlocked.Exchange(ref pendingTakeover, 0) != 0) Takeover?.Invoke();
                if (Interlocked.Exchange(ref pendingActivity, 0) != 0) Activity?.Invoke();
                if (Interlocked.Exchange(ref pendingInvocation, 0) != 0) Invoked?.Invoke();
            }
            catch (Exception) { Interlocked.Exchange(ref pendingUnavailable, 1); }
        }
    }
}
