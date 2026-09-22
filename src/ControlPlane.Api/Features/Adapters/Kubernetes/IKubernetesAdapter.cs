using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Kubernetes.Helm;

namespace ControlPlane.Api.Features.Adapters.Kubernetes;

public interface IKubernetesAdapter
{
    Task<bool> CordonNodeAsync(string nodeName, CancellationToken ct = default);
    Task<bool> UncordonNodeAsync(string nodeName, CancellationToken ct = default);
    Task<K8sDrainResult> DrainNodeAsync(string nodeName, TimeSpan timeout, bool ignoreDaemonSets = true, bool deleteEmptyDirData = true, CancellationToken ct = default, Func<string, Task>? onProgress = null);
    Task<K8sNodeStatus?> GetNodeStatusAsync(string nodeName, CancellationToken ct = default);
    Task<List<K8sDiscoveredNodeDto>> ListNodesAsync(CancellationToken ct = default);

    // Cluster test connection & Workload operations
    Task<KubernetesClusterTestResultDto> TestConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(new KubernetesClusterTestResultDto(true, "v1.31.0", 1, 10, "Connected"));

    Task<List<string>> ListNamespacesAsync(CancellationToken ct = default)
        => Task.FromResult(new List<string>());

    Task<List<K8sDeploymentSummaryDto>> ListDeploymentsAsync(string? namespaceName = null, CancellationToken ct = default)
        => Task.FromResult(new List<K8sDeploymentSummaryDto>());

    Task<bool> RestartDeploymentAsync(string namespaceName, string deploymentName, CancellationToken ct = default)
        => Task.FromResult(true);

    Task<bool> ScaleDeploymentAsync(string namespaceName, string deploymentName, int replicas, CancellationToken ct = default)
        => Task.FromResult(true);

    Task<List<K8sPodSummaryDto>> ListPodsAsync(string? namespaceName = null, string? nodeName = null, CancellationToken ct = default)
        => Task.FromResult(new List<K8sPodSummaryDto>());

    async Task<List<K8sWorkloadItemDto>> ListAllWorkloadsAsync(string? namespaceName = null, CancellationToken ct = default)
    {
        var deps = await ListDeploymentsAsync(namespaceName, ct);
        return deps.Select(d => new K8sWorkloadItemDto(
            Name: d.Name,
            Namespace: d.Namespace,
            Kind: "Deployment",
            DesiredReplicas: d.DesiredReplicas,
            ReadyReplicas: d.ReadyReplicas,
            AvailableReplicas: d.AvailableReplicas,
            Images: d.Images,
            CreationTimestamp: d.CreationTimestamp,
            IsProtected: false
        )).ToList();
    }

    Task<List<K8sWorkloadItemDto>> ListAllWorkloadsAsync(string? namespaceName, bool includeAllResources, CancellationToken ct = default)
        => ListAllWorkloadsAsync(namespaceName, ct);

    Task<bool> RestartWorkloadAsync(string kind, string namespaceName, string name, CancellationToken ct = default)
        => Task.FromResult(true);

    Task<bool> UpdateWorkloadImageAsync(string kind, string namespaceName, string name, string newImage, string? containerName = null, CancellationToken ct = default)
        => Task.FromResult(true);

    Task<bool> RecreateWorkloadPodsAsync(string kind, string namespaceName, string name, CancellationToken ct = default)
        => Task.FromResult(true);

    Task<bool> TriggerCronJobAsync(string namespaceName, string cronJobName, CancellationToken ct = default)
        => Task.FromResult(true);

    Task<K8sAppBundleDto?> GetAppBundleAsync(string namespaceName, string appName, CancellationToken ct = default)
        => Task.FromResult<K8sAppBundleDto?>(null);

