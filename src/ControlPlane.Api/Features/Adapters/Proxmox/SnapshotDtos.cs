namespace ControlPlane.Api.Features.Adapters.Proxmox;

/// <summary>
/// Detailed metadata for a snapshot located on a Proxmox VM/LXC.
/// </summary>
public record HostSnapshotItemDto(
    Guid HostId,
    string Hostname,
    string Node,
    int Vmid,
    bool IsLxc,
    string Name,
    string? Description,
    DateTimeOffset? CreatedAt,
    double AgeHours,
    bool IsControlPlaneSnapshot,
    bool IsProtectedByActiveJob,
    bool IsExpired,
    bool CanPrune
);

/// <summary>
/// Request payload for snapshot pruning sweep.
/// </summary>
public record SnapshotPruneRequest(
    Guid? HostId = null,
    bool DryRun = false
);

/// <summary>
/// Record of an evaluated or pruned snapshot.
/// </summary>
public record PrunedSnapshotItemDto(
    Guid HostId,
    string Hostname,
    string SnapshotName,
    double AgeHours,
    bool Success,
    string? Message = null
);

/// <summary>
/// Summary result of a snapshot pruning sweep.
/// </summary>
public record SnapshotPruneResultDto(
    int TotalScanned,
    int ExpiredCount,
    int PrunedCount,
    int SkippedCount,
    bool DryRun,
    List<PrunedSnapshotItemDto> Items,
    List<string> Errors
);
