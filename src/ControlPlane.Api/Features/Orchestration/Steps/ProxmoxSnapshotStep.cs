using ControlPlane.Api.Features.Adapters.Proxmox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Orchestration;

/// <summary>
/// Proxmox hypervisor snapshot step.
/// Creates a safety snapshot prior to destructive package operations and handles automated rollback on failure.
/// </summary>
public class ProxmoxSnapshotStep : IJobStep
{
    private readonly IProxmoxClient? _proxmoxClient;

    public string StepName => "Hypervisor Safety Snapshot (Proxmox VE)";

    public ProxmoxSnapshotStep(IProxmoxClient? proxmoxClient = null)
    {
        _proxmoxClient = proxmoxClient;
    }

    public async Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct)
    {
        var targetType = context.TargetHost.TargetType?.ToLowerInvariant() ?? "";

        // If not a Proxmox VM or LXC, cleanly skip
        if (targetType != "proxmox_vm" && targetType != "proxmox_lxc")
        {
            await context.EmitLogAsync(
                "system",
                $"[SNAPSHOT] Host '{context.TargetHost.Hostname}' target type is '{context.TargetHost.TargetType}' (not Proxmox VM/LXC). Skipping hypervisor safety snapshot.",
                ct
            );
            return JobStepResult.Succeeded("Skipped: Host is not a virtualized Proxmox instance.");
        }

        var proxmoxTarget = context.TargetHost.Proxmox;
        if (proxmoxTarget == null || string.IsNullOrWhiteSpace(proxmoxTarget.Node) || proxmoxTarget.Vmid <= 0)
        {
            var msg = $"Target host '{context.TargetHost.Hostname}' is configured as '{context.TargetHost.TargetType}' but missing Proxmox node or VMID correlation details.";
            await context.EmitLogAsync("system", $"[SNAPSHOT] Error: {msg}", ct);
            return JobStepResult.Failed(msg);
        }

        var client = ResolveClient(context);
        if (client == null)
        {
            var msg = "Proxmox REST client is not available or registered in the service container.";
            await context.EmitLogAsync("system", $"[SNAPSHOT] Error: {msg}", ct);
            return JobStepResult.Failed(msg);
        }

        var snapName = $"cp-pre-update-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var description = $"ControlPlane pre-update safety snapshot. Expires: {DateTimeOffset.UtcNow.AddHours(24):O}";

        // Persist snapshot identifier in DB immediately
        await context.SetSnapshotIdentifierAsync(snapName, ct);

        var isLxc = targetType == "proxmox_lxc";
        var node = proxmoxTarget.Node;
        var vmid = proxmoxTarget.Vmid;

        await context.EmitLogAsync(
            "system",
            $"[SNAPSHOT] Creating pre-update hypervisor snapshot '{snapName}' on Proxmox node '{node}' for {(isLxc ? "LXC" : "VM")} {vmid}...",
            ct
        );

        try
        {
            var upid = await client.CreateVmSnapshotAsync(node, vmid, snapName, description, isLxc, ct);
            await context.EmitLogAsync("system", $"[SNAPSHOT] Snapshot task accepted (UPID: {upid}). Polling for task completion...", ct);

            var taskStatus = await client.PollTaskCompletionAsync(node, upid, ct: ct);
            if (!taskStatus.IsSuccess)
            {
                var errorMsg = $"Snapshot task failed with exit status: {taskStatus.ExitStatus ?? "unknown error"}";
                await context.EmitLogAsync("system", $"[SNAPSHOT] Error: {errorMsg}", ct);
                return JobStepResult.Failed(errorMsg);
            }

            await context.EmitLogAsync("system", $"[SNAPSHOT] Snapshot '{snapName}' created and verified successfully.", ct);
            return JobStepResult.Succeeded($"Snapshot '{snapName}' created successfully.");
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Failed to create Proxmox snapshot {SnapName} on node {Node} for {Vmid}", snapName, node, vmid);
            await context.EmitLogAsync("system", $"[SNAPSHOT] Exception creating snapshot: {ex.Message}", ct);
            return JobStepResult.Failed($"Proxmox snapshot creation failed: {ex.Message}", ex);
        }
    }

    public async Task RollbackAsync(JobExecutionContext context, CancellationToken ct)
    {
        var snapName = context.Job.SnapshotIdentifier;
        if (string.IsNullOrWhiteSpace(snapName))
        {
            await context.EmitLogAsync("system", "[ROLLBACK] No snapshot identifier recorded; skipping hypervisor rollback.", ct);
            return;
        }

        var targetType = context.TargetHost.TargetType?.ToLowerInvariant() ?? "";
        if (targetType != "proxmox_vm" && targetType != "proxmox_lxc")
        {
            return;
        }

        var proxmoxTarget = context.TargetHost.Proxmox;
        if (proxmoxTarget == null || string.IsNullOrWhiteSpace(proxmoxTarget.Node) || proxmoxTarget.Vmid <= 0)
        {
            await context.EmitLogAsync("system", "[ROLLBACK] Cannot execute rollback: Proxmox target metadata missing.", ct);
            return;
        }

        var client = ResolveClient(context);
        if (client == null)
        {
            await context.EmitLogAsync("system", "[ROLLBACK] Error: Proxmox REST client not available to perform rollback.", ct);
            return;
        }

        var isLxc = targetType == "proxmox_lxc";
        var node = proxmoxTarget.Node;
        var vmid = proxmoxTarget.Vmid;

        await context.EmitLogAsync(
            "system",
            $"[ROLLBACK] Initiating automated hypervisor rollback to snapshot {snapName}...",
            ct
        );

        try
        {
            var upid = await client.RollbackVmSnapshotAsync(node, vmid, snapName, isLxc, ct);
            await context.EmitLogAsync("system", $"[ROLLBACK] Rollback task accepted (UPID: {upid}). Polling for task completion...", ct);

            await client.PollTaskCompletionAsync(node, upid, ct: ct);

            await context.EmitLogAsync("system", $"[ROLLBACK] Automated rollback to snapshot '{snapName}' completed successfully.", ct);
            await context.UpdateJobStatusAsync(
                UpdateJobState.RolledBack,
                StepName,
                failureReason: $"Automated hypervisor rollback completed to snapshot '{snapName}'.",
                ct: ct
            );
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Failed to rollback Proxmox snapshot {SnapName} on node {Node} for {Vmid}", snapName, node, vmid);
            await context.EmitLogAsync("system", $"[ROLLBACK] Error during automated rollback: {ex.Message}", ct);
        }
    }

    private IProxmoxClient? ResolveClient(JobExecutionContext context)
    {
        if (_proxmoxClient != null)
        {
            return _proxmoxClient;
        }

        using var scope = context.ScopeFactory.CreateScope();
        return scope.ServiceProvider.GetService<IProxmoxClient>();
    }
}
