using System.Diagnostics;
using System.Net;
using ControlPlane.Api.Features.Adapters.Config;
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Kubernetes node discovery operation was canceled.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning("Kubernetes cluster is unreachable or timed out ({Message}). Skipping Kubernetes node discovery.", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Kubernetes nodes for discovery");
        }

        return discovered;
    }

    public async Task<KubernetesClusterTestResultDto> TestConnectionAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var version = await _client.Version.GetCodeAsync(cancellationToken: ct);
            var nodes = await _client.CoreV1.ListNodeAsync(limit: 100, cancellationToken: ct);
            sw.Stop();

            var nodeCount = nodes?.Items?.Count ?? 0;
            var gitVersion = version?.GitVersion ?? "Unknown";

            return new KubernetesClusterTestResultDto(
                Success: true,
                ServerVersion: gitVersion,
                NodeCount: nodeCount,
                LatencyMs: sw.ElapsedMilliseconds,
                Message: $"Connected successfully to Kubernetes {gitVersion} ({nodeCount} nodes detected)."
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning("Failed to connect to Kubernetes cluster during test-connection: {Message}", ex.Message);
            return new KubernetesClusterTestResultDto(
                Success: false,
                ServerVersion: null,
                NodeCount: 0,
                LatencyMs: sw.ElapsedMilliseconds,
                Message: $"Connection failed: {ex.Message}"
            );
        }
    }

    public async Task<List<string>> ListNamespacesAsync(CancellationToken ct = default)
    {
        try
        {
            var namespaces = await _client.CoreV1.ListNamespaceAsync(cancellationToken: ct);
            return namespaces.Items?
                .Select(n => n.Metadata?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .ToList() ?? new List<string>();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Kubernetes list namespaces operation was canceled.");
            return new List<string>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning("Kubernetes cluster is unreachable or timed out while listing namespaces: {Message}", ex.Message);
            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Kubernetes namespaces");
            return new List<string>();
        }
    }

    public async Task<List<K8sDeploymentSummaryDto>> ListDeploymentsAsync(string? namespaceName = null, CancellationToken ct = default)
    {
        try
        {
            var deployments = string.IsNullOrWhiteSpace(namespaceName)
                ? await _client.AppsV1.ListDeploymentForAllNamespacesAsync(cancellationToken: ct)
                : await _client.AppsV1.ListNamespacedDeploymentAsync(namespaceName, cancellationToken: ct);

            if (deployments?.Items == null) return new List<K8sDeploymentSummaryDto>();

            return deployments.Items.Select(d =>
            {
                var images = d.Spec?.Template?.Spec?.Containers?
                    .Select(c => c.Image)
                    .Where(img => !string.IsNullOrWhiteSpace(img))
                    .Select(img => img!)
                    .ToList() ?? new List<string>();

                return new K8sDeploymentSummaryDto(
                    Name: d.Metadata?.Name ?? string.Empty,
                    Namespace: d.Metadata?.NamespaceProperty ?? "default",
                    DesiredReplicas: d.Spec?.Replicas ?? 0,
                    ReadyReplicas: d.Status?.ReadyReplicas ?? 0,
                    AvailableReplicas: d.Status?.AvailableReplicas ?? 0,
                    Images: images,
                    CreationTimestamp: d.Metadata?.CreationTimestamp
                );
            }).ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Kubernetes list deployments operation was canceled.");
            return new List<K8sDeploymentSummaryDto>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning("Kubernetes cluster is unreachable or timed out while listing deployments: {Message}", ex.Message);
            return new List<K8sDeploymentSummaryDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Kubernetes deployments");
            return new List<K8sDeploymentSummaryDto>();
        }
    }

    public async Task<bool> RestartDeploymentAsync(string namespaceName, string deploymentName, CancellationToken ct = default)
    {
        _logger.LogInformation("Triggering rollout restart for deployment '{Namespace}/{Name}'...", namespaceName, deploymentName);
        try
        {
            var nowIso = DateTime.UtcNow.ToString("o");
            var patchStr = $"{{\"spec\":{{\"template\":{{\"metadata\":{{\"annotations\":{{\"kubectl.kubernetes.io/restartedAt\":\"{nowIso}\"}}}}}}}}}}";
            var patch = new V1Patch(patchStr, V1Patch.PatchType.MergePatch);
            await _client.AppsV1.PatchNamespacedDeploymentAsync(patch, deploymentName, namespaceName, cancellationToken: ct);
            _logger.LogInformation("Rollout restart successfully initiated for deployment '{Namespace}/{Name}'.", namespaceName, deploymentName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart deployment '{Namespace}/{Name}'", namespaceName, deploymentName);
            return false;
        }
    }

    public async Task<bool> ScaleDeploymentAsync(string namespaceName, string deploymentName, int replicas, CancellationToken ct = default)
    {
        _logger.LogInformation("Scaling deployment '{Namespace}/{Name}' to {Replicas} replicas...", namespaceName, deploymentName, replicas);
        try
        {
            var patchStr = $"{{\"spec\":{{\"replicas\":{replicas}}}}}";
            var patch = new V1Patch(patchStr, V1Patch.PatchType.MergePatch);
            await _client.AppsV1.PatchNamespacedDeploymentAsync(patch, deploymentName, namespaceName, cancellationToken: ct);
            _logger.LogInformation("Deployment '{Namespace}/{Name}' scaled to {Replicas} replicas.", namespaceName, deploymentName, replicas);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scale deployment '{Namespace}/{Name}'", namespaceName, deploymentName);
            return false;
        }
    }

    public async Task<List<K8sPodSummaryDto>> ListPodsAsync(string? namespaceName = null, string? nodeName = null, CancellationToken ct = default)
    {
        try
        {
            string? fieldSelector = !string.IsNullOrWhiteSpace(nodeName) ? $"spec.nodeName={nodeName}" : null;

            var pods = string.IsNullOrWhiteSpace(namespaceName)
                ? await _client.CoreV1.ListPodForAllNamespacesAsync(fieldSelector: fieldSelector, cancellationToken: ct)
                : await _client.CoreV1.ListNamespacedPodAsync(namespaceName, fieldSelector: fieldSelector, cancellationToken: ct);

            if (pods?.Items == null) return new List<K8sPodSummaryDto>();

            return pods.Items.Select(p =>
            {
                var restartCount = p.Status?.ContainerStatuses?.Sum(cs => cs.RestartCount) ?? 0;
                var isReady = p.Status?.Conditions?
                    .Any(c => string.Equals(c.Type, "Ready", StringComparison.OrdinalIgnoreCase) && string.Equals(c.Status, "True", StringComparison.OrdinalIgnoreCase)) ?? false;

                return new K8sPodSummaryDto(
                    Name: p.Metadata?.Name ?? string.Empty,
                    Namespace: p.Metadata?.NamespaceProperty ?? "default",
                    Phase: p.Status?.Phase ?? "Unknown",
                    NodeName: p.Spec?.NodeName,
                    PodIp: p.Status?.PodIP,
                    RestartCount: restartCount,
                    IsReady: isReady,
                    StartTime: p.Status?.StartTime
                );
            }).ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Kubernetes list pods operation was canceled.");
            return new List<K8sPodSummaryDto>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning("Kubernetes cluster is unreachable or timed out while listing pods: {Message}", ex.Message);
            return new List<K8sPodSummaryDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Kubernetes pods");
            return new List<K8sPodSummaryDto>();
        }
    }
}
