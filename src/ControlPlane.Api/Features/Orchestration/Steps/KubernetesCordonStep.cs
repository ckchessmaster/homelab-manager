using ControlPlane.Api.Features.Adapters.Kubernetes;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Api.Features.Orchestration;

/// <summary>
/// Marks a Kubernetes node unschedulable (cordoned) before maintenance begins.
/// </summary>
public class KubernetesCordonStep : IJobStep
{
    private readonly IKubernetesAdapter? _adapter;
    private readonly string? _targetNodeName;

    public string StepName => "Kubernetes Node Cordon";

    public KubernetesCordonStep(IKubernetesAdapter? adapter = null, string? targetNodeName = null)
    {
        _adapter = adapter;
        _targetNodeName = targetNodeName;
    }

    public async Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct)
    {
        using var scope = _adapter == null && context.ScopeFactory != null
            ? context.ScopeFactory.CreateScope()
            : null;

        var adapter = _adapter ?? scope?.ServiceProvider.GetService<IKubernetesAdapter>();
        if (adapter == null)
        {
            await context.EmitLogAsync("system", "[K8S] Kubernetes adapter is not configured; skipping cordon step.", ct);
            return JobStepResult.Succeeded("Skipped: Kubernetes adapter unavailable.");
        }

        var nodeName = _targetNodeName
            ?? (context.State.TryGetValue("K8sNodeName", out var n) ? n?.ToString() : null)
            ?? context.TargetHost.Hostname;

        if (string.IsNullOrWhiteSpace(nodeName))
        {
            await context.EmitLogAsync("system", "[K8S] No Kubernetes node name specified or resolvable; skipping cordon step.", ct);
            return JobStepResult.Succeeded("Skipped: No Kubernetes node name found.");
        }

        // Check if node exists in cluster
        var status = await adapter.GetNodeStatusAsync(nodeName, ct);
        if (status == null)
        {
            await context.EmitLogAsync("system", $"[K8S] Node '{nodeName}' does not exist in Kubernetes cluster; skipping cordon step.", ct);
            return JobStepResult.Succeeded($"Skipped: Node '{nodeName}' not registered in Kubernetes.");
        }

        context.State["K8sNodeName"] = nodeName;
        context.State["IsKubernetesNode"] = true;

        await context.EmitLogAsync("system", $"[K8S] Cordoning Kubernetes node '{nodeName}'...", ct);
        var cordoned = await adapter.CordonNodeAsync(nodeName, ct);

        if (!cordoned)
        {
            var errorMsg = $"Failed to cordon Kubernetes node '{nodeName}'.";
            await context.EmitLogAsync("system", $"[K8S] Error: {errorMsg}", ct);
            return JobStepResult.Failed(errorMsg);
        }

        await context.EmitLogAsync("system", $"[K8S] Node '{nodeName}' cordoned successfully (unschedulable = true).", ct);
        return JobStepResult.Succeeded($"Node '{nodeName}' cordoned successfully.");
    }

    public async Task RollbackAsync(JobExecutionContext context, CancellationToken ct)
    {
        if (context.State.TryGetValue("K8sNodeName", out var n) && n != null)
        {
            var nodeName = n.ToString()!;
            using var scope = _adapter == null && context.ScopeFactory != null
                ? context.ScopeFactory.CreateScope()
                : null;

            var adapter = _adapter ?? scope?.ServiceProvider.GetService<IKubernetesAdapter>();
            if (adapter != null)
            {
                await context.EmitLogAsync("system", $"[K8S] Rollback: uncordoning node '{nodeName}'...", ct);
                await adapter.UncordonNodeAsync(nodeName, ct);
            }
        }
    }
}
