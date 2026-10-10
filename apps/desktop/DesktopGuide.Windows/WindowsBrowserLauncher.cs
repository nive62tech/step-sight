using System.Diagnostics;
using DesktopGuide.Core;
using DesktopGuide.Platform;
using Microsoft.Win32;

namespace DesktopGuide.Windows;

public sealed class WindowsBrowserLauncher(WindowsCapture capture) : IBrowserLauncher
{
    public async Task<CaptureSource> OpenAsync(BrowserLaunch plan, CancellationToken cancellation)
    {
        var executable = FindExecutable(plan.Browser);
        var before = Windows(executable).Keys.ToHashSet();
        cancellation.ThrowIfCancellationRequested();
        var launch = new ProcessStartInfo(executable) { UseShellExecute = false };
        launch.ArgumentList.Add(plan.Browser == "firefox" ? "-new-window" : "--new-window");
        launch.ArgumentList.Add(plan.Address.AbsoluteUri);
        using var process = Process.Start(launch) ?? throw new SafetyException("The browser couldn't be opened.");
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(20))
        {
            await Task.Delay(250, cancellation);
            var candidates = Windows(executable).Where(pair => !before.Contains(pair.Key)).ToArray();
            if (candidates.Length > 1) throw new SafetyException("More than one browser window opened. Use Choose an existing window to select the intended one.");
            if (candidates.Length != 1) continue;
            var found = candidates[0];
            // Require the same new window to remain visible after startup before binding capture.
            await Task.Delay(750, cancellation);
            if (!Windows(executable).TryGetValue(found.Key, out var pid) || pid != found.Value) continue;
            cancellation.ThrowIfCancellationRequested();
            Native.SetForegroundWindow(found.Key);
            return capture.RegisterLaunchedWindow(found.Key, pid, $"{plan.Browser} · {plan.Address.Host}");
        }
        throw new SafetyException("The browser opened without a new visible window. Retry, or choose its window manually.");
    }
    private static Dictionary<nint, uint> Windows(string executable)
    {
        var result = new Dictionary<nint, uint>();
        Native.EnumWindows((window, _) =>
        {
            if (!Native.IsWindowVisible(window) || Native.IsIconic(window)) return true;
            if (!Native.GetWindowRect(window, out var rect) || rect.Right - rect.Left < 300 || rect.Bottom - rect.Top < 200) return true;
            Native.GetWindowThreadProcessId(window, out var pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) result[window] = pid;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException) { }
            return true;
        }, 0);
        return result;
    }
    private static string FindExecutable(string browser)
    {
        var name = browser switch { "chrome" => "chrome.exe", "edge" => "msedge.exe", "firefox" => "firefox.exe", _ => throw new SafetyException("Unsupported browser") };
        var folder = browser switch { "chrome" => @"Google\Chrome\Application", "edge" => @"Microsoft\Edge\Application", _ => "Mozilla Firefox" };
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + name);
            if (key?.GetValue(null) is string path && File.Exists(path.Trim('"'))) return Path.GetFullPath(path.Trim('"'));
        }
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
        {
            var path = Path.Combine(Environment.GetFolderPath(root), folder, name);
            if (File.Exists(path)) return path;
        }
        throw new SafetyException($"{browser} isn't installed. Name an installed browser in your prompt.");
    }
}
