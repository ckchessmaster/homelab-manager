using System.Collections.Concurrent;
using ControlPlane.Api.Hubs;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using ControlPlane.Api.Features.Agents.Messaging;

namespace ControlPlane.Api.Features.Orchestration.Temporal;

public interface IWorkflowLogEmitter
{
    Task EmitLogAsync(Guid jobId, string streamType, string logLine, CancellationToken ct = default);
    Task UpdateJobStatusAsync(Guid jobId, string status, string? activeStep, string? failureReason = null, CancellationToken ct = default);
    Task UpdateJobStatusAsync(Guid jobId, string status, string? activeStep, string? failureReason, Guid? hostId, CancellationToken ct = default)
        => UpdateJobStatusAsync(jobId, status, activeStep, failureReason, ct);
    Task SetSnapshotIdentifierAsync(Guid jobId, string? snapshotIdentifier, CancellationToken ct = default);
}

public class WorkflowLogEmitter : IWorkflowLogEmitter
{
    private readonly ConcurrentDictionary<Guid, long> _sequenceCounters = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<JobLogHub, IJobClient> _hubContext;
    private readonly IAgentMessageBus? _messageBus;
    private readonly ILogger<WorkflowLogEmitter> _logger;

    public WorkflowLogEmitter(
        IServiceScopeFactory scopeFactory,
        IHubContext<JobLogHub, IJobClient> hubContext,
        ILogger<WorkflowLogEmitter> logger,
        IAgentMessageBus? messageBus = null)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
        _messageBus = messageBus;
    }

    public async Task EmitLogAsync(Guid jobId, string streamType, string logLine, CancellationToken ct = default)
    {
        var seq = _sequenceCounters.AddOrUpdate(jobId, 1, (_, current) => current + 1);
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

            var jobExists = await db.UpdateJobs.AnyAsync(j => j.Id == jobId, ct);
            if (jobExists)
            {
                var logEntry = new StepLog
                {
                    JobId = jobId,
                    SequenceId = seq,
                    StreamType = streamType,
                    LogLine = logLine,
                    Timestamp = timestamp
                };

                db.StepLogs.Add(logEntry);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                _logger.LogDebug("Job {JobId} not found in update_jobs. Skipping DB step_logs persistence to avoid foreign key violation.", jobId);
            }

            await _hubContext.Clients.Group(jobId.ToString()).ReceiveLogLine(
                jobId,
                seq,
                streamType,
                logLine,
                timestamp
            );

            if (_messageBus != null)
            {
                _ = _messageBus.PublishJobLogAsync(jobId, seq, streamType, logLine, timestamp, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit or persist log for job {JobId}: {LogLine}", jobId, logLine);
        }
    }

    public Task UpdateJobStatusAsync(
        Guid jobId,
        string status,
        string? activeStep,
        string? failureReason = null,
        CancellationToken ct = default)
        => UpdateJobStatusAsync(jobId, status, activeStep, failureReason, null, ct);

    public async Task UpdateJobStatusAsync(
        Guid jobId,
        string status,
        string? activeStep,
        string? failureReason,
        Guid? hostId,
        CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

            var job = await db.UpdateJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
            if (job == null && hostId.HasValue && hostId.Value != Guid.Empty)
            {
                job = new UpdateJob
                {
                    Id = jobId,
                    TargetHostId = hostId.Value,
                    PipelineId = "temporal-host-upgrade",
                    InitiatedBy = "RollingUpgradeWorkflow",
                    Status = status,
                    ActiveStep = activeStep,
                    StartedAt = DateTimeOffset.UtcNow,
                    FailureReason = failureReason
                };
                db.UpdateJobs.Add(job);
                await db.SaveChangesAsync(ct);
            }
            else if (job != null)
            {
                job.Status = status;
                job.ActiveStep = activeStep;
                job.FailureReason = failureReason;

                if (status is "Completed" or "Failed" or "RolledBack")
                {
                    job.CompletedAt = DateTimeOffset.UtcNow;
                }

                await db.SaveChangesAsync(ct);
            }

            await _hubContext.Clients.Group(jobId.ToString()).JobStatusChanged(
                jobId,
                status,
                activeStep
            );

            if (_messageBus != null)
            {
                _ = _messageBus.PublishJobStatusAsync(jobId, status, activeStep, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update job status for {JobId} to {Status}", jobId, status);
        }
    }

    public async Task SetSnapshotIdentifierAsync(Guid jobId, string? snapshotIdentifier, CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

            var job = await db.UpdateJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
            if (job != null)
            {
                job.SnapshotIdentifier = snapshotIdentifier;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist snapshot identifier for job {JobId}", jobId);
        }
    }
}
