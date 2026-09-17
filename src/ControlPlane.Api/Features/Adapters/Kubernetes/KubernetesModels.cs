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

public record K8sWorkloadItemDto(
    string Name,
    string Namespace,
    string Kind,
    int DesiredReplicas,
    int ReadyReplicas,
    int AvailableReplicas,
    List<string> Images,
    DateTime? CreationTimestamp,
    string? Schedule = null,
    bool? Suspend = null,
    DateTime? LastScheduleTime = null,
    bool IsProtected = false
);

public record K8sAppPortMapping(
    string Name,
    int ContainerPort,
    int ServicePort,
    string Protocol = "TCP"
);

public record K8sAppEnvVar(
    string Key,
    string Value,
    bool IsSecret = false
);

public record K8sAppVolumeMount(
    string Name,
    string MountPath,
    string PvcName,
    string StorageSize = "10Gi",
    string StorageClass = "longhorn"
);

public record K8sAppBundleDto(
    string Name,
    string Namespace,
    string Kind,
    int Replicas,
    string Image,
    List<K8sAppPortMapping> Ports,
    string? IngressHost,
    string? IngressPath,
    bool TlsEnabled,
    List<K8sAppEnvVar> EnvironmentVariables,
    List<K8sAppVolumeMount> VolumeMounts,
    string? CpuRequest = null,
    string? CpuLimit = null,
    string? MemoryRequest = null,
    string? MemoryLimit = null,
    string? RawYaml = null
);

public record K8sApplyRequestDto(
    string YamlContent,
    bool DryRun = false
);

public record K8sApplyResultDto(
    bool Success,
    string Message,
    List<string> AffectedResources,
    List<string>? Warnings = null,
    string? Diff = null
);

public record K8sDeleteOptionsDto(
    bool DeleteWorkload = true,
    bool DeleteService = true,
    bool DeleteIngress = true,
    bool DeletePvc = false,
    string? ConfirmedName = null
);

public record K8sDeleteNamespaceRequestDto(
    string ConfirmedName
);

public record K8sCreateNamespaceRequestDto(
    string Name,
    Dictionary<string, string>? Labels = null,
    Dictionary<string, string>? Annotations = null
);

public record K8sIngressRulePathDto(
    string Path,
    string PathType,
    string ServiceName,
    int ServicePort,
    int EndpointsCount = 0
);

public record K8sIngressSummaryDto(
    string Name,
    string Namespace,
    string? IngressClass,
    List<string> Hosts,
    List<K8sIngressRulePathDto> Paths,
    List<string> TlsHosts,
    Dictionary<string, string> Annotations,
    DateTime? CreationTimestamp
);

public record K8sCertificateSummaryDto(
    string Name,
    string Namespace,
    string? Issuer,
    string? SecretName,
    bool IsReady,
    DateTime? RenewalTime,
    DateTime? NotAfter,
    List<string> Conditions
);

public record K8sPvcSummaryDto(
    string Name,
    string Namespace,
    string Status,
    string? VolumeName,
    string? Capacity,
    string? StorageClass,
    List<string> AccessModes,
    List<string> MountingPods,
    DateTime? CreationTimestamp
);

public record K8sStorageClassDto(
    string Name,
    string Provisioner,
    string ReclaimPolicy,
    string VolumeBindingMode,
    bool IsDefault
);

public record K8sStorageOverviewDto(
    int TotalPvcs,
    int BoundPvcs,
    long TotalCapacityBytes,
    List<K8sPvcSummaryDto> Pvcs,
    List<K8sStorageClassDto> StorageClasses,
    bool LonghornDetected
);

public record K8sNodeVitalDto(
    string NodeName,
    long CpuUsageMillis,
    long CpuAllocatableMillis,
    long MemoryUsageBytes,
    long MemoryAllocatableBytes,
    bool DiskPressure,
    bool MemoryPressure,
    bool PidPressure,
    bool Ready
);

public record K8sPodVitalDto(
    string PodName,
    string Namespace,
    long CpuUsageMillis,
    long MemoryUsageBytes
);

public record K8sClusterVitalsDto(
    bool MetricsServerAvailable,
    long TotalCpuUsageMillis,
    long TotalCpuAllocatableMillis,
    long TotalMemoryUsageBytes,
    long TotalMemoryAllocatableBytes,
    List<K8sNodeVitalDto> Nodes,
    List<K8sPodVitalDto> TopPods
);

// --- Individual Resources: Secrets & ConfigMaps ---

public record K8sSecretSummaryDto(
    string Name,
    string Namespace,
    string Type,
    int KeysCount,
    List<string> Keys,
    DateTime? CreationTimestamp,
    bool IsSystem = false
);

public record K8sSecretDetailDto(
    string Name,
    string Namespace,
    string Type,
    Dictionary<string, string> Data,
    Dictionary<string, string>? Labels,
    Dictionary<string, string>? Annotations,
    DateTime? CreationTimestamp
);

public record K8sCreateSecretRequestDto(
    string Name,
    string Namespace,
    string Type = "Opaque",
    Dictionary<string, string>? StringData = null,
    Dictionary<string, string>? Labels = null,
    Dictionary<string, string>? Annotations = null
);

public record K8sConfigMapSummaryDto(
    string Name,
    string Namespace,
    int KeysCount,
    List<string> Keys,
    DateTime? CreationTimestamp,
    bool IsSystem = false
);

public record K8sConfigMapDetailDto(
    string Name,
    string Namespace,
    Dictionary<string, string> Data,
    Dictionary<string, string>? Labels,
    Dictionary<string, string>? Annotations,
    DateTime? CreationTimestamp
);

public record K8sCreateConfigMapRequestDto(
    string Name,
    string Namespace,
    Dictionary<string, string>? Data = null,
    Dictionary<string, string>? Labels = null,
    Dictionary<string, string>? Annotations = null
);

public record K8sResourceOperationResultDto(
    bool Success,
    string Message,
    string? ResourceName = null
);
