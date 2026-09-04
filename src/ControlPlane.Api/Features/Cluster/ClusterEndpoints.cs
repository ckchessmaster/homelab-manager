using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Features.Cluster;

public static class ClusterEndpoints
{
    public const string GlobalMaintenanceLockKey = "GLOBAL_MAINTENANCE_LOCK";

    public static IEndpointRouteBuilder MapClusterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/cluster")
            .RequireAuthorization();

        group.MapGet("/status", (ClusterState state) => Results.Ok(new
        {
            isSuspended = state.IsSuspended,
            currentLeaseHolder = state.CurrentLeaseHolder,
            leaseExpiresAt = state.LeaseExpiresAt,
            timestamp = DateTimeOffset.UtcNow
        }));

        group.MapGet("/export-snapshot", async (ControlPlaneDbContext db, CancellationToken ct) =>
        {
            var hosts = await db.Hosts.AsNoTracking().ToListAsync(ct);
            var jobs = await db.UpdateJobs.AsNoTracking().ToListAsync(ct);
            var logs = await db.StepLogs.AsNoTracking().ToListAsync(ct);

            var snapshot = new ClusterSnapshot(
                ExportedAt: DateTimeOffset.UtcNow,
                Hosts: hosts.Select(HostSnapshotDto.FromEntity).ToList(),
                UpdateJobs: jobs.Select(JobSnapshotDto.FromEntity).ToList(),
                StepLogs: logs.Select(StepLogSnapshotDto.FromEntity).ToList()
            );

            return Results.Ok(snapshot);
        });

        group.MapPost("/lease-acquire", async (
            LeaseAcquireRequest request,
            ControlPlaneDbContext db,
            ClusterState clusterState,
            CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var durationMinutes = request.DurationMinutes > 0 ? request.DurationMinutes : 60;
            var expiresAt = now.AddMinutes(durationMinutes);

            var existingLease = await db.ClusterLeases
                .FirstOrDefaultAsync(l => l.LeaseKey == GlobalMaintenanceLockKey, ct);

            if (existingLease != null && existingLease.ExpiresAt > now &&
                !string.Equals(existingLease.HolderIdentifier, request.HolderIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Conflict(new
                {
                    message = $"Global maintenance lock is currently held by '{existingLease.HolderIdentifier}' until {existingLease.ExpiresAt:u}.",
                    currentHolder = existingLease.HolderIdentifier,
                    expiresAt = existingLease.ExpiresAt
                });
            }

            if (existingLease == null)
            {
                existingLease = new ClusterLease
                {
                    LeaseKey = GlobalMaintenanceLockKey,
                    HolderIdentifier = request.HolderIdentifier,
                    AcquiredAt = now,
                    ExpiresAt = expiresAt
                };
                db.ClusterLeases.Add(existingLease);
            }
            else
            {
                existingLease.HolderIdentifier = request.HolderIdentifier;
                existingLease.AcquiredAt = now;
                existingLease.ExpiresAt = expiresAt;
            }

            await db.SaveChangesAsync(ct);

            clusterState.IsSuspended = true;
            clusterState.CurrentLeaseHolder = request.HolderIdentifier;
            clusterState.LeaseExpiresAt = expiresAt;

            return Results.Ok(new
            {
                leaseKey = GlobalMaintenanceLockKey,
                holder = request.HolderIdentifier,
                acquiredAt = existingLease.AcquiredAt,
                expiresAt = existingLease.ExpiresAt,
                isSuspended = true
            });
        });

        group.MapPost("/lease-release", async (
            LeaseReleaseRequest request,
            ControlPlaneDbContext db,
            ClusterState clusterState,
            CancellationToken ct) =>
        {
            var existingLease = await db.ClusterLeases
                .FirstOrDefaultAsync(l => l.LeaseKey == GlobalMaintenanceLockKey, ct);

            if (existingLease != null)
            {
                db.ClusterLeases.Remove(existingLease);
                await db.SaveChangesAsync(ct);
            }

            clusterState.IsSuspended = false;
            clusterState.CurrentLeaseHolder = null;
            clusterState.LeaseExpiresAt = null;

            return Results.Ok(new
            {
                message = "Global maintenance lock released successfully.",
                isSuspended = false
            });
        });

        group.MapPost("/reconcile-delta", async (
            DeltaSyncPayload payload,
            ControlPlaneDbContext db,
            CancellationToken ct) =>
        {
            var reconciledJobsCount = 0;
            var reconciledLogsCount = 0;

            // Reconcile jobs
            foreach (var jobDto in payload.UpdateJobs)
            {
                var existingJob = await db.UpdateJobs.FindAsync(new object[] { jobDto.Id }, ct);
                if (existingJob == null)
                {
                    // Ensure host exists first
                    var hostExists = await db.Hosts.AnyAsync(h => h.Id == jobDto.TargetHostId, ct);
                    if (!hostExists)
                    {
                        var matchingHostDto = payload.Hosts.FirstOrDefault(h => h.Id == jobDto.TargetHostId);
                        if (matchingHostDto != null)
                        {
                            var newHost = matchingHostDto.ToEntity();
                            db.Hosts.Add(newHost);
                            await db.SaveChangesAsync(ct);
                        }
                    }

                    var newJob = jobDto.ToEntity();
                    db.UpdateJobs.Add(newJob);
                    reconciledJobsCount++;
                }
                else
                {
                    existingJob.Status = jobDto.Status;
                    existingJob.ActiveStep = jobDto.ActiveStep;
                    existingJob.SnapshotIdentifier = jobDto.SnapshotIdentifier;
                    existingJob.StartedAt = jobDto.StartedAt;
                    existingJob.CompletedAt = jobDto.CompletedAt;
                    existingJob.FailureReason = jobDto.FailureReason;
                    reconciledJobsCount++;
                }
            }

            // Reconcile step logs
            foreach (var logDto in payload.StepLogs)
            {
                var exists = await db.StepLogs.AnyAsync(l => l.JobId == logDto.JobId && l.SequenceId == logDto.SequenceId, ct);
                if (!exists)
                {
                    db.StepLogs.Add(logDto.ToEntity());
                    reconciledLogsCount++;
                }
            }

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                message = "Delta synchronization reconciled successfully.",
                reconciledJobsCount,
                reconciledLogsCount,
                timestamp = DateTimeOffset.UtcNow
            });
        });

        return app;
    }
}

