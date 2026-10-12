using ControlPlane.Api.Features.Agents.Models;

namespace ControlPlane.Api.Features.Agents.Messaging;

public interface IAgentMessageBus
{
    string PodId { get; }

    Task PublishPresenceAsync(Guid hostId, string state, CancellationToken ct = default);
    Task PublishCommandAsync(Guid targetHostId, AgentCommandEnvelope command, CancellationToken ct = default);
    Task PublishJobLogAsync(Guid jobId, long sequenceId, string streamType, string logLine, DateTimeOffset timestamp, CancellationToken ct = default);
    Task PublishJobStatusAsync(Guid jobId, string status, string? activeStep, CancellationToken ct = default);

    event Func<AgentPresencePayload, Task>? OnPresenceReceived;
    event Func<AgentCommandPayload, Task>? OnCommandReceived;
    event Func<JobLogPayload, Task>? OnJobLogReceived;
    event Func<JobStatusPayload, Task>? OnJobStatusReceived;
}
