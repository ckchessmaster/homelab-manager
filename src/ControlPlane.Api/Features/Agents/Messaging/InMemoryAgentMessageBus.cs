using ControlPlane.Api.Features.Agents.Models;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Agents.Messaging;

public class InMemoryAgentMessageBus : IAgentMessageBus
{
    private readonly ILogger<InMemoryAgentMessageBus> _logger;

    public string PodId { get; } = "local-single-instance";

#pragma warning disable CS0067
    public event Func<AgentPresencePayload, Task>? OnPresenceReceived;
    public event Func<AgentCommandPayload, Task>? OnCommandReceived;
    public event Func<JobLogPayload, Task>? OnJobLogReceived;
    public event Func<JobStatusPayload, Task>? OnJobStatusReceived;
#pragma warning restore CS0067

    public InMemoryAgentMessageBus(ILogger<InMemoryAgentMessageBus> logger)
    {
        _logger = logger;
    }

    public Task PublishPresenceAsync(Guid hostId, string state, CancellationToken ct = default)
    {
        _logger.LogDebug("Local presence event for host {HostId}: {State}", hostId, state);
        return Task.CompletedTask;
    }

    public Task PublishCommandAsync(Guid targetHostId, AgentCommandEnvelope command, CancellationToken ct = default)
    {
        _logger.LogDebug("Local command event for host {HostId}: {Type}", targetHostId, command.Type);
        return Task.CompletedTask;
    }

    public Task PublishJobLogAsync(Guid jobId, long sequenceId, string streamType, string logLine, DateTimeOffset timestamp, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task PublishJobStatusAsync(Guid jobId, string status, string? activeStep, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
