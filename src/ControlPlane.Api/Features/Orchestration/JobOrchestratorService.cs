using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Hubs;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Features.Orchestration;

public class JobOrchestratorService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<JobLogHub, IJobClient> _hubContext;
    private readonly IAgentCommandExecutor _commandExecutor;
    private readonly AgentConnectionManager _connectionManager;
    private readonly ILogger<JobOrchestratorService> _logger;

    public JobOrchestratorService(
        IServiceScopeFactory scopeFactory,
        IHubContext<JobLogHub, IJobClient> hubContext,
        IAgentCommandExecutor commandExecutor,
        AgentConnectionManager connectionManager,
        ILogger<JobOrchestratorService> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _commandExecutor = commandExecutor;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<(UpdateJob? Job, string? Error)> CreateAndStartJobAsync(
        Guid targetHostId,
        string initiatedBy = "Operator",
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == targetHostId, ct);
        if (host == null)
        {
            return (null, $"Host {targetHostId} not found.");
        }

        var job = new UpdateJob
        {
            Id = Guid.NewGuid(),
            TargetHostId = host.Id,
            InitiatedBy = initiatedBy,
            Status = UpdateJobState.Pending,
            StartedAt = DateTimeOffset.UtcNow
        };

        db.UpdateJobs.Add(job);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Created update job {JobId} for host {Hostname} ({HostId})", job.Id, host.Hostname, host.Id);

        // Notify UI of new pending job
        await _hubContext.Clients.Group(job.Id.ToString()).JobStatusChanged(job.Id, UpdateJobState.Pending, null);

        // Run DAG asynchronously in background
        _ = Task.Run(async () =>
        {
            try
            {
                await RunJobPipelineAsync(job.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background execution of job {JobId} encountered an error", job.Id);
            }
        });

        return (job, null);
    }

    public async Task<bool> RunJobPipelineAsync(Guid jobId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var job = await db.UpdateJobs
            .Include(j => j.TargetHost)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job == null)
        {
            _logger.LogError("Job {JobId} not found for execution", jobId);
            return false;
        }

        var context = new JobExecutionContext(
            job,
            job.TargetHost,
            _scopeFactory,
            _hubContext,
            _commandExecutor,
            _connectionManager,
            _logger
        );

        var pipeline = BuildDefaultUpgradePipeline();
        return await pipeline.ExecuteAsync(context, ct);
    }

    public virtual DagExecutionPipeline BuildDefaultUpgradePipeline()
    {
        var steps = new List<IJobStep>
        {
            new PreflightHeartbeatCheckStep(),
            new PreflightDiskHeadroomCheckStep(),
            new PreflightPackageLockCheckStep(),
            new ProxmoxSnapshotStep(),
            new PackageUpgradeStep(),
            new DeterministicRebootStep(),
            new AwaitReconnectionStep(),
            new PostFlightHealthProbeStep()
        };

        return new DagExecutionPipeline(steps);
    }

    public async Task<UpdateJob?> GetJobByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        return await db.UpdateJobs
            .AsNoTracking()
            .Include(j => j.TargetHost)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
    }

    public async Task<List<UpdateJob>> ListJobsAsync(Guid? hostId = null, int limit = 50, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();

        var query = db.UpdateJobs.AsNoTracking();

        if (hostId.HasValue)
        {
            query = query.Where(j => j.TargetHostId == hostId.Value);
        }

        if (db.Database.IsSqlite())
        {
            var jobs = await query.ToListAsync(ct);
            return jobs
                .OrderByDescending(j => j.StartedAt ?? DateTimeOffset.MinValue)
                .Take(limit)
                .ToList();
        }

        return await query
            .OrderByDescending(j => j.StartedAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}
