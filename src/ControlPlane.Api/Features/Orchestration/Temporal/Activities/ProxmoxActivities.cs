using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Temporalio.Activities;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities;

public class ProxmoxActivities : IProxmoxActivities
{
    private readonly IProxmoxClient? _proxmoxClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWorkflowLogEmitter _logEmitter;
    private readonly ILogger<ProxmoxActivities> _logger;

    public ProxmoxActivities(
        IServiceScopeFactory scopeFactory,
        IWorkflowLogEmitter logEmitter,
        ILogger<ProxmoxActivities> logger,
        IProxmoxClient? proxmoxClient = null)
    {
        _scopeFactory = scopeFactory;
        _logEmitter = logEmitter;
        _logger = logger;
        _proxmoxClient = proxmoxClient;
    }

    [Activity]
    public async Task<ProxmoxSnapshotResult> CreateSnapshotAsync(ProxmoxSnapshotInput input)
    {
        var targetType = input.TargetType?.ToLowerInvariant() ?? "";

        // If not a Proxmox VM or LXC, cleanly skip
        if (targetType != "proxmox_vm" && targetType != "proxmox_lxc")
        {
            await _logEmitter.EmitLogAsync(
                input.JobId,
                "system",
                $"[SNAPSHOT] Host '{input.Hostname}' target type is '{input.TargetType}' (not Proxmox VM/LXC). Skipping hypervisor safety snapshot."
            );
            return new ProxmoxSnapshotResult(true, false, null, "Skipped: Host is not a virtualized Proxmox instance.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var host = await db.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == input.HostId);

        var proxmoxTarget = host?.Proxmox;
        if (proxmoxTarget == null || string.IsNullOrWhiteSpace(proxmoxTarget.Node) || proxmoxTarget.Vmid <= 0)
        {
            var msg = $"Target host '{input.Hostname}' is configured as '{input.TargetType}' but missing Proxmox node or VMID correlation details.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[SNAPSHOT] Error: {msg}");
            return new ProxmoxSnapshotResult(false, false, null, msg);
        }

        var client = _proxmoxClient ?? scope.ServiceProvider.GetService<IProxmoxClient>();
        if (client == null)
        {
            var msg = "Proxmox REST client is not available or registered in the service container.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[SNAPSHOT] Error: {msg}");
            return new ProxmoxSnapshotResult(false, false, null, msg);
        }

        var isLxc = targetType == "proxmox_lxc";
        var node = proxmoxTarget.Node;
        var vmid = proxmoxTarget.Vmid;
        var snapName = input.SnapshotName ?? $"cp-pre-update-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var description = $"ControlPlane pre-update safety snapshot. Expires: {DateTimeOffset.UtcNow.AddHours(24):O}";

        try
        {
            // Check if VM storage backend supports snapshots
            bool hasSnapshotSupport = true;
            try
            {
                hasSnapshotSupport = await client.HasSnapshotFeatureAsync(node, vmid, isLxc);
            }
            catch (Exception featEx)
            {
                _logger.LogWarning(featEx, "Failed querying snapshot feature for VM {Vmid} on node {Node}: {Message}", vmid, node, featEx.Message);
                if (featEx.Message.Contains("snapshot feature is not available", StringComparison.OrdinalIgnoreCase) ||
                    featEx.Message.Contains("does not support snapshot", StringComparison.OrdinalIgnoreCase) ||
                    featEx.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
                    featEx.Message.Contains("storage does not support snapshots", StringComparison.OrdinalIgnoreCase))
                {
                    hasSnapshotSupport = false;
                }
                else
                {
                    throw;
                }
            }

            if (!hasSnapshotSupport)
            {
                await _logEmitter.EmitLogAsync(
                    input.JobId,
                    "system",
                    $"[SNAPSHOT] Notice: {(isLxc ? "LXC" : "VM")} {vmid} on Proxmox node '{node}' is backed by storage that does not support snapshots ('snapshot feature is not available'). Skipping safety snapshot."
                );
                return new ProxmoxSnapshotResult(true, false, null, "Skipped: Proxmox storage does not support snapshots.");
            }

            await _logEmitter.EmitLogAsync(
                input.JobId,
                "system",
                $"[SNAPSHOT] Creating pre-update hypervisor snapshot '{snapName}' on Proxmox node '{node}' for {(isLxc ? "LXC" : "VM")} {vmid}..."
            );

            var upid = await client.CreateVmSnapshotAsync(node, vmid, snapName, description, isLxc);
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[SNAPSHOT] Snapshot task accepted (UPID: {upid}). Polling for task completion...");

            var taskStatus = await client.PollTaskCompletionAsync(node, upid);
            if (!taskStatus.IsSuccess)
            {
                if (taskStatus.ExitStatus != null &&
                    (taskStatus.ExitStatus.Contains("snapshot feature is not available", StringComparison.OrdinalIgnoreCase) ||
                     taskStatus.ExitStatus.Contains("does not support snapshot", StringComparison.OrdinalIgnoreCase) ||
                     taskStatus.ExitStatus.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
                     taskStatus.ExitStatus.Contains("storage does not support snapshots", StringComparison.OrdinalIgnoreCase)))
                {
                    await _logEmitter.EmitLogAsync(
                        input.JobId,
                        "system",
                        $"[SNAPSHOT] Notice: Proxmox reported 'snapshot feature is not available' for VM {vmid}. Skipping safety snapshot."
                    );
                    return new ProxmoxSnapshotResult(true, false, null, "Skipped: Proxmox storage does not support snapshots.");
                }

                var errorMsg = $"Snapshot task failed with exit status: {taskStatus.ExitStatus ?? "unknown error"}";
                await _logEmitter.EmitLogAsync(input.JobId, "system", $"[SNAPSHOT] Error: {errorMsg}");
                return new ProxmoxSnapshotResult(false, false, null, errorMsg);
            }

            await _logEmitter.SetSnapshotIdentifierAsync(input.JobId, snapName);
            var successMsg = $"Snapshot '{snapName}' created and verified successfully.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[SNAPSHOT] {successMsg}");
            return new ProxmoxSnapshotResult(true, true, snapName, successMsg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Proxmox snapshot {SnapName} on node {Node} for {Vmid}", snapName, node, vmid);

            if (ex.Message.Contains("snapshot feature is not available", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("does not support snapshot", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("storage does not support snapshots", StringComparison.OrdinalIgnoreCase))
            {
                await _logEmitter.EmitLogAsync(
                    input.JobId,
                    "system",
                    $"[SNAPSHOT] Notice: Proxmox reported snapshots not supported for {(isLxc ? "LXC" : "VM")} {vmid} ({ex.Message}). Skipping safety snapshot."
                );
                return new ProxmoxSnapshotResult(true, false, null, "Skipped: Proxmox storage does not support snapshots.");
            }

            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[SNAPSHOT] Exception creating snapshot: {ex.Message}");
            return new ProxmoxSnapshotResult(false, false, null, $"Proxmox snapshot creation failed: {ex.Message}");
        }
    }

    [Activity]
    public async Task<ProxmoxRollbackResult> RollbackSnapshotAsync(ProxmoxRollbackInput input)
    {
        var snapName = input.SnapshotName;
        if (string.IsNullOrWhiteSpace(snapName))
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", "[ROLLBACK] No snapshot identifier recorded; skipping hypervisor rollback.");
            return new ProxmoxRollbackResult(true, "Skipped: No snapshot recorded.");
        }

        var targetType = input.TargetType?.ToLowerInvariant() ?? "";
        if (targetType != "proxmox_vm" && targetType != "proxmox_lxc")
        {
            return new ProxmoxRollbackResult(true, "Skipped: Host is not Proxmox VM/LXC.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
        var host = await db.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == input.HostId);

        var proxmoxTarget = host?.Proxmox;
        if (proxmoxTarget == null || string.IsNullOrWhiteSpace(proxmoxTarget.Node) || proxmoxTarget.Vmid <= 0)
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", "[ROLLBACK] Cannot execute rollback: Proxmox target metadata missing.");
            return new ProxmoxRollbackResult(false, "Proxmox target metadata missing.");
        }

        var client = _proxmoxClient ?? scope.ServiceProvider.GetService<IProxmoxClient>();
        if (client == null)
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", "[ROLLBACK] Error: Proxmox REST client not available to perform rollback.");
            return new ProxmoxRollbackResult(false, "Proxmox REST client not available.");
        }

        var isLxc = targetType == "proxmox_lxc";
        var node = proxmoxTarget.Node;
        var vmid = proxmoxTarget.Vmid;

        await _logEmitter.EmitLogAsync(
            input.JobId,
            "system",
            $"[ROLLBACK] Initiating automated hypervisor rollback to snapshot '{snapName}' on node '{node}' for {(isLxc ? "LXC" : "VM")} {vmid}..."
        );

        try
        {
            var upid = await client.RollbackVmSnapshotAsync(node, vmid, snapName, isLxc);
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[ROLLBACK] Rollback task accepted (UPID: {upid}). Polling for task completion...");

            await client.PollTaskCompletionAsync(node, upid);

            var successMsg = $"Automated rollback to snapshot '{snapName}' completed successfully.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[ROLLBACK] {successMsg}");
            await _logEmitter.UpdateJobStatusAsync(input.JobId, "RolledBack", "Hypervisor Snapshot Rollback", failureReason: successMsg);

            return new ProxmoxRollbackResult(true, successMsg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback Proxmox snapshot {SnapName} on node {Node} for {Vmid}", snapName, node, vmid);
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[ROLLBACK] Error during automated rollback: {ex.Message}");
            return new ProxmoxRollbackResult(false, $"Automated rollback failed: {ex.Message}");
        }
    }
}
