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

/// <summary>One-shot bounded WGC capture. Runtime behavior requires explicit Windows qualification.</summary>
internal sealed class WgcCapture : IDisposable
{
    private ID3D11Device? device;
    private IDirect3DDevice? winrtDevice;
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
    public async Task<Bitmap> CaptureAsync(nint window, CancellationToken cancellation)
    {
        if (!Supported) throw new SafetyException("Windows Graphics Capture is unavailable; monitor fallback needs separate consent");
        Initialize();
        var item = CreateItem(window);
        if (item.Size.Width <= 0 || item.Size.Height <= 0 || (long)item.Size.Width * item.Size.Height > 16_000_000)
            throw new SafetyException("Window capture dimensions invalid");
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(winrtDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        var completed = new TaskCompletionSource<Bitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.FrameArrived += (sender, _) =>
        {
            if (completed.Task.IsCompleted) return;
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame is null) return;
                if (frame.ContentSize.Width != item.Size.Width || frame.ContentSize.Height != item.Size.Height)
                    throw new SafetyException("Window resized during capture");
                var bitmap = CopySurface(frame.Surface, frame.ContentSize.Width, frame.ContentSize.Height);
                if (!completed.TrySetResult(bitmap)) bitmap.Dispose();
            }
            catch (Exception) { completed.TrySetException(new SafetyException("Window frame unavailable; reselect and preview")); }
        };
        session.StartCapture();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var registration = timeout.Token.Register(() => completed.TrySetCanceled(timeout.Token));
        return await completed.Task;
    }
    private Bitmap CopySurface(IDirect3DSurface surface, int width, int height)
    {
        var abi = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            var interfaceId = typeof(IDirect3DDxgiInterfaceAccess).GUID;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(abi, in interfaceId, out var accessPointer));
            using var access = new IDirect3DDxgiInterfaceAccess(accessPointer);
            using var texture = access.GetInterface<ID3D11Texture2D>();
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
        finally { Marshal.Release(abi); }
    }
    public void Dispose() { (winrtDevice as IDisposable)?.Dispose(); device?.Dispose(); }
}