    Task<string?> GetResourceYamlAsync(string namespaceName, string name, string? kind = null, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    Task<K8sApplyResultDto> ApplyManifestYamlAsync(string yamlContent, bool dryRun = false, CancellationToken ct = default)
        => Task.FromResult(new K8sApplyResultDto(true, "Applied successfully", new List<string>()));

    Task<bool> DeleteAppBundleAsync(string namespaceName, string appName, K8sDeleteOptionsDto options, CancellationToken ct = default)
        => Task.FromResult(true);

    Task<List<K8sServiceSummaryDto>> ListServicesAsync(string? namespaceName = null, CancellationToken ct = default)
        => Task.FromResult(new List<K8sServiceSummaryDto>());

    Task<K8sServiceDetailDto?> GetServiceAsync(string namespaceName, string serviceName, CancellationToken ct = default)
        => Task.FromResult<K8sServiceDetailDto?>(null);

    Task<K8sResourceOperationResultDto> UpdateServiceAsync(string namespaceName, string serviceName, K8sUpdateServiceRequestDto request, CancellationToken ct = default)
        => Task.FromResult(new K8sResourceOperationResultDto(true, "Updated successfully.", serviceName));

    Task<List<K8sIngressSummaryDto>> ListIngressesAsync(string? namespaceName = null, CancellationToken ct = default)
        => Task.FromResult(new List<K8sIngressSummaryDto>());

    Task<K8sIngressDetailDto?> GetIngressAsync(string namespaceName, string ingressName, CancellationToken ct = default)
        => Task.FromResult<K8sIngressDetailDto?>(null);

    Task<K8sResourceOperationResultDto> UpdateIngressAsync(string namespaceName, string ingressName, K8sUpdateIngressRequestDto request, CancellationToken ct = default)
        => Task.FromResult(new K8sResourceOperationResultDto(true, "Updated successfully.", ingressName));

    Task<List<K8sCertificateSummaryDto>> ListCertificatesAsync(string? namespaceName = null, CancellationToken ct = default)
        => Task.FromResult(new List<K8sCertificateSummaryDto>());

    Task<K8sStorageOverviewDto> GetStorageOverviewAsync(string? namespaceName = null, CancellationToken ct = default)
        => Task.FromResult(new K8sStorageOverviewDto(0, 0, 0, new List<K8sPvcSummaryDto>(), new List<K8sStorageClassDto>(), false));

    Task<K8sClusterVitalsDto> GetClusterVitalsAsync(CancellationToken ct = default)
        => Task.FromResult(new K8sClusterVitalsDto(false, 0, 0, 0, 0, new List<K8sNodeVitalDto>(), new List<K8sPodVitalDto>()));

    Task<bool> DeleteNamespaceAsync(string namespaceName, CancellationToken ct = default)
        => Task.FromResult(true);

    Task<bool> CreateNamespaceAsync(string namespaceName, Dictionary<string, string>? labels = null, Dictionary<string, string>? annotations = null, CancellationToken ct = default)
        => Task.FromResult(true);

    // Helm operations
    Task<List<HelmReleaseSummaryDto>> ListHelmReleasesAsync(string? namespaceName = null, CancellationToken ct = default)
        => Task.FromResult(new List<HelmReleaseSummaryDto>());

    Task<HelmReleaseDetailDto?> GetHelmReleaseAsync(string namespaceName, string releaseName, CancellationToken ct = default)
        => Task.FromResult<HelmReleaseDetailDto?>(null);

    Task<HelmReleaseDetailDto?> GetHelmReleaseAsync(string namespaceName, string releaseName, int? revision, CancellationToken ct = default)
        => GetHelmReleaseAsync(namespaceName, releaseName, ct);

    Task<List<HelmReleaseRevisionDto>> GetHelmReleaseHistoryAsync(string namespaceName, string releaseName, CancellationToken ct = default)
        => Task.FromResult(new List<HelmReleaseRevisionDto>());

    Task<HelmOperationResultDto> InstallOrUpgradeHelmReleaseAsync(InstallHelmReleaseRequestDto request, CancellationToken ct = default)
        => Task.FromResult(new HelmOperationResultDto(true, "Installed"));

    Task<HelmOperationResultDto> RollbackHelmReleaseAsync(string namespaceName, string releaseName, int revision, CancellationToken ct = default)
        => Task.FromResult(new HelmOperationResultDto(true, "Rolled back"));

    Task<HelmOperationResultDto> UninstallHelmReleaseAsync(string namespaceName, string releaseName, CancellationToken ct = default)
        => Task.FromResult(new HelmOperationResultDto(true, "Uninstalled"));

    // Secrets & ConfigMaps
    Task<List<K8sSecretSummaryDto>> ListSecretsAsync(string? namespaceName = null, CancellationToken ct = default)
        => Task.FromResult(new List<K8sSecretSummaryDto>());

    Task<K8sSecretDetailDto?> GetSecretAsync(string namespaceName, string secretName, bool maskValues = true, CancellationToken ct = default)
        => Task.FromResult<K8sSecretDetailDto?>(null);

    Task<K8sResourceOperationResultDto> CreateSecretAsync(string namespaceName, K8sCreateSecretRequestDto request, CancellationToken ct = default)
        => Task.FromResult(new K8sResourceOperationResultDto(true, "Secret created successfully.", request.Name));

    Task<K8sResourceOperationResultDto> UpdateSecretAsync(string namespaceName, string secretName, K8sCreateSecretRequestDto request, CancellationToken ct = default)
        => Task.FromResult(new K8sResourceOperationResultDto(true, "Secret updated successfully.", secretName));

    Task<bool> DeleteSecretAsync(string namespaceName, string secretName, CancellationToken ct = default)
        => Task.FromResult(true);

    Task<List<K8sConfigMapSummaryDto>> ListConfigMapsAsync(string? namespaceName = null, CancellationToken ct = default)
        => Task.FromResult(new List<K8sConfigMapSummaryDto>());

    Task<K8sConfigMapDetailDto?> GetConfigMapAsync(string namespaceName, string configMapName, CancellationToken ct = default)
        => Task.FromResult<K8sConfigMapDetailDto?>(null);

    Task<K8sResourceOperationResultDto> CreateConfigMapAsync(string namespaceName, K8sCreateConfigMapRequestDto request, CancellationToken ct = default)
        => Task.FromResult(new K8sResourceOperationResultDto(true, "ConfigMap created successfully.", request.Name));

    Task<K8sResourceOperationResultDto> UpdateConfigMapAsync(string namespaceName, string configMapName, K8sCreateConfigMapRequestDto request, CancellationToken ct = default)
        => Task.FromResult(new K8sResourceOperationResultDto(true, "ConfigMap updated successfully.", configMapName));

    Task<bool> DeleteConfigMapAsync(string namespaceName, string configMapName, CancellationToken ct = default)
        => Task.FromResult(true);

    // Pod Logs & Diagnostics
    Task<string> GetPodLogsAsync(string namespaceName, string podName, string? container = null, int? tailLines = 100, CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    // Deployment Revisions & Rollback
    Task<List<K8sDeploymentRevisionDto>> GetDeploymentRevisionsAsync(string namespaceName, string deploymentName, CancellationToken ct = default)
        => Task.FromResult(new List<K8sDeploymentRevisionDto>());

    Task<bool> RollbackDeploymentAsync(string namespaceName, string deploymentName, int revision, CancellationToken ct = default)
        => Task.FromResult(true);

    // Events
    Task<List<K8sEventDto>> ListEventsAsync(string? namespaceName = null, string? type = null, CancellationToken ct = default)
        => Task.FromResult(new List<K8sEventDto>());
}

