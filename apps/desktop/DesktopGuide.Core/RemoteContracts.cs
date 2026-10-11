namespace DesktopGuide.Core;

public sealed record RemoteRegistration(int SchemaVersion, Guid DeviceId, string Label, bool AllowAutonomous) : IVersioned;
public sealed record RemoteLease(int SchemaVersion, Guid ConnectionId, string DesktopKey, DateTimeOffset ExpiresAt) : IVersioned;
public sealed record RemoteStatus(string State, string Mode, bool Busy, string Instruction, string Notice, bool CanResume, bool CanCheck);
public sealed record RemoteAcknowledgement(Guid CommandId, bool Accepted, string Message);
public sealed record RemotePoll(int SchemaVersion, RemoteStatus Status, RemoteAcknowledgement? Acknowledgement) : IVersioned;
public sealed record RemoteCommand(Guid Id, string Kind, string? Mode, string? Goal, DateTimeOffset ExpiresAt);
public sealed record RemotePollResponse(int SchemaVersion, RemoteCommand? Command) : IVersioned;
public sealed record RemoteOk(int SchemaVersion, bool Ok) : IVersioned;
