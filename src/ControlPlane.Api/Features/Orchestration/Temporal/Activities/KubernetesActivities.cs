using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using ControlPlane.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Temporalio.Activities;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities;

public class KubernetesActivities : IKubernetesActivities
{
    private readonly IKubernetesAdapter? _adapter;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWorkflowLogEmitter _logEmitter;
    private readonly ILogger<KubernetesActivities> _logger;

    public KubernetesActivities(
        IServiceScopeFactory scopeFactory,
        IWorkflowLogEmitter logEmitter,
        ILogger<KubernetesActivities> logger,
        IKubernetesAdapter? adapter = null)
    {
        _scopeFactory = scopeFactory;
        _logEmitter = logEmitter;
        _logger = logger;
        _adapter = adapter;
    }

    [Activity]
    public async Task<KubernetesCordonResult> CordonNodeAsync(KubernetesNodeInput input)
    {
        using var scope = _scopeFactory.CreateScope();
        var adapter = _adapter ?? scope.ServiceProvider.GetService<IKubernetesAdapter>();
        if (adapter == null)
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", "[K8S] Kubernetes adapter is not configured; skipping cordon step.");
            return new KubernetesCordonResult(true, false, null, "Skipped: Kubernetes adapter unavailable.");
        }

        var nodeName = input.NodeName;
        if (string.IsNullOrWhiteSpace(nodeName))
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var host = await db.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == input.HostId);
            nodeName = host?.Hostname;
        }

        if (string.IsNullOrWhiteSpace(nodeName))
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", "[K8S] No Kubernetes node name specified or resolvable; skipping cordon step.");
            return new KubernetesCordonResult(true, false, null, "Skipped: No Kubernetes node name found.");
        }

        var status = await adapter.GetNodeStatusAsync(nodeName);
        if (status == null)
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[K8S] Node '{nodeName}' does not exist in Kubernetes cluster; skipping cordon step.");
            return new KubernetesCordonResult(true, false, nodeName, $"Skipped: Node '{nodeName}' not registered in Kubernetes.");
        }

        await _logEmitter.EmitLogAsync(input.JobId, "system", $"[K8S] Cordoning Kubernetes node '{nodeName}'...");
        var cordoned = await adapter.CordonNodeAsync(nodeName);

        if (!cordoned)
        {
            var errorMsg = $"Failed to cordon Kubernetes node '{nodeName}'.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[K8S] Error: {errorMsg}");
            return new KubernetesCordonResult(false, false, nodeName, errorMsg);
        }

        var successMsg = $"Node '{nodeName}' cordoned successfully (unschedulable = true).";
        await _logEmitter.EmitLogAsync(input.JobId, "system", $"[K8S] {successMsg}");
        return new KubernetesCordonResult(true, true, nodeName, successMsg);
    }

    [Activity]
    public async Task<KubernetesDrainResult> DrainNodeAsync(KubernetesDrainInput input)
    {
        using var scope = _scopeFactory.CreateScope();
        var adapter = _adapter ?? scope.ServiceProvider.GetService<IKubernetesAdapter>();
        if (adapter == null)
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", "[K8S] Kubernetes adapter unavailable; skipping drain step.");
            return new KubernetesDrainResult(true, false, 0, "Skipped: Kubernetes adapter unavailable.");
        }

        var timeout = TimeSpan.FromSeconds(input.TimeoutSeconds);
        await _logEmitter.EmitLogAsync(
            input.JobId,
            "system",
            $"[K8S] Draining workloads from node '{input.NodeName}' (timeout: {timeout.TotalSeconds}s)..."
        );

        try
        {
            var result = await adapter.DrainNodeAsync(
                input.NodeName,
                timeout,
                input.IgnoreDaemonSets,
                deleteEmptyDirData: true,
                onProgress: async msg => await _logEmitter.EmitLogAsync(input.JobId, "system", msg)
            );

            if (!result.Success)
            {
                var msg = result.ErrorMessage ?? $"Failed to drain workloads from node '{input.NodeName}'.";
                await _logEmitter.EmitLogAsync(input.JobId, "system", $"[K8S] Error: {msg}");
                return new KubernetesDrainResult(false, false, 0, msg);
            }

            var successMsg = $"Node '{input.NodeName}' drained successfully ({result.EvictedPodCount} pods evicted).";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[K8S] {successMsg}");
            return new KubernetesDrainResult(true, true, result.EvictedPodCount, successMsg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error draining node '{NodeName}'", input.NodeName);
            var msg = $"Drain failed with exception: {ex.Message}";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[K8S] Error: {msg}");
            return new KubernetesDrainResult(false, false, 0, msg);
        }
    }

    [Activity]
    public async Task<KubernetesUncordonResult> UncordonNodeAsync(KubernetesNodeInput input)
    {
        if (string.IsNullOrWhiteSpace(input.NodeName))
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", "[K8S] No node name specified; skipping uncordon step.");
            return new KubernetesUncordonResult(true, null, "Skipped: No node name provided.");
        }

        using var scope = _scopeFactory.CreateScope();
        var adapter = _adapter ?? scope.ServiceProvider.GetService<IKubernetesAdapter>();
        if (adapter == null)
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", "[K8S] Kubernetes adapter unavailable; skipping uncordon step.");
            return new KubernetesUncordonResult(true, input.NodeName, "Skipped: Kubernetes adapter unavailable.");
        }

        await _logEmitter.EmitLogAsync(input.JobId, "system", $"[K8S] Uncordoning node '{input.NodeName}' (restoring scheduling)...");
        var uncordoned = await adapter.UncordonNodeAsync(input.NodeName);

        if (!uncordoned)
        {
            var msg = $"Failed to uncordon node '{input.NodeName}'.";
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[K8S] Warning: {msg}");
            return new KubernetesUncordonResult(false, input.NodeName, msg);
        }

        var successMsg = $"Node '{input.NodeName}' uncordoned successfully (unschedulable = false). Scheduling restored.";
        await _logEmitter.EmitLogAsync(input.JobId, "system", $"[K8S] {successMsg}");
        return new KubernetesUncordonResult(true, input.NodeName, successMsg);
    }
}
