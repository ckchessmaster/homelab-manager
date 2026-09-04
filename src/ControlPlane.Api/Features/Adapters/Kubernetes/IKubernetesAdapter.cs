namespace ControlPlane.Api.Features.Adapters.Kubernetes;

public interface IKubernetesAdapter
{
    Task<bool> CordonNodeAsync(string nodeName, CancellationToken ct = default);
    Task<bool> UncordonNodeAsync(string nodeName, CancellationToken ct = default);
    Task<K8sDrainResult> DrainNodeAsync(string nodeName, TimeSpan timeout, bool ignoreDaemonSets = true, bool deleteEmptyDirData = true, CancellationToken ct = default);
    Task<K8sNodeStatus?> GetNodeStatusAsync(string nodeName, CancellationToken ct = default);
}
