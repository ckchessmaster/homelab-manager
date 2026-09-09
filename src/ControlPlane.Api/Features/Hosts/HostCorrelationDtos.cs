namespace ControlPlane.Api.Features.Hosts;

public record CorrelatedVmDto(
    int Vmid,
    string Name,
    string Type,
    string Status,
    Guid? HostId,
    string? Hostname,
    string? IpAddress,
    bool IsAgentOnline,
    string? K8sClusterId,
    string? K8sNodeName
);

public record CorrelatedHypervisorDto(
    Guid? HostId,
    string Hostname,
    string? FriendlyName,
    string ProxmoxNode,
    string? InstanceId,
    bool IsOnline
);

public record CorrelatedKubernetesDto(
    string ClusterId,
    string NodeName,
    List<string> Roles,
    bool IsReady,
    int RunningPodsCount
);

public record HostCorrelationDto(
    Guid HostId,
    string Hostname,
    bool IsHypervisor,
    string? HypervisorNode,
    List<CorrelatedVmDto> HostedVms,
    bool IsVm,
    CorrelatedHypervisorDto? Hypervisor,
    bool IsKubernetesNode,
    CorrelatedKubernetesDto? Kubernetes
);

public record KubernetesClusterImpactDto(
    string ClusterId,
    string NodeName,
    bool IsControlPlane,
    bool IsOnlyControlPlane,
    int TotalNodes,
    int ReadyNodes,
    int RunningPodsCount,
    bool QuorumAtRisk,
    string Summary
);

public record HostRebootImpactDto(
    Guid HostId,
    string Hostname,
    bool IsHypervisor,
    string? HypervisorNode,
    List<CorrelatedVmDto> AffectedRunningVms,
    bool IsKubernetesNode,
    KubernetesClusterImpactDto? KubernetesImpact,
    bool HasWarnings,
    bool RequiresConfirmation,
    List<string> WarningMessages
);

public record SyncCorrelationResultDto(
    bool Success,
    int CorrelatedKubernetesNodes,
    int CorrelatedProxmoxHosts,
    int TotalHostsUpdated,
    List<string> Messages
);
