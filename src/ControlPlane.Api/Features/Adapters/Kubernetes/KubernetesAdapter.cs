using System.Net;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Features.Adapters.Kubernetes;

public class KubernetesAdapter : IKubernetesAdapter
{
    private readonly IKubernetes _client;
    private readonly ILogger<KubernetesAdapter> _logger;

    public KubernetesAdapter(IKubernetes client, ILogger<KubernetesAdapter> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<bool> CordonNodeAsync(string nodeName, CancellationToken ct = default)
    {
        _logger.LogInformation("Cordoning Kubernetes node '{Node}' (setting unschedulable = true)...", nodeName);
        try
        {
            var patch = new V1Patch("{\"spec\": {\"unschedulable\": true}}", V1Patch.PatchType.MergePatch);
            await _client.CoreV1.PatchNodeAsync(patch, nodeName, cancellationToken: ct);
            _logger.LogInformation("Node '{Node}' cordoned successfully.", nodeName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cordon node '{Node}'", nodeName);
            return false;
        }
    }

    public async Task<bool> UncordonNodeAsync(string nodeName, CancellationToken ct = default)
    {
        _logger.LogInformation("Uncordoning Kubernetes node '{Node}' (setting unschedulable = false)...", nodeName);
        try
        {
            var patch = new V1Patch("{\"spec\": {\"unschedulable\": false}}", V1Patch.PatchType.MergePatch);
            await _client.CoreV1.PatchNodeAsync(patch, nodeName, cancellationToken: ct);
            _logger.LogInformation("Node '{Node}' uncordoned successfully.", nodeName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uncordon node '{Node}'", nodeName);
            return false;
        }
    }

    public async Task<K8sDrainResult> DrainNodeAsync(
        string nodeName,
        TimeSpan timeout,
        bool ignoreDaemonSets = true,
        bool deleteEmptyDirData = true,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Initiating drain on Kubernetes node '{Node}' (timeout: {Timeout}s)...", nodeName, timeout.TotalSeconds);

        // 1. Cordon first
        var cordoned = await CordonNodeAsync(nodeName, ct);
        if (!cordoned)
        {
            return new K8sDrainResult(nodeName, false, 0, 0, "Failed to cordon node before eviction.");
        }

        // 2. Query pods scheduled on the node
        var podsResponse = await _client.CoreV1.ListPodForAllNamespacesAsync(
            fieldSelector: $"spec.nodeName={nodeName}",
            cancellationToken: ct);

        var allPods = podsResponse.Items ?? new List<V1Pod>();
        var evictablePods = allPods.Where(pod => IsEvictable(pod, ignoreDaemonSets)).ToList();

        _logger.LogInformation("Node '{Node}' has {TotalPods} total pods, {EvictableCount} evictable.",
            nodeName, allPods.Count, evictablePods.Count);

        if (evictablePods.Count == 0)
        {
            return new K8sDrainResult(nodeName, true, 0, 0, null);
        }

        var evictedCount = 0;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        // 3. Issue eviction requests
        foreach (var pod in evictablePods)
        {
            var podName = pod.Metadata?.Name ?? "unknown";
            var podNamespace = pod.Metadata?.NamespaceProperty ?? "default";

            var evicted = await EvictPodWithRetryAsync(podName, podNamespace, cts.Token);
            if (evicted)
            {
                evictedCount++;
            }
        }

        // 4. Poll until evictable pods have terminated
        while (!cts.Token.IsCancellationRequested)
        {
            var remainingPodsResp = await _client.CoreV1.ListPodForAllNamespacesAsync(
                fieldSelector: $"spec.nodeName={nodeName}",
                cancellationToken: cts.Token);

            var remaining = (remainingPodsResp.Items ?? new List<V1Pod>())
                .Count(p => IsEvictable(p, ignoreDaemonSets));

            if (remaining == 0)
            {
                _logger.LogInformation("All evictable pods cleanly terminated on node '{Node}'.", nodeName);
                return new K8sDrainResult(nodeName, true, evictedCount, 0, null);
            }

            _logger.LogDebug("Waiting for {Remaining} pods to terminate on node '{Node}'...", remaining, nodeName);
            try
            {
                await Task.Delay(2000, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return new K8sDrainResult(
            nodeName,
            false,
            evictedCount,
            evictablePods.Count - evictedCount,
            $"Drain timed out waiting for pods to terminate after {timeout.TotalSeconds} seconds."
        );
    }

    public async Task<K8sNodeStatus?> GetNodeStatusAsync(string nodeName, CancellationToken ct = default)
    {
        try
        {
            var node = await _client.CoreV1.ReadNodeAsync(nodeName, cancellationToken: ct);
            if (node == null) return null;

            var isReady = node.Status?.Conditions?.Any(c => c.Type == "Ready" && c.Status == "True") ?? false;
            var unschedulable = node.Spec?.Unschedulable ?? false;

            var internalIp = node.Status?.Addresses?
                .FirstOrDefault(a => a.Type == "InternalIP")?.Address;

            var podCount = 0;
            try
            {
                var podsResp = await _client.CoreV1.ListPodForAllNamespacesAsync(
                    fieldSelector: $"spec.nodeName={nodeName}",
                    cancellationToken: ct);
                podCount = podsResp.Items?.Count ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not list pods for node '{Node}' status query", nodeName);
            }

            return new K8sNodeStatus(nodeName, isReady, unschedulable, internalIp, podCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get node status for '{Node}'", nodeName);
            return null;
        }
    }

    private static bool IsEvictable(V1Pod pod, bool ignoreDaemonSets)
    {
        // Ignore static / mirror pods (kubelet created)
        if (pod.Metadata?.Annotations != null && pod.Metadata.Annotations.ContainsKey("kubernetes.io/config.mirror"))
        {
            return false;
        }

        // Ignore DaemonSets if specified
        if (ignoreDaemonSets && pod.Metadata?.OwnerReferences != null &&
            pod.Metadata.OwnerReferences.Any(o => string.Equals(o.Kind, "DaemonSet", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Ignore already succeeded or failed pods
        if (pod.Status?.Phase == "Succeeded" || pod.Status?.Phase == "Failed")
        {
            return false;
        }

        return true;
    }

    private async Task<bool> EvictPodWithRetryAsync(string podName, string podNamespace, CancellationToken ct)
    {
        var eviction = new V1Eviction
        {
            Metadata = new V1ObjectMeta
            {
                Name = podName,
                NamespaceProperty = podNamespace
            },
            DeleteOptions = new V1DeleteOptions
            {
                GracePeriodSeconds = 30
            }
        };

        var backoffMs = 500;
        var maxBackoffMs = 5000;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Evicting pod '{Namespace}/{Pod}'...", podNamespace, podName);
                await _client.CoreV1.CreateNamespacedPodEvictionAsync(eviction, podName, podNamespace, cancellationToken: ct);
                return true;
            }
            catch (HttpOperationException ex) when ((int)ex.Response.StatusCode == 429)
            {
                // HTTP 429: Too Many Requests (PDB violation)
                _logger.LogWarning("Eviction of '{Namespace}/{Pod}' rejected by PDB (429 Too Many Requests). Retrying in {Backoff}ms...",
                    podNamespace, podName, backoffMs);

                await Task.Delay(backoffMs, ct);
                backoffMs = Math.Min(backoffMs * 2, maxBackoffMs);
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                // Already deleted
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error evicting pod '{Namespace}/{Pod}'", podNamespace, podName);
                return false;
            }
        }

        return false;
    }

    public async Task<List<K8sDiscoveredNodeDto>> ListNodesAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Listing Kubernetes cluster nodes for discovery...");
        var discovered = new List<K8sDiscoveredNodeDto>();

        try
        {
            var nodes = await _client.CoreV1.ListNodeAsync(cancellationToken: ct);
            if (nodes?.Items == null) return discovered;

            foreach (var node in nodes.Items)
            {
                var name = node.Metadata?.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;

                var internalIp = node.Status?.Addresses?
                    .FirstOrDefault(a => string.Equals(a.Type, "InternalIP", StringComparison.OrdinalIgnoreCase))?
                    .Address;

                var roles = new List<string>();
                if (node.Metadata?.Labels != null)
                {
                    foreach (var (k, v) in node.Metadata.Labels)
                    {
                        if (k.StartsWith("node-role.kubernetes.io/"))
                        {
                            var role = k["node-role.kubernetes.io/".Length..];
                            if (!string.IsNullOrWhiteSpace(role)) roles.Add(role);
                        }
                    }
                }
                if (roles.Count == 0) roles.Add("worker");

                var isReady = node.Status?.Conditions?
                    .Any(c => string.Equals(c.Type, "Ready", StringComparison.OrdinalIgnoreCase) && string.Equals(c.Status, "True", StringComparison.OrdinalIgnoreCase)) ?? false;

                var unschedulable = node.Spec?.Unschedulable ?? false;
                var osImage = node.Status?.NodeInfo?.OsImage;
                var kernelVersion = node.Status?.NodeInfo?.KernelVersion;
                var containerRuntime = node.Status?.NodeInfo?.ContainerRuntimeVersion;
                var labels = node.Metadata?.Labels != null
                    ? new Dictionary<string, string>(node.Metadata.Labels)
                    : new Dictionary<string, string>();

                discovered.Add(new K8sDiscoveredNodeDto(
                    Name: name,
                    InternalIp: internalIp,
                    Roles: roles,
                    IsReady: isReady,
                    Unschedulable: unschedulable,
                    OsImage: osImage,
                    KernelVersion: kernelVersion,
                    ContainerRuntimeVersion: containerRuntime,
                    Labels: labels
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Kubernetes nodes for discovery");
        }

        return discovered;
    }
}
