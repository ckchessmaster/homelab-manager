using ControlPlane.Api.Features.Agents.Messaging;
using ControlPlane.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Jobs;

public class JobLogHubBridge : IHostedService
{
    private readonly IAgentMessageBus _messageBus;
    private readonly IHubContext<JobLogHub, IJobClient> _hubContext;
    private readonly ILogger<JobLogHubBridge> _logger;

    public JobLogHubBridge(
        IAgentMessageBus messageBus,
        IHubContext<JobLogHub, IJobClient> hubContext,
        ILogger<JobLogHubBridge> logger)
    {
        _messageBus = messageBus;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _messageBus.OnJobLogReceived += HandleJobLogAsync;
        _messageBus.OnJobStatusReceived += HandleJobStatusAsync;
        _logger.LogInformation("JobLogHubBridge subscribed to inter-pod job logs and status events");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _messageBus.OnJobLogReceived -= HandleJobLogAsync;
        _messageBus.OnJobStatusReceived -= HandleJobStatusAsync;
        return Task.CompletedTask;
    }

    private async Task HandleJobLogAsync(JobLogPayload payload)
    {
        try
        {
            await _hubContext.Clients.Group(payload.JobId.ToString()).ReceiveLogLine(
                payload.JobId,
                payload.SequenceId,
                payload.StreamType,
                payload.LogLine,
                payload.Timestamp
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast remote job log to SignalR group {JobId}", payload.JobId);
        }
    }

    private async Task HandleJobStatusAsync(JobStatusPayload payload)
    {
        try
        {
            await _hubContext.Clients.Group(payload.JobId.ToString()).JobStatusChanged(
                payload.JobId,
                payload.Status,
                payload.ActiveStep
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast remote job status to SignalR group {JobId}", payload.JobId);
        }
    }
}
