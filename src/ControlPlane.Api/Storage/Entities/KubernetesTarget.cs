namespace ControlPlane.Api.Storage.Entities;

/// <summary>
/// Correlation target linking a host to a managed Kubernetes cluster and node name.
/// </summary>
public class KubernetesTarget
{
    public string? ClusterId { get; set; }

    public string? NodeName { get; set; }
}
