using System.Collections.Concurrent;
using ControlPlane.Api.Hubs;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Features.Orchestration.Temporal;

public interface IWorkflowLogEmitter
{
    Task EmitLogAsync(Guid jobId, string streamType, string logLine, CancellationToken ct = default);
    Task UpdateJobStatusAsync(Guid jobId, string status, string? activeStep, string? failureReason = null, CancellationToken ct = default);
    Task SetSnapshotIdentifierAsync(Guid jobId, string? snapshotIdentifier, CancellationToken ct = default);
}

public class WorkflowLogEmitter : IWorkflowLogEmitter
{
    private readonly ConcurrentDictionary<Guid, long> _sequenceCounters = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<JobLogHub, IJobClient> _hubContext;
    private readonly ILogger<WorkflowLogEmitter> _logger;

    public WorkflowLogEmitter(
        IServiceScopeFactory scopeFactory,
        IHubContext<JobLogHub, IJobClient> hubContext,
        ILogger<WorkflowLogEmitter> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task EmitLogAsync(Guid jobId, string streamType, string logLine, CancellationToken ct = default)
    {
        var seq = _sequenceCounters.AddOrUpdate(jobId, 1, (_, current) => current + 1);
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

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

            await _hubContext.Clients.Group(jobId.ToString()).ReceiveLogLine(
                jobId,
                seq,
                streamType,
                logLine,
                timestamp
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit or persist log for job {JobId}: {LogLine}", jobId, logLine);
        }
    }

    public async Task UpdateJobStatusAsync(
        Guid jobId,
        string status,
        string? activeStep,
        string? failureReason = null,
        CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

            var job = await db.UpdateJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
            if (job != null)
            {
                job.Status = status;
                job.ActiveStep = activeStep;
                if (!string.IsNullOrEmpty(failureReason))
                {
                    job.FailureReason = failureReason;
                }

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
