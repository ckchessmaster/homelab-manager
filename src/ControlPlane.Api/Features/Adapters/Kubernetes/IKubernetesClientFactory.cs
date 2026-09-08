namespace ControlPlane.Api.Features.Adapters.Kubernetes;

/// <summary>
/// Factory for instantiating Kubernetes clients and adapters bound to specific cluster configurations.
/// </summary>
public interface IKubernetesClientFactory
{
    /// <summary>
    /// Creates a raw Kubernetes API client for the specified cluster ID, or the default configured cluster.
    /// </summary>
    Task<k8s.IKubernetes> CreateClientAsync(string? clusterId = null, CancellationToken ct = default);

    /// <summary>
    /// Creates a scoped IKubernetesAdapter for the specified cluster ID, or the default configured cluster.
    /// </summary>
    Task<IKubernetesAdapter> CreateAdapterAsync(string? clusterId = null, CancellationToken ct = default);
}
