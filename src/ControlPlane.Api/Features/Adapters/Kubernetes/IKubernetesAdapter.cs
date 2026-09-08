using ControlPlane.Api.Features.Adapters.Config;

namespace ControlPlane.Api.Features.Adapters.Kubernetes;

public interface IKubernetesAdapter
{
    Task<bool> CordonNodeAsync(string nodeName, CancellationToken ct = default);
    Task<bool> UncordonNodeAsync(string nodeName, CancellationToken ct = default);
    Task<K8sDrainResult> DrainNodeAsync(string nodeName, TimeSpan timeout, bool ignoreDaemonSets = true, bool deleteEmptyDirData = true, CancellationToken ct = default);
    Task<K8sNodeStatus?> GetNodeStatusAsync(string nodeName, CancellationToken ct = default);
    Task<List<K8sDiscoveredNodeDto>> ListNodesAsync(CancellationToken ct = default);

    // Cluster test connection & Workload operations
    Task<KubernetesClusterTestResultDto> TestConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(new KubernetesClusterTestResultDto(true, "v1.31.0", 1, 10, "Connected"));

    Task<List<string>> ListNamespacesAsync(CancellationToken ct = default)
        => Task.FromResult(new List<string>());

    Task<List<K8sDeploymentSummaryDto>> ListDeploymentsAsync(string? namespaceName = null, CancellationToken ct = default)
        => Task.FromResult(new List<K8sDeploymentSummaryDto>());

    Task<bool> RestartDeploymentAsync(string namespaceName, string deploymentName, CancellationToken ct = default)
        => Task.FromResult(true);

    Task<bool> ScaleDeploymentAsync(string namespaceName, string deploymentName, int replicas, CancellationToken ct = default)
        => Task.FromResult(true);

    Task<List<K8sPodSummaryDto>> ListPodsAsync(string? namespaceName = null, string? nodeName = null, CancellationToken ct = default)
        => Task.FromResult(new List<K8sPodSummaryDto>());
}

