namespace ControlPlane.Api.Features.Adapters.Kubernetes;

public record K8sNodeStatus(
    string NodeName,
    bool IsReady,
    bool Unschedulable,
    string? InternalIp,
    int PodCount
);

public record K8sCordonRequest(
    string NodeName
);

public record K8sUncordonRequest(
    string NodeName
);

public record K8sDrainRequest(
    string NodeName,
    int TimeoutSeconds = 180,
    bool IgnoreDaemonSets = true,
    bool DeleteEmptyDirData = true
);

public record K8sDrainResult(
    string NodeName,
    bool Success,
    int EvictedPodCount,
    int RemainingPods,
    string? ErrorMessage
);

public record K8sDiscoveredNodeDto(
    string Name,
    string? InternalIp,
    List<string> Roles,
    bool IsReady,
    bool Unschedulable,
    string? OsImage,
    string? KernelVersion,
    string? ContainerRuntimeVersion,
    Dictionary<string, string> Labels
);

public record K8sDeploymentSummaryDto(
    string Name,
    string Namespace,
    int DesiredReplicas,
    int ReadyReplicas,
    int AvailableReplicas,
    List<string> Images,
    DateTime? CreationTimestamp
);

public record K8sPodSummaryDto(
    string Name,
    string Namespace,
    string Phase,
    string? NodeName,
    string? PodIp,
    int RestartCount,
    bool IsReady,
    DateTime? StartTime
);

public record K8sScaleDeploymentRequest(
    int Replicas
);

