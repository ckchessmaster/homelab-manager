using ControlPlane.Api.Features.Adapters.Kubernetes;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Api.Features.Orchestration;

/// <summary>
/// Restores scheduling to the Kubernetes node (spec.unschedulable = false) after post-flight health verification succeeds.
/// </summary>
public class KubernetesUncordonStep : IJobStep
{
    private readonly IKubernetesAdapter? _adapter;

    public string StepName => "Kubernetes Node Uncordon";

    public KubernetesUncordonStep(IKubernetesAdapter? adapter = null)
    {
        _adapter = adapter;
    }

    public async Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct)
    {
        if (!context.State.TryGetValue("K8sNodeName", out var n) || n == null)
        {
            await context.EmitLogAsync("system", "[K8S] Host was not cordoned or is not a Kubernetes node; skipping uncordon step.", ct);
            return JobStepResult.Succeeded("Skipped: Host is not a cordoned Kubernetes node.");
        }

        var nodeName = n.ToString()!;
        using var scope = _adapter == null && context.ScopeFactory != null
            ? context.ScopeFactory.CreateScope()
            : null;

        var adapter = _adapter ?? scope?.ServiceProvider.GetService<IKubernetesAdapter>();
        if (adapter == null)
        {
            await context.EmitLogAsync("system", "[K8S] Kubernetes adapter unavailable; skipping uncordon step.", ct);
            return JobStepResult.Succeeded("Skipped: Kubernetes adapter unavailable.");
        }

        await context.EmitLogAsync("system", $"[K8S] Uncordoning node '{nodeName}' (restoring scheduling)...", ct);
        var uncordoned = await adapter.UncordonNodeAsync(nodeName, ct);

        if (!uncordoned)
        {
            var msg = $"Failed to uncordon node '{nodeName}'.";
            await context.EmitLogAsync("system", $"[K8S] Warning: {msg}", ct);
            return JobStepResult.Failed(msg);
        }

        await context.EmitLogAsync("system", $"[K8S] Node '{nodeName}' uncordoned successfully (unschedulable = false). Scheduling restored.", ct);
        return JobStepResult.Succeeded($"Node '{nodeName}' uncordoned successfully.");
    }

    public Task RollbackAsync(JobExecutionContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
