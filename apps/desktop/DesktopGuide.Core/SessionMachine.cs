namespace DesktopGuide.Core;

public enum LessonState
{
    Idle, Consent, Observing, Planning, Presenting, Pointing, WaitingForLearner, Verifying,
    AnsweringQuestion, WaitingForApplication, Recovering, AwaitingDemonstrationApproval,
    Demonstrating, PrivacyPaused, Paused, NetworkInterrupted, Stopped, Completed
}
public enum InteractionMode { Guide, Demonstrate, Practice }

public sealed class SessionMachine
{
    private readonly object gate = new();
    private volatile LessonState state = LessonState.Idle;
    public LessonState State => state;
    public event Action<LessonState>? Changed;
    private static readonly Dictionary<LessonState, LessonState[]> Allowed = new()
    {
        [LessonState.Idle] = [LessonState.Consent],
        [LessonState.Consent] = [LessonState.Observing],
        [LessonState.Observing] = [LessonState.Planning, LessonState.Verifying],
        [LessonState.Planning] = [LessonState.Presenting, LessonState.Recovering],
        [LessonState.Presenting] = [LessonState.Pointing, LessonState.WaitingForLearner, LessonState.WaitingForApplication, LessonState.AwaitingDemonstrationApproval, LessonState.Completed],
        [LessonState.Pointing] = [LessonState.WaitingForLearner, LessonState.Observing],
        [LessonState.WaitingForLearner] = [LessonState.Observing, LessonState.Verifying, LessonState.Pointing, LessonState.AnsweringQuestion, LessonState.AwaitingDemonstrationApproval],
        [LessonState.Verifying] = [LessonState.Presenting, LessonState.Recovering, LessonState.WaitingForApplication, LessonState.Completed],
        [LessonState.AnsweringQuestion] = [LessonState.WaitingForLearner, LessonState.Observing],
        [LessonState.WaitingForApplication] = [LessonState.Observing],
        [LessonState.Recovering] = [LessonState.Observing, LessonState.AnsweringQuestion],
        [LessonState.AwaitingDemonstrationApproval] = [LessonState.Demonstrating, LessonState.WaitingForLearner],
        [LessonState.Demonstrating] = [LessonState.Verifying],
        [LessonState.Paused] = [LessonState.Consent],
        [LessonState.PrivacyPaused] = [LessonState.Consent],
        [LessonState.NetworkInterrupted] = [LessonState.Consent],
    };

    public void Transition(LessonState next)
    {
        lock (gate)
        {
            if (State == LessonState.Stopped && next != LessonState.Stopped)
                throw new SafetyException("Stopped session cannot resume");
            var interruption = next is LessonState.Stopped or LessonState.Paused or LessonState.PrivacyPaused or LessonState.NetworkInterrupted;
            if (!interruption && (!Allowed.TryGetValue(State, out var choices) || !choices.Contains(next)))
                throw new SafetyException($"Invalid transition: {State} to {next}");
            state = next;
        }
        Changed?.Invoke(next);
    }
}
