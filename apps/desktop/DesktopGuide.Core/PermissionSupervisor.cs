namespace DesktopGuide.Core;

public sealed class LocalCapability
{
    internal LocalCapability(Guid nonce) { Nonce = nonce; }
    internal Guid Nonce { get; }
}
public sealed class DispatchLease
{
    internal DispatchLease(ActionProposal proposal, long epoch, Guid scope, Guid target, DateTimeOffset expires)
        => (Proposal, Epoch, Scope, Target, Expires) = (proposal, epoch, scope, target, expires);
    public ActionProposal Proposal { get; }
    internal long Epoch { get; }
    internal Guid Scope { get; }
    internal Guid Target { get; }
    internal DateTimeOffset Expires { get; }
}

public sealed class PermissionSupervisor(TimeProvider? clock = null)
{
    private readonly object gate = new();
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly Dictionary<Guid, (ActionProposal Proposal, ScreenFrame Frame, long Epoch, DateTimeOffset Expires)> approvals = [];
    private readonly HashSet<Guid> consumedProposals = [];
    private CancellationTokenSource lifetime = new();
    private Guid scope;
    private bool observation, pointer, demonstration;
    private bool autonomous;
    private Guid autonomousTarget;
    private DateTimeOffset autonomousExpires;
    private int autonomousActionsLeft;
    private InteractionMode mode;
    private long epoch;
    public long Epoch { get { lock (gate) return epoch; } }
    public CancellationToken Cancellation { get { lock (gate) return lifetime.Token; } }
    public int AutonomousActionsRemaining { get { lock (gate) return autonomousActionsLeft; } }

    public void GrantAutonomousTaskFromLocalConsent(Guid scopeId, Guid targetId, bool consent)
    {
        lock (gate)
        {
            RevokeLocked();
            if (!consent || scopeId == Guid.Empty || targetId == Guid.Empty) throw new SafetyException("Autonomous control needs fresh task consent.");
            scope = scopeId; autonomousTarget = targetId; mode = InteractionMode.Autonomous;
            observation = autonomous = true;
            autonomousExpires = clock.GetUtcNow().AddMinutes(10); autonomousActionsLeft = 30;
        }
    }

    public void RequireAutonomousTask(Guid scopeId, Guid targetId)
    {
        lock (gate)
            if (!observation || !autonomous || mode != InteractionMode.Autonomous || scope != scopeId || autonomousTarget != targetId
                || autonomousActionsLeft <= 0 || autonomousExpires <= clock.GetUtcNow() || lifetime.IsCancellationRequested)
                throw new SafetyException("Autonomous task permission expired or reached its 30-action limit. Resume with fresh consent.");
    }

    public LocalCapability ApproveAutonomousAction(ActionProposal proposal, ScreenFrame frame)
    {
        lock (gate)
        {
            RequireAutonomousTask(frame.Observation.ScopeId, frame.Geometry.LocalTargetId);
            ValidateProposal(proposal, frame);
            if (consumedProposals.Contains(proposal.Id)) throw new SafetyException("Proposal replay rejected");
            var nonce = Guid.NewGuid(); approvals.Clear();
            var expires = clock.GetUtcNow().AddSeconds(15);
            approvals.Add(nonce, (proposal, frame, epoch, expires < autonomousExpires ? expires : autonomousExpires));
            return new(nonce);
        }
    }

    private bool InputGranted() => (demonstration && mode == InteractionMode.Demonstrate)
        || (autonomous && mode == InteractionMode.Autonomous && autonomousExpires > clock.GetUtcNow());

    public void GrantFromLocalConsent(Guid scopeId, InteractionMode selectedMode, bool observe, bool point, bool demonstrate)
    {
        lock (gate)
        {
            RevokeLocked();
            scope = scopeId; mode = selectedMode;
            observation = observe; pointer = point; demonstration = demonstrate;
        }
    }

