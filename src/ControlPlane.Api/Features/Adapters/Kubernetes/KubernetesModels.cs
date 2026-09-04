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
