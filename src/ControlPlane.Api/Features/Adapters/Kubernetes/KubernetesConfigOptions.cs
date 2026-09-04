namespace ControlPlane.Api.Features.Adapters.Kubernetes;

public class KubernetesConfigOptions
{
    public const string SectionName = "Kubernetes";

    public string? KubeConfigPath { get; set; }

    public string? MasterUri { get; set; }

    public bool InClusterConfig { get; set; } = false;

    public int DrainTimeoutSeconds { get; set; } = 180;

    public bool IgnoreDaemonSets { get; set; } = true;

    public bool DeleteEmptyDirData { get; set; } = true;
}
