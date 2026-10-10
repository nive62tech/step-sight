using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DesktopGuide.Core;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace DesktopGuide.Windows;

/// <summary>One reusable WGC session per selected window; CPU images are copied only on demand.</summary>
internal sealed class WgcCapture : IDisposable
{
    private ID3D11Device? device;
    private IDirect3DDevice? winrtDevice;
    private readonly object lifecycle = new();
    private readonly object gpu = new();
    private CaptureLease? active;
    private bool disposed;
    public int SessionStarts { get; private set; }
    public bool Active { get { lock (lifecycle) return active is not null; } }
    public static bool Supported => GraphicsCaptureSession.IsSupported();

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface CaptureInterop
    {
        void CreateForWindow(nint window, ref Guid iid, out nint item);
        void CreateForMonitor(nint monitor, ref Guid iid, out nint item);
    }
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, int length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint result);

    private void Initialize()
    {
        if (device is not null) return;
        device = D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var pointer));
        try { winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }
    private static GraphicsCaptureItem CreateItem(nint window)
    {
        var className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out var name));
        nint factory = 0;
        try
        {
            var iid = typeof(CaptureInterop).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref iid, out factory));
            var interop = (CaptureInterop)Marshal.GetObjectForIUnknown(factory);
            var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            interop.CreateForWindow(window, ref itemId, out var pointer);
            try { return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(pointer); }
            finally { Marshal.Release(pointer); Marshal.ReleaseComObject(interop); }
        }
        finally { if (factory != 0) Marshal.Release(factory); WindowsDeleteString(name); }
    }
    public Task<Bitmap> CaptureAsync(nint window, CancellationToken cancellation) => CaptureAsync(window, cancellation, 0);
    private async Task<Bitmap> CaptureAsync(nint window, CancellationToken cancellation, int resizeRetries)
    {
        CaptureLease lease;
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellation.ThrowIfCancellationRequested();
            if (!Supported) throw new SafetyException("Windows Graphics Capture is unavailable; monitor fallback needs separate consent");
            if (active is not null && (active.Window != window || !active.IsAlive))
            {
                active.Dispose(); active = null;
            }
            if (active is null) { Initialize(); active = new CaptureLease(this, window); SessionStarts++; }
            lease = active;
        }
        try { return await lease.SnapshotAsync(cancellation); }
        catch (CaptureResizedException)
        {
            // WGC can stop delivering frames after Recreate on a static resized window.
            // Restart only for an actual size change, never for routine snapshots.
            Stop(lease); cancellation.ThrowIfCancellationRequested();
            if (resizeRetries >= 2) throw new SafetyException("Finish resizing the window, then check the screen again.");
            return await CaptureAsync(window, cancellation, resizeRetries + 1);
        }
        catch { Stop(lease); throw; }
    }
    public void Stop() => Stop(null);
    private void Stop(CaptureLease? expected)
    {
        CaptureLease? previous;
        lock (lifecycle)
        {
            if (expected is not null && active != expected) return;
            previous = active; active = null;
        }
        previous?.Dispose();
    }

    private sealed class CaptureResizedException : Exception { }
    private sealed class CaptureLease : IDisposable
    {
        private readonly WgcCapture owner;
        private readonly object gate = new();
        private readonly GraphicsCaptureItem item;
        private readonly Direct3D11CaptureFramePool pool;
        private readonly GraphicsCaptureSession session;
        private ID3D11Texture2D? latest;
        private global::Windows.Graphics.SizeInt32 size;
        private TaskCompletionSource arrived = NewSignal();
        private bool closed;
        private Exception? error;
        public nint Window { get; }
        public bool IsAlive { get { lock (gate) return !closed && error is null; } }
        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CaptureLease(WgcCapture owner, nint window)
        {
            this.owner = owner; Window = window; item = CreateItem(window); size = item.Size;
            ValidateSize(size);
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(owner.winrtDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            if (global::Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "MinUpdateInterval"))
                session.MinUpdateInterval = TimeSpan.FromMilliseconds(100);
            pool.FrameArrived += OnFrame;
            item.Closed += OnClosed;
            try { session.StartCapture(); }
            catch { Dispose(); throw; }
        }
        private static void ValidateSize(global::Windows.Graphics.SizeInt32 value)
        {
            if (value.Width <= 0 || value.Height <= 0 || (long)value.Width * value.Height > 16_000_000)
                throw new SafetyException("Window capture dimensions invalid");
        }
        private void OnClosed(GraphicsCaptureItem sender, object args)
        {
            lock (gate) { error = new SafetyException("The observed window was closed."); arrived.TrySetResult(); }
        }
        private void OnFrame(Direct3D11CaptureFramePool sender, object args)
        {
            lock (gate)
            {
                if (closed || error is not null) return;
                try
                {
                    using (var frame = sender.TryGetNextFrame())
                    {
                        if (frame is null) return;
                        ValidateSize(frame.ContentSize);
                        if (frame.ContentSize.Width != size.Width || frame.ContentSize.Height != size.Height)
                        { error = new CaptureResizedException(); arrived.TrySetResult(); return; }
                        else { lock (owner.gpu) owner.BufferSurface(frame.Surface, ref latest); }
                    }
                    var previous = arrived; arrived = NewSignal(); previous.TrySetResult();
                }
                catch (Exception) { error = new SafetyException("Window frame unavailable. Stop and start the guide again."); arrived.TrySetResult(); }
            }
        }
        public async Task<Bitmap> SnapshotAsync(CancellationToken cancellation)
        {
            Task next; bool hasFrame;
            lock (gate) { ThrowIfUnavailable(); next = arrived.Task; hasFrame = latest is not null; }
            // Allow a compositor update after the request. An unchanged window can legitimately
            // produce no new frame; the last GPU copy remains current while this session is alive.
            try { await next.WaitAsync(hasFrame ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromSeconds(3), cancellation); }
            catch (TimeoutException) when (hasFrame) { }
            catch (TimeoutException) { throw new SafetyException("Windows hasn't supplied an image yet. Check that the browser is visible, then retry."); }
            cancellation.ThrowIfCancellationRequested();
            lock (gate)
            {
                ThrowIfUnavailable();
                if (latest is null) throw new SafetyException("Waiting for the browser image. Try again after the window finishes opening.");
                lock (owner.gpu) return owner.CopyTexture(latest, size.Width, size.Height);
            }
        }
        private void ThrowIfUnavailable()
        {
            if (closed) throw new OperationCanceledException("Window capture stopped");
            if (error is not null) throw error;
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (closed) return; closed = true;
                arrived.TrySetResult(); latest?.Dispose(); latest = null;
                pool.FrameArrived -= OnFrame; item.Closed -= OnClosed;
            }
            session.Dispose(); pool.Dispose();
        }
    }

    private void BufferSurface(IDirect3DSurface surface, ref ID3D11Texture2D? buffered)
    {
        var abi = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            var interfaceId = typeof(IDirect3DDxgiInterfaceAccess).GUID;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(abi, in interfaceId, out var accessPointer));
            using var access = new IDirect3DDxgiInterfaceAccess(accessPointer);
            using var texture = access.GetInterface<ID3D11Texture2D>();
            var description = texture.Description;
            if (buffered is null)
            {
                description.Usage = ResourceUsage.Default; description.BindFlags = BindFlags.None;
                description.CPUAccessFlags = CpuAccessFlags.None; description.MiscFlags = ResourceOptionFlags.None;
                buffered = device!.CreateTexture2D(description);
            }
            device!.ImmediateContext.CopyResource(buffered, texture);
        }
        finally { Marshal.Release(abi); }
    }
    private Bitmap CopyTexture(ID3D11Texture2D texture, int width, int height)
    {
            var description = texture.Description;
            description.Usage = ResourceUsage.Staging; description.BindFlags = BindFlags.None;
            description.CPUAccessFlags = CpuAccessFlags.Read; description.MiscFlags = ResourceOptionFlags.None;
            using var staging = device!.CreateTexture2D(description);
            // Vortice caches ImmediateContext on the device; disposing it here breaks the next frame.
            var context = device.ImmediateContext;
            context.CopyResource(staging, texture);
            var mapped = context.Map(staging, 0, MapMode.Read);
            try
            {
                var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var bits = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
                try
                {
                    var row = new byte[width * 4];
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(mapped.DataPointer + y * (int)mapped.RowPitch, row, 0, row.Length);
                        Marshal.Copy(row, 0, bits.Scan0 + y * bits.Stride, row.Length);
                    }
                }
                finally { bitmap.UnlockBits(bits); }
                return bitmap;
            }
            finally { context.Unmap(staging, 0); }
    }
    public void Dispose()
    {
        lock (lifecycle) { if (disposed) return; disposed = true; }
        Stop(); (winrtDevice as IDisposable)?.Dispose(); device?.Dispose();
    }
}
