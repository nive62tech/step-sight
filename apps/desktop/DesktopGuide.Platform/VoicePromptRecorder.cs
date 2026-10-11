using DesktopGuide.Core;

namespace DesktopGuide.Platform;

public interface IVoicePromptRecorder
{
    void Start();
    Task<string> FinishAsync(CancellationToken cancellation);
    void Cancel();
}

public sealed class VoicePromptRecorder(IAudioCapture microphone, IGuidanceApiClient api) : IVoicePromptRecorder
{
    public void Start() => microphone.Start();
    public void Cancel() => microphone.Cancel();
    public async Task<string> FinishAsync(CancellationToken cancellation)
    {
        var wav = await microphone.StopAsync().WaitAsync(cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (wav.Length <= 44) throw new SafetyException("No speech was recorded. Try again or type your task.");
        var text = (await api.TranscribeAsync(wav, cancellation)).Trim();
        cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1000)
            throw new SafetyException("Please use a short spoken task, or type it below.");
        return text; // Draft only. This service never creates a session or authorizes input.
    }
}
