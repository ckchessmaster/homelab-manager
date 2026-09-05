using ControlPlane.Api.Features.Adapters.Kubernetes;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Api.Features.Orchestration;

/// <summary>
/// Evicts non-DaemonSet pods from the cordoned node respecting PodDisruptionBudgets prior to reboot.
/// </summary>
public class KubernetesDrainStep : IJobStep
{
    private readonly IKubernetesAdapter? _adapter;
    private readonly TimeSpan _drainTimeout;
    private readonly bool _ignoreDaemonSets;

    public string StepName => "Kubernetes Workload Eviction (Drain)";

    public KubernetesDrainStep(IKubernetesAdapter? adapter = null, TimeSpan? drainTimeout = null, bool ignoreDaemonSets = true)
    {
        _adapter = adapter;
        _drainTimeout = drainTimeout ?? TimeSpan.FromSeconds(180);
        _ignoreDaemonSets = ignoreDaemonSets;
    }

    public async Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct)
    {
        var isK8sNode = context.State.TryGetValue("IsKubernetesNode", out var isK8s) && isK8s is true;
        if (!isK8sNode && !context.State.ContainsKey("K8sNodeName"))
        {
            await context.EmitLogAsync("system", "[K8S] Host is not marked as a Kubernetes node; skipping drain step.", ct);
            return JobStepResult.Succeeded("Skipped: Host is not a Kubernetes node.");
        }

        using var scope = _adapter == null && context.ScopeFactory != null
            ? context.ScopeFactory.CreateScope()
            : null;

        var adapter = _adapter ?? scope?.ServiceProvider.GetService<IKubernetesAdapter>();
        if (adapter == null)
        {
            await context.EmitLogAsync("system", "[K8S] Kubernetes adapter unavailable; skipping drain step.", ct);
            return JobStepResult.Succeeded("Skipped: Kubernetes adapter unavailable.");
        }

        var nodeName = context.State.TryGetValue("K8sNodeName", out var n)
            ? n?.ToString() ?? context.TargetHost.Hostname
            : context.TargetHost.Hostname;

        await context.EmitLogAsync("system", $"[K8S] Draining workloads from node '{nodeName}' (timeout: {_drainTimeout.TotalSeconds}s)...", ct);

        var result = await adapter.DrainNodeAsync(nodeName, _drainTimeout, _ignoreDaemonSets, deleteEmptyDirData: true, ct);

        if (!result.Success)
        {
            var msg = result.ErrorMessage ?? $"Failed to drain workloads from node '{nodeName}'.";
            await context.EmitLogAsync("system", $"[K8S] Error: {msg}", ct);
            return JobStepResult.Failed(msg);
        }

        await context.EmitLogAsync("system", $"[K8S] Node '{nodeName}' drained successfully. Evicted {result.EvictedPodCount} pods.", ct);
        return JobStepResult.Succeeded($"Drained node '{nodeName}' ({result.EvictedPodCount} pods evicted).");
    }

    public Task RollbackAsync(JobExecutionContext context, CancellationToken ct)
    {
        // Drain cannot re-schedule evicted pods back to the node; scheduling restoration is handled by CordonStep rollback
        return Task.CompletedTask;
    }
}
