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
}
