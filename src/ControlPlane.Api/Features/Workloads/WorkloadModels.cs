namespace ControlPlane.Api.Features.Workloads;

using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Workloads.ImageUpdates;

public record WorkloadSummaryDto(
    string ClusterId,
    string ClusterName,
    string Namespace,
    string Name,
    int DesiredReplicas,
    int ReadyReplicas,
    int AvailableReplicas,
    List<string> Images,
    DateTime? CreationTimestamp,
    string Status,
    string Kind = "Deployment",
    bool IsProtected = false,
    string? Schedule = null,
    DateTime? LastScheduleTime = null,
    ImageUpdateInfoDto? ImageUpdate = null
);

public record WorkloadAggregationResultDto(
    List<WorkloadSummaryDto> Items,
    List<string> Clusters,
    List<string> Namespaces,
    int TotalDeployments,
    int HealthyDeployments
);

public record ScaleWorkloadRequest(
    int Replicas
);
