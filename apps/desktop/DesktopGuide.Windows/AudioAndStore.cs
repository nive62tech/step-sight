using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopGuide.Platform;
using NAudio.Wave;

namespace DesktopGuide.Windows;

public sealed class WindowsAudioCapture : IAudioCapture
{
    private WaveInEvent? microphone;
    private MemoryStream? buffer;
    private WaveFileWriter? writer;
    private TaskCompletionSource<byte[]>? completion;
    private System.Threading.Timer? limit;
    private readonly object gate = new();
    public bool Recording { get { lock (gate) return microphone is not null; } }
    public void Start()
    {
        lock (gate)
        {
            if (microphone is not null) return;
            buffer = new MemoryStream(); completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            microphone = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
            writer = new WaveFileWriter(buffer, microphone.WaveFormat);
            microphone.DataAvailable += (_, args) =>
            {
                lock (gate)
                {
                    if (writer is null || buffer is null) return;
                    int remaining = (int)Math.Max(0, 960000 - writer.Length);
                    if (remaining > 0) writer.Write(args.Buffer, 0, Math.Min(remaining, args.BytesRecorded));
                }
            };
            microphone.RecordingStopped += (_, args) =>
            {
                lock (gate)
                {
                    byte[] audio = [];
                    if (writer is not null && buffer is not null)
                    { writer.Dispose(); audio = buffer.ToArray(); }
                    writer = null; buffer = null;
                    microphone?.Dispose(); microphone = null; limit?.Dispose(); limit = null;
                    if (args.Exception is null) completion?.TrySetResult(audio);
                    else completion?.TrySetException(new InvalidOperationException("Microphone unavailable"));
                }
            };
            try { microphone.StartRecording(); limit = new(_ => { lock (gate) microphone?.StopRecording(); }, null, 30000, Timeout.Infinite); }
            catch { microphone.Dispose(); microphone = null; writer.Dispose(); writer = null; buffer = null; throw; }
        }
    }
    public Task<byte[]> StopAsync()
    {
        lock (gate) { microphone?.StopRecording(); return completion?.Task ?? Task.FromResult(Array.Empty<byte>()); }
    }
    public void Cancel()
    {
        lock (gate) { microphone?.StopRecording(); completion?.TrySetCanceled(); }
    }
    public void Dispose() => Cancel();
}

public sealed class WindowsSpeechPlayback : ISpeechPlayback
{
    private readonly object gate = new();
    private WaveOutEvent? output;
    private WaveFileReader? reader;
    public void Play(byte[] wav)
    {
        lock (gate)
        {
            Stop();
            reader = new(new MemoryStream(wav, writable: false)); output = new();
            output.Init(reader); output.Play();
        }
    }
    public void Stop() { lock (gate) { output?.Stop(); output?.Dispose(); output = null; reader?.Dispose(); reader = null; } }
    public void Dispose() => Stop();
}

public sealed class WindowsLocalStore : ILocalSessionStore
{
    private readonly string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopGuide");
    public Guid DeviceId { get; }
    public WindowsLocalStore()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "device-id.txt");
        if (File.Exists(path) && Guid.TryParse(File.ReadAllText(path), out var id)) DeviceId = id;
        else { DeviceId = Guid.NewGuid(); File.WriteAllText(path, DeviceId.ToString()); }
    }
    public void SaveSummary(string lessonId, int completedSteps)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Summary(lessonId, completedSteps)));
        File.WriteAllBytes(Path.Combine(directory, "progress.bin"), ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
    }
    public (string LessonId, int CompletedSteps)? ReadSummary()
    {
        try
        {
            var path = Path.Combine(directory, "progress.bin");
            if (!File.Exists(path)) return null;
            var value = JsonSerializer.Deserialize<Summary>(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser));
            return value is null ? null : (value.LessonId, value.CompletedSteps);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException) { return null; }
    }
    public void Clear() { var path = Path.Combine(directory, "progress.bin"); if (File.Exists(path)) File.Delete(path); }
    private sealed record Summary(string LessonId, int CompletedSteps);
}