    public void RequireObservation(Guid scopeId)
    {
        lock (gate)
            if (!observation || scope != scopeId || lifetime.IsCancellationRequested)
                throw new SafetyException("Observation permission missing");
    }

    public void GuideBatch(ScreenFrame frame, bool foreground, Action dispatch)
    {
        lock (gate)
        {
            frame.Validate();
            if (!observation || !pointer || scope != frame.Observation.ScopeId || mode == InteractionMode.Practice
                || !foreground || lifetime.IsCancellationRequested)
                throw new SafetyException("Pointer guidance is not permitted");
            dispatch(); // Audited Win32 batches only, never asynchronous or blocking work.
        }
    }

    public LocalCapability ApproveFromLocalEvent(ActionProposal proposal, ScreenFrame frame)
    {
        lock (gate)
        {
            ValidateProposal(proposal, frame);
            if (!observation || !demonstration || mode != InteractionMode.Demonstrate || scope != frame.Observation.ScopeId
                || lifetime.IsCancellationRequested || consumedProposals.Contains(proposal.Id))
                throw new SafetyException("Demonstration permission missing or proposal already used");
            var nonce = Guid.NewGuid();
            approvals.Clear();
            approvals.Add(nonce, (proposal, frame, epoch, clock.GetUtcNow().AddSeconds(15)));
            return new LocalCapability(nonce);
        }
    }

    public DispatchLease Consume(LocalCapability capability, ActionProposal proposal, ScreenFrame current, bool foreground)
    {
        lock (gate)
        {
            if (!approvals.Remove(capability.Nonce, out var grant)) throw new SafetyException("Approval already consumed or revoked");
            ValidateProposal(proposal, current);
            if (grant.Proposal != proposal || grant.Epoch != epoch || grant.Expires <= clock.GetUtcNow()
                || grant.Frame.Geometry != current.Geometry || grant.Frame.PixelDigest != current.PixelDigest
                || grant.Frame.Observation.Id != current.Observation.Id || !foreground
                || !InputGranted() || lifetime.IsCancellationRequested)
                throw new SafetyException("Approval no longer matches the current target");
            if (mode == InteractionMode.Autonomous)
            { RequireAutonomousTask(current.Observation.ScopeId, current.Geometry.LocalTargetId); autonomousActionsLeft--; }
            if (!consumedProposals.Add(proposal.Id)) throw new SafetyException("Proposal replay rejected");
            return new DispatchLease(proposal, epoch, scope, current.Geometry.LocalTargetId, grant.Expires);
        }
    }

    public void ExecuteBatch(DispatchLease lease, ScreenFrame current, bool foreground, Action dispatch)
    {
        lock (gate)
        {
            if (lease.Epoch != epoch || lease.Scope != scope || lease.Target != current.Geometry.LocalTargetId
                || lease.Expires <= clock.GetUtcNow() || !observation || !InputGranted()
                || !foreground || lifetime.IsCancellationRequested)
                throw new SafetyException("Execution disarmed");
            dispatch();
        }
    }

    public void Revoke()
    {
        lock (gate) RevokeLocked();
    }
    public void CancelPendingInput()
    {
        lock (gate)
        {
            lifetime.Cancel(); lifetime.Dispose(); lifetime = new CancellationTokenSource();
            epoch++; approvals.Clear(); autonomous = false; autonomousActionsLeft = 0;
        }
    }
    private void RevokeLocked()
    {
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = new CancellationTokenSource();
        observation = pointer = demonstration = autonomous = false; autonomousActionsLeft = 0;
        epoch++;
        approvals.Clear();
    }
    private static void ValidateProposal(ActionProposal proposal, ScreenFrame frame)
    {
        frame.Validate(); proposal.Action.Validate();
        if (proposal.SourceObservationId != frame.Observation.Id || proposal.GeometryId != frame.Geometry.Id
            || proposal.Risk != "low" || proposal.ProviderConfirmationRequired || string.IsNullOrWhiteSpace(proposal.Effect))
            throw new SafetyException("Proposal requires manual handling or different evidence");
    }
}
