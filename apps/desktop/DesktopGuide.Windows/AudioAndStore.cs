using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopGuide.Platform;
using NAudio.Wave;

namespace DesktopGuide.Windows;

public sealed class WindowsAudioCapture : IAudioCapture
{
    private sealed class RecordingSession
    {
        public readonly WaveInEvent Microphone = new() { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
        public readonly MemoryStream Buffer = new();
        public readonly TaskCompletionSource<byte[]> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WaveFileWriter? Writer;
        public System.Threading.Timer? Limit;
        public bool Cancelled;
    }
    private RecordingSession? current;
    private Task<byte[]>? lastRecording;
    private readonly object gate = new();
    public bool Recording { get { lock (gate) return current is not null; } }
    public void Start()
    {
        lock (gate)
        {
            if (current is not null) throw new InvalidOperationException("Recording already active");
            var session = new RecordingSession(); current = session; lastRecording = session.Completion.Task;
            session.Writer = new WaveFileWriter(session.Buffer, session.Microphone.WaveFormat);
            session.Microphone.DataAvailable += (_, args) =>
            {
                lock (gate)
                {
                    if (session.Cancelled || session.Writer is null) return;
                    int remaining = (int)Math.Max(0, 960000 - session.Writer.Length);
                    if (remaining > 0) session.Writer.Write(args.Buffer, 0, Math.Min(remaining, args.BytesRecorded));
                }
            };
            session.Microphone.RecordingStopped += (_, args) =>
            {
                lock (gate)
                {
                    byte[] audio = [];
                    if (session.Writer is not null)
                    { session.Writer.Dispose(); if (!session.Cancelled) audio = session.Buffer.ToArray(); }
                    session.Writer = null; session.Microphone.Dispose(); session.Limit?.Dispose(); session.Buffer.Dispose();
                    if (current == session) current = null; // An old callback cannot end a newer recording.
                    if (session.Cancelled) session.Completion.TrySetCanceled();
                    else if (args.Exception is null) session.Completion.TrySetResult(audio);
                    else session.Completion.TrySetException(new InvalidOperationException("Microphone unavailable"));
                }
            };
            try { session.Microphone.StartRecording(); session.Limit = new(_ => { lock (gate) { if (current == session) session.Microphone.StopRecording(); } }, null, 30000, Timeout.Infinite); }
            catch { session.Cancelled = true; current = null; lastRecording = null; session.Microphone.Dispose(); session.Writer?.Dispose(); session.Writer = null; session.Buffer.Dispose(); throw; }
        }
    }
    public Task<byte[]> StopAsync()
    {
        lock (gate) { current?.Microphone.StopRecording(); var result = lastRecording ?? Task.FromResult(Array.Empty<byte>()); lastRecording = null; return result; }
    }
    public void Cancel()
    {
        lock (gate)
        {
            var session = current; current = null; lastRecording = null;
            if (session is null) return;
            session.Cancelled = true; session.Limit?.Dispose(); session.Completion.TrySetCanceled(); session.Microphone.StopRecording();
        }
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
