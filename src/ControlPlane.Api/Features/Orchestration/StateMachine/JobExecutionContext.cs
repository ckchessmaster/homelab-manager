using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Hubs;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Features.Orchestration;

/// <summary>
/// Execution context provided to DAG steps during pipeline execution.
/// </summary>
public class JobExecutionContext
{
    private long _sequenceCounter;

    public Guid JobId { get; }
    public HostEntity TargetHost { get; }
    public UpdateJob Job { get; }
    public IServiceScopeFactory ScopeFactory { get; }
    public IHubContext<JobLogHub, IJobClient> HubContext { get; }
    public IAgentCommandExecutor CommandExecutor { get; }
    public AgentConnectionManager ConnectionManager { get; }
    public ILogger Logger { get; }
    public IDictionary<string, object?> State { get; } = new Dictionary<string, object?>();

    public Guid HostId => TargetHost.Id;

    public JobExecutionContext(
        UpdateJob job,
        HostEntity targetHost,
        IServiceScopeFactory scopeFactory,
        IHubContext<JobLogHub, IJobClient> hubContext,
        IAgentCommandExecutor commandExecutor,
        AgentConnectionManager connectionManager,
        ILogger logger)
    {
        Job = job;
        JobId = job.Id;
        TargetHost = targetHost;
        ScopeFactory = scopeFactory;
        HubContext = hubContext;
        CommandExecutor = commandExecutor;
        ConnectionManager = connectionManager;
        Logger = logger;
    }

    /// <summary>
    /// Emits a framed log line, persists it to the database, and streams it to SignalR subscribers.
    /// </summary>
    public async Task EmitLogAsync(string streamType, string logLine, CancellationToken ct = default)
    {
        var seq = Interlocked.Increment(ref _sequenceCounter);
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            using var scope = ScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

            var logEntry = new StepLog
            {
                JobId = JobId,
                SequenceId = seq,
                StreamType = streamType,
                LogLine = logLine,
                Timestamp = timestamp
            };

            db.StepLogs.Add(logEntry);
            await db.SaveChangesAsync(ct);

            await HubContext.Clients.Group(JobId.ToString()).ReceiveLogLine(
                JobId,
                seq,
                streamType,
                logLine,
                timestamp
            );
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to persist log for job {JobId}: {LogLine}", JobId, logLine);
        }
    }

    /// <summary>
    /// Updates the job status and active step in the database and broadcasts the change via SignalR.
    /// </summary>
    public async Task UpdateJobStatusAsync(
        string status,
        string? activeStep,
        string? failureReason = null,
        CancellationToken ct = default)
    {
        Job.Status = status;
        Job.ActiveStep = activeStep;
        if (!string.IsNullOrEmpty(failureReason))
        {
            Job.FailureReason = failureReason;
        }

        if (status is UpdateJobState.Completed or UpdateJobState.Failed or UpdateJobState.RolledBack)
        {
            Job.CompletedAt = DateTimeOffset.UtcNow;
        }

        try
        {
            using var scope = ScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

            var trackedJob = await db.UpdateJobs.FirstOrDefaultAsync(j => j.Id == JobId, ct);
            if (trackedJob != null)
            {
                trackedJob.Status = Job.Status;
                trackedJob.ActiveStep = Job.ActiveStep;
                trackedJob.FailureReason = Job.FailureReason;
                trackedJob.CompletedAt = Job.CompletedAt;
                await db.SaveChangesAsync(ct);
            }

            await HubContext.Clients.Group(JobId.ToString()).JobStatusChanged(
                JobId,
                status,
                activeStep
            );
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update job status for {JobId} to {Status}", JobId, status);
        }
    }
}
