using System.Globalization;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Features.Adapters.Proxmox;

public class SnapshotRetentionService : ISnapshotRetentionService
{
    private readonly ControlPlaneDbContext _db;
    private readonly IProxmoxClient _proxmoxClient;
    private readonly SnapshotRetentionOptions _options;
    private readonly ILogger<SnapshotRetentionService> _logger;

    public SnapshotRetentionService(
        ControlPlaneDbContext db,
        IProxmoxClient proxmoxClient,
        IOptions<SnapshotRetentionOptions> options,
        ILogger<SnapshotRetentionService> logger)
    {
        _db = db;
        _proxmoxClient = proxmoxClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<HostSnapshotItemDto>> GetSnapshotsAsync(Guid? hostId = null, CancellationToken ct = default)
    {
        var hostsQuery = _db.Hosts.AsNoTracking()
            .Where(h => h.Proxmox != null && h.Proxmox.Vmid > 0 && !string.IsNullOrWhiteSpace(h.Proxmox.Node));

        if (hostId.HasValue)
        {
            hostsQuery = hostsQuery.Where(h => h.Id == hostId.Value);
        }

        var hosts = await hostsQuery.ToListAsync(ct);
        var activeJobSnapshots = await GetActiveJobProtectedSnapshotsAsync(ct);

        var results = new List<HostSnapshotItemDto>();

        foreach (var host in hosts)
        {
            var isLxc = string.Equals(host.TargetType, "proxmox_lxc", StringComparison.OrdinalIgnoreCase);
            var node = host.Proxmox!.Node;
            var vmid = host.Proxmox.Vmid;

            try
            {
                var snapshots = await _proxmoxClient.ListVmSnapshotsAsync(node, vmid, isLxc, ct);

                foreach (var snap in snapshots)
                {
                    if (string.Equals(snap.Name, "current", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var isCpSnapshot = snap.Name.StartsWith(_options.SnapshotPrefix, StringComparison.OrdinalIgnoreCase);
                    var createdAt = ParseSnapshotCreatedAt(snap);
                    var ageHours = createdAt.HasValue
                        ? Math.Max(0, (DateTimeOffset.UtcNow - createdAt.Value).TotalHours)
                        : 0;

                    var isProtected = activeJobSnapshots.Contains(snap.Name);
                    var isExpired = isCpSnapshot && ageHours >= _options.RetentionHours;
                    var canPrune = isExpired && !isProtected && isCpSnapshot;

                    results.Add(new HostSnapshotItemDto(
                        HostId: host.Id,
                        Hostname: host.Hostname,
                        Node: node,
                        Vmid: vmid,
                        IsLxc: isLxc,
                        Name: snap.Name,
                        Description: snap.Description,
                        CreatedAt: createdAt,
                        AgeHours: Math.Round(ageHours, 1),
                        IsControlPlaneSnapshot: isCpSnapshot,
                        IsProtectedByActiveJob: isProtected,
                        IsExpired: isExpired,
                        CanPrune: canPrune
                    ));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list snapshots for host '{Hostname}' ({Node}:{Vmid})", host.Hostname, node, vmid);
            }
        }

        return results;
    }

    public async Task<SnapshotPruneResultDto> PruneExpiredSnapshotsAsync(
        Guid? hostId = null,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Starting snapshot retention sweep (HostId: {HostId}, DryRun: {DryRun}, RetentionHours: {RetentionHours})",
            hostId, dryRun, _options.RetentionHours);

        var snapshots = await GetSnapshotsAsync(hostId, ct);
        var expiredCandidates = snapshots.Where(s => s.CanPrune).ToList();

        var items = new List<PrunedSnapshotItemDto>();
        var errors = new List<string>();
        int prunedCount = 0;
        int skippedCount = snapshots.Count - expiredCandidates.Count;

        foreach (var snap in expiredCandidates)
        {
            if (dryRun)
            {
                items.Add(new PrunedSnapshotItemDto(
                    HostId: snap.HostId,
                    Hostname: snap.Hostname,
                    SnapshotName: snap.Name,
                    AgeHours: snap.AgeHours,
                    Success: true,
                    Message: $"[DRY-RUN] Eligible for pruning (Age: {snap.AgeHours:F1}h >= {_options.RetentionHours}h)."
                ));
                prunedCount++;
                continue;
            }

            try
            {
                _logger.LogInformation("Pruning expired snapshot '{SnapshotName}' on host '{Hostname}' ({Node}:{Vmid}, Age: {AgeHours:F1}h)...",
                    snap.Name, snap.Hostname, snap.Node, snap.Vmid, snap.AgeHours);

                var upid = await _proxmoxClient.DeleteVmSnapshotAsync(
                    snap.Node,
                    snap.Vmid,
                    snap.Name,
                    snap.IsLxc,
                    ct
                );

                var taskStatus = await _proxmoxClient.PollTaskCompletionAsync(snap.Node, upid, ct: ct);
                if (!taskStatus.IsSuccess)
                {
                    var msg = $"Delete task failed with exit status: {taskStatus.ExitStatus ?? "unknown error"}";
                    _logger.LogError("Failed to prune snapshot '{SnapshotName}' on host '{Hostname}': {Error}", snap.Name, snap.Hostname, msg);
                    errors.Add($"[{snap.Hostname}] {snap.Name}: {msg}");
                    items.Add(new PrunedSnapshotItemDto(snap.HostId, snap.Hostname, snap.Name, snap.AgeHours, Success: false, Message: msg));
                }
                else
                {
                    _logger.LogInformation("Successfully pruned expired snapshot '{SnapshotName}' on host '{Hostname}'.", snap.Name, snap.Hostname);
                    items.Add(new PrunedSnapshotItemDto(snap.HostId, snap.Hostname, snap.Name, snap.AgeHours, Success: true, Message: "Successfully pruned."));
                    prunedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception deleting snapshot '{SnapshotName}' on host '{Hostname}'", snap.Name, snap.Hostname);
                errors.Add($"[{snap.Hostname}] {snap.Name}: {ex.Message}");
                items.Add(new PrunedSnapshotItemDto(snap.HostId, snap.Hostname, snap.Name, snap.AgeHours, Success: false, Message: ex.Message));
            }
        }

        return new SnapshotPruneResultDto(
            TotalScanned: snapshots.Count,
            ExpiredCount: expiredCandidates.Count,
            PrunedCount: prunedCount,
            SkippedCount: skippedCount,
            DryRun: dryRun,
            Items: items,
            Errors: errors
        );
    }

    public async Task<bool> DeleteSnapshotAsync(Guid hostId, string snapshotName, CancellationToken ct = default)
    {
        var host = await _db.Hosts.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == hostId, ct);

        if (host?.Proxmox == null || string.IsNullOrWhiteSpace(host.Proxmox.Node) || host.Proxmox.Vmid <= 0)
        {
            throw new InvalidOperationException($"Host with ID '{hostId}' is not configured with Proxmox metadata.");
        }

        var isLxc = string.Equals(host.TargetType, "proxmox_lxc", StringComparison.OrdinalIgnoreCase);
        var node = host.Proxmox.Node;
        var vmid = host.Proxmox.Vmid;

        _logger.LogInformation("Manually deleting snapshot '{SnapshotName}' on host '{Hostname}' ({Node}:{Vmid})...",
            snapshotName, host.Hostname, node, vmid);

        var upid = await _proxmoxClient.DeleteVmSnapshotAsync(node, vmid, snapshotName, isLxc, ct);
        var taskStatus = await _proxmoxClient.PollTaskCompletionAsync(node, upid, ct: ct);

        if (!taskStatus.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to delete snapshot '{snapshotName}': {taskStatus.ExitStatus ?? "unknown error"}");
        }

        return true;
    }

    private async Task<HashSet<string>> GetActiveJobProtectedSnapshotsAsync(CancellationToken ct)
    {
        var activeSnapshots = await _db.UpdateJobs.AsNoTracking()
            .Where(j => (j.Status == "Running" || j.Status == "Verifying") && !string.IsNullOrWhiteSpace(j.SnapshotIdentifier))
            .Select(j => j.SnapshotIdentifier!)
            .ToListAsync(ct);

        return activeSnapshots.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private DateTimeOffset? ParseSnapshotCreatedAt(ProxmoxSnapshotItem snap)
    {
        // 1. Prefer Proxmox reported snaptime (unix epoch seconds)
        if (snap.SnapTime.HasValue && snap.SnapTime.Value > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(snap.SnapTime.Value);
        }

        // 2. Parse from ControlPlane snapshot naming format: cp-pre-update-yyyyMMddHHmmss
        if (snap.Name.StartsWith(_options.SnapshotPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var timePart = snap.Name[_options.SnapshotPrefix.Length..];
            if (timePart.Length >= 14 &&
                DateTime.TryParseExact(timePart[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDt))
            {
                return new DateTimeOffset(parsedDt, TimeSpan.Zero);
            }
        }

        return null;
    }
}
