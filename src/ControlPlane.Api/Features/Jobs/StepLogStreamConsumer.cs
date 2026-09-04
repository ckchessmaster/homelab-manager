using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Hubs;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Features.Jobs;

public class StepLogStreamConsumer : IStepLogConsumer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<JobLogHub, IJobClient> _hubContext;
    private readonly IAgentCommandExecutor? _commandExecutor;
    private readonly ILogger<StepLogStreamConsumer> _logger;

    public StepLogStreamConsumer(
        IServiceScopeFactory scopeFactory,
        IHubContext<JobLogHub, IJobClient> hubContext,
        ILogger<StepLogStreamConsumer> logger,
        IAgentCommandExecutor? commandExecutor = null)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
        _commandExecutor = commandExecutor;
    }

    public async Task ConsumeFrameAsync(Guid hostId, AgentFrameData frame, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

            // Ensure job exists to satisfy foreign key constraint
            var job = await db.UpdateJobs.FirstOrDefaultAsync(j => j.Id == frame.JobId, cancellationToken);
            if (job == null)
            {
                job = new UpdateJob
                {
                    Id = frame.JobId,
                    TargetHostId = hostId,
                    InitiatedBy = "Operator",
                    Status = "Running",
                    StartedAt = DateTimeOffset.UtcNow,
                    ActiveStep = "Command Execution"
                };
                db.UpdateJobs.Add(job);
                await db.SaveChangesAsync(cancellationToken);
            }

            var stepLog = new StepLog
            {
                JobId = frame.JobId,
                SequenceId = frame.SequenceId,
                StreamType = frame.StreamType,
                LogLine = frame.LogLine,
                Timestamp = frame.Timestamp
            };

            db.StepLogs.Add(stepLog);

            // Update job status if terminal state indicated
            if (frame.StreamType == "system" && frame.LogLine.Contains("completed successfully", StringComparison.OrdinalIgnoreCase))
            {
                job.Status = "Completed";
                job.CompletedAt = DateTimeOffset.UtcNow;
                _ = _hubContext.Clients.Group(frame.JobId.ToString()).JobStatusChanged(frame.JobId, "Completed", null);
            }
            else if (frame.StreamType == "system" && frame.LogLine.Contains("exited with code", StringComparison.OrdinalIgnoreCase) && !frame.LogLine.Contains("code 0", StringComparison.OrdinalIgnoreCase))
            {
                job.Status = "Failed";
                job.FailureReason = frame.LogLine;
                job.CompletedAt = DateTimeOffset.UtcNow;
                _ = _hubContext.Clients.Group(frame.JobId.ToString()).JobStatusChanged(frame.JobId, "Failed", null);
            }

            await db.SaveChangesAsync(cancellationToken);

            // Broadcast real-time log frame to SignalR subscribers
            await _hubContext.Clients.Group(frame.JobId.ToString()).ReceiveLogLine(
                frame.JobId,
                frame.SequenceId,
                frame.StreamType,
                frame.LogLine,
                frame.Timestamp
            );

            _commandExecutor?.NotifyFrame(hostId, frame);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to consume frame sequence {SequenceId} for job {JobId}", frame.SequenceId, frame.JobId);
        }
    }
}
