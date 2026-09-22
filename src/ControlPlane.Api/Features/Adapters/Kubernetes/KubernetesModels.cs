namespace ControlPlane.Api.Features.Adapters.Kubernetes;

public record K8sNodeVitalsDto(
    string Name,
    bool IsReady,
    bool Unschedulable,
    List<string> Roles,
    string? OsImage,
    string? KernelVersion,
    int PodCount
);

public record KubernetesClusterVitalsDto(
    string ClusterId,
    string ClusterName,
    int TotalNodes,
    int ReadyNodes,
    int TotalPods,
    int RunningPods,
    int TotalNamespaces,
    long LatencyMs,
    List<K8sNodeVitalsDto> Nodes,
    DateTimeOffset FetchedAt
);

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
    DateTime? StartTime,
    List<string>? Containers = null
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
    bool IsSecret = false,
    string? SecretName = null,
    string? SecretKey = null,
    string? ConfigMapName = null,
    string? ConfigMapKey = null,
    string? ContainerName = null
);

public record K8sAppEnvFromSource(
    string? SecretRef = null,
    string? ConfigMapRef = null,
    string? Prefix = null,
    string? ContainerName = null
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
    string? RawYaml = null,
    List<K8sAppEnvFromSource>? EnvFrom = null
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

public record K8sIngressDetailDto(
    string Name,
    string Namespace,
    string? IngressClass,
    List<string> Hosts,
    List<K8sIngressRulePathDto> Paths,
    List<string> TlsHosts,
    string? TlsSecretName,
    Dictionary<string, string> Annotations,
    Dictionary<string, string>? Labels,
    DateTime? CreationTimestamp,
    string RawYaml
);

public record K8sServicePortDto(
    string? Name,
    int Port,
    string? TargetPort,
    string Protocol,
    int? NodePort = null
);

public record K8sServiceSummaryDto(
    string Name,
    string Namespace,
    string Type,
    string? ClusterIp,
    List<string>? ExternalIps,
    List<K8sServicePortDto> Ports,
    Dictionary<string, string>? Selector,
    int EndpointsCount,
    DateTime? CreationTimestamp
);

public record K8sServiceDetailDto(
    string Name,
    string Namespace,
    string Type,
    string? ClusterIp,
    List<string>? ClusterIps,
    List<string>? ExternalIps,
    List<K8sServicePortDto> Ports,
    Dictionary<string, string>? Selector,
    Dictionary<string, string>? Annotations,
    Dictionary<string, string>? Labels,
    int EndpointsCount,
    DateTime? CreationTimestamp,
    string RawYaml
);

public record K8sUpdateServiceRequestDto(
    string? RawYaml = null,
    string? Type = null,
    List<K8sServicePortDto>? Ports = null,
    Dictionary<string, string>? Selector = null,
    Dictionary<string, string>? Annotations = null,
    Dictionary<string, string>? Labels = null
);

public record K8sUpdateIngressRequestDto(
    string? RawYaml = null,
    string? IngressClass = null,
    List<string>? Hosts = null,
    List<K8sIngressRulePathDto>? Paths = null,
    bool? TlsEnabled = null,
    string? TlsSecretName = null,
    Dictionary<string, string>? Annotations = null
);

public record K8sResourceYamlDto(
    string Name,
    string Namespace,
    string Kind,
    string YamlContent
);

public record K8sCertificateSummaryDto(
    string Name,
    string Namespace,
    string? Issuer,
    string? SecretName,
    bool IsReady,
    DateTime? RenewalTime,
    DateTime? NotAfter,
    List<string> Conditions,
    List<string>? DnsNames = null
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
    DateTime? CreationTimestamp,
    long? UsedBytes = null,
    long? CapacityBytes = null,
    string? ReplicaHealth = null
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

public record K8sVersionInfoDto(
    string GitVersion,
    string? Major = null,
    string? Minor = null,
    string? Platform = null,
    string? LatestStableVersion = null,
    bool IsOutdated = false,
    string? UpdateType = null
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
    bool Ready,
    bool Unschedulable = false,
    string? KubeletVersion = null,
    string? OsImage = null,
    string? KernelVersion = null,
    string? ContainerRuntime = null,
    string? Architecture = null
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
    List<K8sPodVitalDto> TopPods,
    K8sVersionInfoDto? ServerVersion = null
);

// --- Individual Resources: Secrets & ConfigMaps ---

public record K8sSecretSummaryDto(
    string Name,
    string Namespace,
    string Type,
    int KeysCount,
    List<string> Keys,
    DateTime? CreationTimestamp,
    bool IsSystem = false,
    List<string>? UsedBy = null
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
    bool IsSystem = false,
    List<string>? UsedBy = null
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

public record K8sDeploymentRevisionDto(
    int Revision,
    DateTime? CreationTimestamp,
    List<string> Images,
    int Replicas,
    int ReadyReplicas,
    bool IsCurrent
);

public record K8sRollbackRequestDto(
    int Revision
);

public record K8sEventDto(
    string Name,
    string Namespace,
    string Type,
    string Reason,
    string Message,
    string InvolvedObjectKind,
    string InvolvedObjectName,
    int? Count,
    DateTime? LastTimestamp,
    string? SourceComponent
);
