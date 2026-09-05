namespace ControlPlane.Api.Features.Adapters.Proxmox;

/// <summary>
/// Service responsible for querying, evaluating retention, and pruning Proxmox VE snapshots.
/// </summary>
public interface ISnapshotRetentionService
{
    /// <summary>
    /// Retrieves all snapshots across all Proxmox-managed hosts, or for a specific host, evaluated with retention status.
    /// </summary>
    Task<List<HostSnapshotItemDto>> GetSnapshotsAsync(Guid? hostId = null, CancellationToken ct = default);

    /// <summary>
    /// Evaluates and prunes expired ControlPlane pre-update snapshots across all hosts or a specific host.
    /// </summary>
    Task<SnapshotPruneResultDto> PruneExpiredSnapshotsAsync(Guid? hostId = null, bool dryRun = false, CancellationToken ct = default);

    /// <summary>
    /// Immediately deletes a specific snapshot for a given host.
    /// </summary>
    Task<bool> DeleteSnapshotAsync(Guid hostId, string snapshotName, CancellationToken ct = default);
}
