using ControlPlane.Api.Features.Adapters.Proxmox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Orchestration;

/// <summary>
/// Proxmox hypervisor rollback step.
/// Can be invoked explicitly in an orchestration workflow to roll back a VM or LXC to its recorded pre-update snapshot.
/// </summary>
public class ProxmoxRollbackStep : IJobStep
{
    private readonly IProxmoxClient? _proxmoxClient;

    public string StepName => "Hypervisor Automated Rollback (Proxmox VE)";

    public ProxmoxRollbackStep(IProxmoxClient? proxmoxClient = null)
    {
        _proxmoxClient = proxmoxClient;
    }

    public async Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct)
    {
        var snapName = context.Job.SnapshotIdentifier;
        if (string.IsNullOrWhiteSpace(snapName))
        {
            await context.EmitLogAsync("system", "[ROLLBACK] No snapshot identifier recorded on job; skipping hypervisor rollback.", ct);
            return JobStepResult.Succeeded("No snapshot identifier to rollback.");
        }

        var targetType = context.TargetHost.TargetType?.ToLowerInvariant() ?? "";
        if (targetType != "proxmox_vm" && targetType != "proxmox_lxc")
        {
            await context.EmitLogAsync("system", $"[ROLLBACK] Host target type '{context.TargetHost.TargetType}' is not virtualized; skipping rollback.", ct);
            return JobStepResult.Succeeded("Skipped: Host is not a virtualized Proxmox instance.");
        }

        var proxmoxTarget = context.TargetHost.Proxmox;
        if (proxmoxTarget == null || string.IsNullOrWhiteSpace(proxmoxTarget.Node) || proxmoxTarget.Vmid <= 0)
        {
            var msg = "Target host Proxmox metadata (node, vmid) is missing or invalid.";
            await context.EmitLogAsync("system", $"[ROLLBACK] Error: {msg}", ct);
            return JobStepResult.Failed(msg);
        }

        var client = ResolveClient(context);
        if (client == null)
        {
            var msg = "Proxmox REST client is not available or registered.";
            await context.EmitLogAsync("system", $"[ROLLBACK] Error: {msg}", ct);
            return JobStepResult.Failed(msg);
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

            var taskStatus = await client.PollTaskCompletionAsync(node, upid, ct: ct);
            if (!taskStatus.IsSuccess)
            {
                var errorMsg = $"Rollback task failed with exit status: {taskStatus.ExitStatus ?? "unknown error"}";
                await context.EmitLogAsync("system", $"[ROLLBACK] Error: {errorMsg}", ct);
                return JobStepResult.Failed(errorMsg);
            }

            await context.EmitLogAsync("system", $"[ROLLBACK] Automated rollback to snapshot '{snapName}' completed successfully.", ct);
            await context.UpdateJobStatusAsync(
                UpdateJobState.RolledBack,
                StepName,
                failureReason: $"Automated hypervisor rollback completed to snapshot '{snapName}'.",
                ct: ct
            );

            return JobStepResult.Succeeded($"Rolled back successfully to snapshot '{snapName}'.", targetState: UpdateJobState.RolledBack);
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "Exception during Proxmox rollback to snapshot {SnapName} on node {Node} for {Vmid}", snapName, node, vmid);
            await context.EmitLogAsync("system", $"[ROLLBACK] Exception during automated rollback: {ex.Message}", ct);
            return JobStepResult.Failed($"Proxmox rollback failed: {ex.Message}", ex);
        }
    }

    public Task RollbackAsync(JobExecutionContext context, CancellationToken ct)
    {
        // Rollback step itself does not have a further rollback
        return Task.CompletedTask;
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
