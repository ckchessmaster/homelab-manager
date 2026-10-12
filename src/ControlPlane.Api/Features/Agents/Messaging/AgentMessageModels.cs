using ControlPlane.Api.Features.Agents.Models;

namespace ControlPlane.Api.Features.Agents.Messaging;

public static class AgentBusChannels
{
    public const string ControlPlaneBus = "controlplane_bus";
}

public record InterPodEnvelope(
    string Type,
    string OriginPodId,
    string PayloadJson,
    DateTimeOffset Timestamp
);

public record AgentPresencePayload(
    Guid HostId,
    string State, // "Connected" | "Disconnected" | "Heartbeat"
    string PodId,
    DateTimeOffset Timestamp
);

public record AgentCommandPayload(
    Guid TargetHostId,
    AgentCommandEnvelope Command,
    string OriginPodId
);

public record JobLogPayload(
    Guid JobId,
    long SequenceId,
    string StreamType,
    string LogLine,
    DateTimeOffset Timestamp
);

public record JobStatusPayload(
    Guid JobId,
    string Status,
    string? ActiveStep
);

public record RemoteAgentSession(
    Guid HostId,
    string PodId,
    DateTimeOffset LastSeenAt
);