// Data Transfer Records
public record LeaseAcquireRequest(string HolderIdentifier, int DurationMinutes = 60);
public record LeaseReleaseRequest(string HolderIdentifier);

public record ClusterSnapshot(
    DateTimeOffset ExportedAt,
    List<HostSnapshotDto> Hosts,
    List<JobSnapshotDto> UpdateJobs,
    List<StepLogSnapshotDto> StepLogs
);

public record DeltaSyncPayload(
    List<HostSnapshotDto> Hosts,
    List<JobSnapshotDto> UpdateJobs,
    List<StepLogSnapshotDto> StepLogs
);

public record HostSnapshotDto(
    Guid Id,
    string Hostname,
    string? FriendlyName,
    string IpAddress,
    string OsFamily,
    string TargetType,
    string? ProxmoxNode,
    int? ProxmoxVmid,
    string? IdracIp,
    string? UnifiSwitchMac,
    int? UnifiSwitchPort,
    bool AgentInstalled,
    DateTimeOffset? AgentLastSeenAt,
    bool PendingReboot,
    int UpgradablePackagesCount,
    string? AgentVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
)
{
    public static HostSnapshotDto FromEntity(Storage.Entities.Host host) => new(
        host.Id,
        host.Hostname,
        host.FriendlyName,
        host.IpAddress,
        host.OsFamily,
        host.TargetType,
        host.Proxmox?.Node,
        host.Proxmox?.Vmid,
        host.Idrac?.IpAddress,
        host.NetworkPort?.SwitchMac,
        host.NetworkPort?.PortNumber,
        host.Agent.Installed,
        host.Agent.LastSeenAt,
        host.Agent.PendingReboot,
        host.Agent.UpgradablePackagesCount,
        host.Agent.Version,
        host.CreatedAt,
        host.UpdatedAt
    );

    public Storage.Entities.Host ToEntity()
    {
        var host = new Storage.Entities.Host
        {
            Id = Id,
            Hostname = Hostname,
            FriendlyName = FriendlyName,
            IpAddress = IpAddress,
            OsFamily = OsFamily,
            TargetType = TargetType,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Agent = new AgentState
            {
                Installed = AgentInstalled,
                LastSeenAt = AgentLastSeenAt,
                PendingReboot = PendingReboot,
                UpgradablePackagesCount = UpgradablePackagesCount,
                Version = AgentVersion
            }
        };

        if (!string.IsNullOrEmpty(ProxmoxNode) && ProxmoxVmid.HasValue)
        {
            host.Proxmox = new ProxmoxTarget { Node = ProxmoxNode, Vmid = ProxmoxVmid.Value };
        }

        if (!string.IsNullOrEmpty(IdracIp))
        {
            host.Idrac = new IdracTarget { IpAddress = IdracIp };
        }

        if (!string.IsNullOrEmpty(UnifiSwitchMac) && UnifiSwitchPort.HasValue)
        {
            host.NetworkPort = new UnifiPortTarget { SwitchMac = UnifiSwitchMac, PortNumber = UnifiSwitchPort.Value };
        }

        return host;
    }
}

public record JobSnapshotDto(
    Guid Id,
    Guid TargetHostId,
    string InitiatedBy,
    string Status,
    string? ActiveStep,
    string? SnapshotIdentifier,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason
)
{
    public static JobSnapshotDto FromEntity(UpdateJob job) => new(
        job.Id,
        job.TargetHostId,
        job.InitiatedBy,
        job.Status,
        job.ActiveStep,
        job.SnapshotIdentifier,
        job.StartedAt,
        job.CompletedAt,
        job.FailureReason
    );

    public UpdateJob ToEntity() => new()
    {
        Id = Id,
        TargetHostId = TargetHostId,
        InitiatedBy = InitiatedBy,
        Status = Status,
        ActiveStep = ActiveStep,
        SnapshotIdentifier = SnapshotIdentifier,
        StartedAt = StartedAt,
        CompletedAt = CompletedAt,
        FailureReason = FailureReason
    };
}

public record StepLogSnapshotDto(
    long Id,
    Guid JobId,
    long SequenceId,
    string StreamType,
    string LogLine,
    DateTimeOffset Timestamp
)
{
    public static StepLogSnapshotDto FromEntity(StepLog log) => new(
        log.Id,
        log.JobId,
        log.SequenceId,
        log.StreamType,
        log.LogLine,
        log.Timestamp
    );

    public StepLog ToEntity() => new()
    {
        JobId = JobId,
        SequenceId = SequenceId,
        StreamType = StreamType,
        LogLine = LogLine,
        Timestamp = Timestamp
    };
}
