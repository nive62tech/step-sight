using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopGuide.Core;
using DesktopGuide.Platform;

namespace DesktopGuide.App;

public sealed partial class MainViewModel
{
    private RemoteControlBridge? phoneBridge;
    private bool remoteOwnsTask;
    private CancellationTokenRegistration remoteTaskCancellation;
    [ObservableProperty] private bool phoneControlEnabled;
    [ObservableProperty] private bool allowAutonomousPhoneTasks;
    [ObservableProperty] private string phoneControlStatus = "Phone control is off.";
    public string PhoneControlUrl { get; private set; } = "";
    public bool CanEnablePhoneControl => SignedIn && !HasLesson && !IsBusy && !PhoneControlEnabled && !VoiceListening && !VoiceTranscribing;
    public void AttachRemote(IRemoteAgentClient client, string origin)
    {
        PhoneControlUrl = origin.TrimEnd('/') + "/control/"; OnPropertyChanged(nameof(PhoneControlUrl));
        phoneBridge = new(client, controller.DeviceId,
            c => RemoteUi(() => new RemoteStatus(LessonState.ToString(), IsAutonomous ? "autonomous" : "guide", IsBusy,
                HasLesson ? Clip(Instruction, 1200) : "", Clip(Notice, 600), remoteOwnsTask && IsPaused && !IsBusy,
                remoteOwnsTask && !IsAutonomous && CanInteract), c),
            HandlePhoneCommand, (enabled, message) => ui(() =>
            {
                PhoneControlEnabled = enabled; PhoneControlStatus = message;
                if (!enabled && remoteOwnsTask) { taskCancellation?.Cancel(); controller.PauseLocal(); }
            }));
    }
    private static string Clip(string text, int max) => text.Length > max ? text[..max] : text;
    private Task<T> RemoteUi<T>(Func<T> action, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        ui(() =>
        {
            if (cancellation.IsCancellationRequested) { completion.TrySetCanceled(cancellation); return; }
            try { completion.TrySetResult(action()); } catch (Exception ex) { completion.TrySetException(ex); }
        });
        return completion.Task.WaitAsync(cancellation);
    }
    private Task<RemoteAcknowledgement> HandlePhoneCommand(RemoteCommand command, CancellationToken cancellation)
        => RemoteUi(() =>
        {
            if (phoneBridge?.Enabled != true || !SignedIn) return new RemoteAcknowledgement(command.Id, false, "Phone control is off or sign-in expired.");
            if (command.ExpiresAt <= DateTimeOffset.UtcNow) return new(command.Id, false, "Command expired.");
            switch (command.Kind)
            {
                case "start":
                    if (IsBusy || (HasLesson && LessonState != LessonState.Completed) || VoiceListening || VoiceTranscribing) return new(command.Id, false, "Stop the current task first.");
                    if (string.IsNullOrWhiteSpace(command.Goal) || command.Goal.Length > 1000 || command.Mode is not ("guide" or "autonomous")) return new(command.Id, false, "Invalid task request.");
                    if (command.Mode == "autonomous" && !phoneBridge.AllowsAutonomous) return new(command.Id, false, "Autonomous phone tasks are not allowed.");
                    if (LessonState == LessonState.Completed) StopTask();
                    SelectedTaskMode = command.Mode == "autonomous" ? 1 : 0; Goal = command.Goal;
                    AutonomousConsent = command.Mode == "autonomous" && phoneBridge.AllowsAutonomous;
                    remoteOwnsTask = true; remoteTaskCancellation.Dispose();
                    remoteTaskCancellation = cancellation.Register(() => { taskCancellation?.Cancel(); controller.PauseLocal(); });
                    _ = ContinueSetup(); return new(command.Id, true, "Task received by your PC.");
                case "stop":
                    StopTask(); remoteOwnsTask = false; remoteTaskCancellation.Dispose();
                    return new(command.Id, true, "Task stopped on your PC.");
                case "pause":
                    if (!remoteOwnsTask) return new(command.Id, false, "No phone-started task is active.");
                    taskCancellation?.Cancel(); controller.PauseLocal(); return new(command.Id, true, "Task paused on your PC.");
                case "resume":
                    if (!remoteOwnsTask || !IsPaused || IsBusy) return new(command.Id, false, "The task is not ready to resume.");
                    if (IsAutonomous && !phoneBridge.AllowsAutonomous) return new(command.Id, false, "Autonomous phone tasks are not allowed.");
                    ResumeConsent = true; _ = Resume(); return new(command.Id, true, "Resuming from a fresh screenshot.");
                case "check":
                    if (!remoteOwnsTask || IsAutonomous || !CanInteract) return new(command.Id, false, "Screen check is unavailable.");
                    _ = Reobserve(); return new(command.Id, true, "Checking a fresh screenshot.");
                default: return new(command.Id, false, "Unsupported command.");
            }
        }, cancellation);
    [RelayCommand] private Task EnablePhoneControl() => Run(async () =>
    {
        if (!SignedIn || IsMock || phoneBridge is null) throw new SafetyException("Sign in to this cloud build before enabling phone control.");
        if (HasLesson || VoiceListening || VoiceTranscribing) throw new SafetyException("Stop the current task or recording first.");
        await phoneBridge.EnableAsync(Environment.MachineName, AllowAutonomousPhoneTasks);
    });
    [RelayCommand] private void DisablePhoneControl()
    {
        phoneBridge?.Disable();
        if (remoteOwnsTask) StopTask();
        remoteOwnsTask = false; remoteTaskCancellation.Dispose();
    }
    partial void OnPhoneControlEnabledChanged(bool value) => RefreshUi();
}
