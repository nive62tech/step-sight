using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System.Text.Json;

namespace DesktopGuide.App;

// Opt-in rendering host for the disposable, cross-process native click regression.
// It has no capture, model, input executor or credentials.
internal static class OverlayTestHost
{
    public static void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var overlay = new OverlayWindow(); overlay.SetMode(true);
        _ = Task.Run(async () =>
        {
            try
            {
                while (await Console.In.ReadLineAsync() is { } line)
                {
                    using var message = JsonDocument.Parse(line);
                    var kind = message.RootElement.GetProperty("kind").GetString();
                    var x = message.RootElement.TryGetProperty("x", out var px) ? px.GetInt32() : 0;
                    var y = message.RootElement.TryGetProperty("y", out var py) ? py.GetInt32() : 0;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (kind == "hide") overlay.Hide();
                        else if (kind == "travel") overlay.ShowTravel((x,y));
                        else if (kind == "target") overlay.ShowTarget((x,y), "Creating a disposable meeting", "agent_click", false);
                    });
                    Console.WriteLine("ready"); await Console.Out.FlushAsync();
                    if (kind == "quit") break;
                }
            }
            finally { await Dispatcher.UIThread.InvokeAsync(() => { overlay.Close(); desktop.Shutdown(); }); }
        });
    }
}
