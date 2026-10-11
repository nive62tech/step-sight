using DesktopGuide.Core;
using Xunit;

namespace DesktopGuide.Tests;

public sealed class SafetyTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ReadingABannerCanScrollButSensitiveActionsStillNeedAUser(bool manualRisk)
    {
        var (_, frame, proposal) = Setup();
        var step = new GuidanceStep(Guid.NewGuid(), frame.Observation.Id, "Scroll within the login and payment information banner", "",
            new("rectangle", "normalized_source_image", frame.Geometry.Id, new(.4,.4,.6,.6), "Notice"), "scroll",
            [new("visible_text", "Bottom of notice")], [], [new(frame.Observation.Id, "Clipped notice with scrollbar")], []);
        proposal = proposal with { Action = new("scroll", .5, .5, Ticks: -2), Risk = manualRisk ? "manual" : "low" };
        if (manualRisk) Assert.Throws<SafetyException>(() => LocalRiskPolicy.RequireAutonomousAction(new(step, proposal)));
        else LocalRiskPolicy.RequireAutonomousAction(new(step, proposal));
        Assert.Throws<SafetyException>(() => LocalRiskPolicy.RequireAutonomousAction(new(step with { ExpectedUserAction = "click" }, proposal with { Action = new("click", .5, .5, "left") })));
    }
    [Theory] [InlineData(InteractionMode.Guide)] [InlineData(InteractionMode.Autonomous)]
    public void OrdinaryConsentNeverGrantsAutonomousInput(InteractionMode mode)
    {
        var (s, f, p) = Setup(mode);
        Assert.Throws<SafetyException>(() => s.ApproveAutonomousAction(p, f));
        Assert.Throws<SafetyException>(() => s.GrantAutonomousTaskFromLocalConsent(f.Observation.ScopeId, f.Geometry.LocalTargetId, false));
    }
    [Fact]
    public void AutonomousGrantIsTargetBoundExpiresAndHasAFiniteBudget()
    {
        var (_, f, p) = Setup(); var clock = new ManualClock(); var s = new PermissionSupervisor(clock);
        s.GrantAutonomousTaskFromLocalConsent(f.Observation.ScopeId, f.Geometry.LocalTargetId, true);
        Assert.Throws<SafetyException>(() => s.RequireAutonomousTask(f.Observation.ScopeId, Guid.NewGuid()));
        for (var i = 0; i < 30; i++)
        {
            var action = p with { Id = Guid.NewGuid() };
            var capability = s.ApproveAutonomousAction(action, f);
            var lease = s.Consume(capability, action, f, true);
            s.ExecuteBatch(lease, f, true, () => { });
            Assert.Throws<SafetyException>(() => s.Consume(capability, action, f, true));
        }
        Assert.Equal(0, s.AutonomousActionsRemaining);
        Assert.Throws<SafetyException>(() => s.ApproveAutonomousAction(p, f));
        s.GrantAutonomousTaskFromLocalConsent(f.Observation.ScopeId, f.Geometry.LocalTargetId, true);
        clock.Current += TimeSpan.FromMinutes(10);
        Assert.Throws<SafetyException>(() => s.ApproveAutonomousAction(p, f));
    }
    [Fact]
    public void AutonomousCapabilitiesAreExactSingleUseAndStopRevokesThem()
    {
        var (s, f, p) = Setup(); s.GrantAutonomousTaskFromLocalConsent(f.Observation.ScopeId, f.Geometry.LocalTargetId, true);
        var cap = s.ApproveAutonomousAction(p, f);
        Assert.Throws<SafetyException>(() => s.Consume(cap, p with { Action = new("click", .1, .1, "left") }, f, true));
        cap = s.ApproveAutonomousAction(p, f); var lease = s.Consume(cap, p, f, true);
        Assert.Throws<SafetyException>(() => s.ApproveAutonomousAction(p, f));
        s.Revoke(); Assert.Throws<SafetyException>(() => s.ExecuteBatch(lease, f, true, () => throw new Exception("Unexpected input")));
    }
    [Theory] [InlineData("Click Pay")] [InlineData("Enter password")] [InlineData("Confirm booking")] [InlineData("Deploy project")]
    public void AutonomousSensitiveActionsStayManual(string instruction)
    {
        var (_, f, p) = Setup();
        var step = new GuidanceStep(Guid.NewGuid(), f.Observation.Id, instruction, "", new("rectangle", "normalized_source_image", f.Geometry.Id, new(.4,.4,.6,.6), ""), "click", [new("visible_text", "Next")], [], [new(f.Observation.Id, "Visible")], []);
        Assert.Throws<SafetyException>(() => LocalRiskPolicy.RequireAutonomousAction(new(step, p)));
        LocalRiskPolicy.RequireAutonomousAction(new(step with { Instruction = "Open train search" }, p));
    }
    private static (PermissionSupervisor Supervisor, ScreenFrame Frame, ActionProposal Proposal) Setup(InteractionMode mode = InteractionMode.Demonstrate)
    {
        var scope = Guid.NewGuid(); var geometryId = Guid.NewGuid(); var observationId = Guid.NewGuid();
        var geometry = new Geometry(geometryId, scope, new(-1920, 0, 1920, 1080), 1600, 900, 144,
            "monitor-2", Guid.NewGuid(), new(0, 0, 1, 1), new(0, 0, 1, 1));
        var frame = new ScreenFrame(new(observationId, DateTimeOffset.UtcNow, scope, geometryId, 1600, 900, true), geometry, [1], "digest");
        var proposal = new ActionProposal(Guid.NewGuid(), observationId, geometryId, new("click", .5, .5, "left"), "Open practice Settings", "low", false);
        var supervisor = new PermissionSupervisor();
        supervisor.GrantFromLocalConsent(scope, mode, true, true, true);
        return (supervisor, frame, proposal);
    }

    [Theory]
    [InlineData(InteractionMode.Guide)]
    [InlineData(InteractionMode.Practice)]
    public void GuideAndPracticeCannotApproveInput(InteractionMode mode)
    {
        var (supervisor, frame, proposal) = Setup(mode);
        Assert.Throws<SafetyException>(() => supervisor.ApproveFromLocalEvent(proposal, frame));
    }

    [Fact]
    public void CapabilityIsSingleUseAndPayloadExact()
    {
        var (supervisor, frame, proposal) = Setup();
        var capability = supervisor.ApproveFromLocalEvent(proposal, frame);
        Assert.Throws<SafetyException>(() => supervisor.Consume(capability, proposal with { Action = new("click", .1, .1, "left") }, frame, true));
        Assert.Throws<SafetyException>(() => supervisor.Consume(capability, proposal, frame, true));
    }

    [Fact]
    public void DispatchLeaseDiesOnStop()
    {
        var (supervisor, frame, proposal) = Setup();
        var capability = supervisor.ApproveFromLocalEvent(proposal, frame);
        var lease = supervisor.Consume(capability, proposal, frame, true);
        int dispatched = 0;
        supervisor.ExecuteBatch(lease, frame, true, () => dispatched++);
        supervisor.Revoke();
        Assert.Throws<SafetyException>(() => supervisor.ExecuteBatch(lease, frame, true, () => dispatched++));
        Assert.Equal(1, dispatched);
        Assert.Throws<SafetyException>(() => supervisor.ApproveFromLocalEvent(proposal, frame));
    }

    [Theory]
    [InlineData("geometry")]
    [InlineData("pixels")]
    [InlineData("observation")]
    [InlineData("scope")]
    [InlineData("foreground")]
    public void ChangedContextInvalidatesApproval(string change)
    {
        var (supervisor, frame, proposal) = Setup();
        var capability = supervisor.ApproveFromLocalEvent(proposal, frame);
        var changed = change switch
        {
            "geometry" => frame with { Geometry = frame.Geometry with { Dpi = 192 } },
            "pixels" => frame with { PixelDigest = "changed" },
            "observation" => frame with { Observation = frame.Observation with { Id = Guid.NewGuid() } },
            "scope" => frame with { Observation = frame.Observation with { ScopeId = Guid.NewGuid() } },
            _ => frame
        };
        Assert.Throws<SafetyException>(() => supervisor.Consume(capability, proposal, changed, change != "foreground"));
    }

    [Fact]
    public void EpochChangeInvalidatesOutstandingApproval()
    {
        var (supervisor, frame, proposal) = Setup();
        var capability = supervisor.ApproveFromLocalEvent(proposal, frame);
        supervisor.GrantFromLocalConsent(frame.Observation.ScopeId, InteractionMode.Demonstrate, true, true, true);
        Assert.Throws<SafetyException>(() => supervisor.Consume(capability, proposal, frame, true));
    }

    [Fact]
    public void ProviderConfirmationAndManualRiskCannotBeConvertedToApproval()
    {
        var (supervisor, frame, proposal) = Setup();
        Assert.Throws<SafetyException>(() => supervisor.ApproveFromLocalEvent(proposal with { ProviderConfirmationRequired = true }, frame));
        Assert.Throws<SafetyException>(() => supervisor.ApproveFromLocalEvent(proposal with { Risk = "manual" }, frame));
    }

    [Theory]
    [InlineData("drag", null)]
    [InlineData("key_chord", "Alt+F4")]
    [InlineData("key_chord", "Win+R")]
    public void UnsafePrimitivesAreRejected(string kind, string? chord)
        => Assert.Throws<SafetyException>(() => new InputAction(kind, Chord: chord).Validate());

    [Fact]
    public void TypingCannotSubmitOrRunCommands()
        => Assert.Throws<SafetyException>(() => new InputAction("type", Text: "hello\n").Validate());

    [Fact]
    public void PracticeNeverPullsPointer()
    {
        var (supervisor, frame, _) = Setup(InteractionMode.Practice);
        Assert.Throws<SafetyException>(() => supervisor.GuideBatch(frame, true, () => throw new Exception("must not dispatch")));
    }

    [Fact]
    public void StopIsTerminalAndServerCannotSkipApproval()
    {
        var machine = new SessionMachine();
        machine.Transition(LessonState.Consent); machine.Transition(LessonState.Observing);
        Assert.Throws<SafetyException>(() => machine.Transition(LessonState.Demonstrating));
        machine.Transition(LessonState.Stopped);
        Assert.Throws<SafetyException>(() => machine.Transition(LessonState.Consent));
    }

    [Fact]
    public async Task StopDuringBatchesPreventsEverySubsequentDispatch()
    {
        var (supervisor, frame, proposal) = Setup();
        var lease = supervisor.Consume(supervisor.ApproveFromLocalEvent(proposal, frame), proposal, frame, true);
        var dispatched = new List<int>();
        for (int i = 0; i < 100; i++)
        {
            if (i == 10) await Task.Run(supervisor.Revoke);
            try { supervisor.ExecuteBatch(lease, frame, true, () => dispatched.Add(i)); }
            catch (SafetyException) { }
        }
        Assert.Equal(Enumerable.Range(0, 10), dispatched);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Current = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Current;
    }

    [Fact]
    public void ExpiryRejectsApproval()
    {
        var (_, frame, proposal) = Setup();
        var clock = new ManualClock(); var supervisor = new PermissionSupervisor(clock);
        supervisor.GrantFromLocalConsent(frame.Observation.ScopeId, InteractionMode.Demonstrate, true, true, true);
        var capability = supervisor.ApproveFromLocalEvent(proposal, frame);
        clock.Current += TimeSpan.FromSeconds(16);
        Assert.Throws<SafetyException>(() => supervisor.Consume(capability, proposal, frame, true));
    }
}
