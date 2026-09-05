namespace ControlPlane.Api.Features.Adapters.Proxmox;

/// <summary>
/// Proxmox VE REST client interface for hypervisor operations, snapshots, rollbacks, and task polling.
/// </summary>
public interface IProxmoxClient
{
    /// <summary>
    /// Triggers creation of a snapshot for a VM (QEMU) or LXC container. Returns the Proxmox task UPID.
    /// </summary>
    Task<string> CreateVmSnapshotAsync(
        string node,
        int vmid,
        string snapName,
        string? description = null,
        bool isLxc = false,
        CancellationToken ct = default);

    /// <summary>
    /// Triggers a rollback to a specified snapshot for a VM (QEMU) or LXC container. Returns the Proxmox task UPID.
    /// </summary>
    Task<string> RollbackVmSnapshotAsync(
        string node,
        int vmid,
        string snapName,
        bool isLxc = false,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a snapshot for a VM (QEMU) or LXC container. Returns the Proxmox task UPID.
    /// </summary>
    Task<string> DeleteVmSnapshotAsync(
        string node,
        int vmid,
        string snapName,
        bool isLxc = false,
        CancellationToken ct = default);

    /// <summary>
    /// Queries the current status of an asynchronous Proxmox task by its UPID.
    /// </summary>
    Task<ProxmoxTaskStatus> GetTaskStatusAsync(
        string node,
        string upid,
        CancellationToken ct = default);

    /// <summary>
    /// Polls a Proxmox task until it transitions to 'stopped'.
    /// Throws TimeoutException if timeout is exceeded, or InvalidOperationException if task exitstatus is not OK.
    /// </summary>
    Task<ProxmoxTaskStatus> PollTaskCompletionAsync(
        string node,
        string upid,
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    /// <summary>
    /// Discovers all VM and LXC resources across the Proxmox cluster.
    /// </summary>
    Task<List<ProxmoxClusterResourceDto>> DiscoverClusterResourcesAsync(CancellationToken ct = default);

    /// <summary>
    /// Attempts to query guest agent network interfaces or container IP address.
    /// </summary>
    Task<string?> TryGetGuestIpAddressAsync(
        string node,
        int vmid,
        bool isLxc = false,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all snapshots currently existing for a VM (QEMU) or LXC container.
    /// </summary>
    Task<List<ProxmoxSnapshotItem>> ListVmSnapshotsAsync(
        string node,
        int vmid,
        bool isLxc = false,
        CancellationToken ct = default);

    /// <summary>
    /// Enumerates compute nodes in the Proxmox cluster or standalone host.
    /// </summary>
    Task<List<ProxmoxNodeDto>> ListNodesAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks whether the active API token has VM.Audit or broader permissions to inspect VMs/LXCs.
    /// </summary>
    Task<bool> HasVmAuditPermissionAsync(CancellationToken ct = default);
}

