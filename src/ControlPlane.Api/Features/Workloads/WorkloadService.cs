using System.Text.RegularExpressions;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Kubernetes.Helm;
using ControlPlane.Api.Features.Workloads.ImageUpdates;

namespace ControlPlane.Api.Features.Workloads;

public interface IWorkloadService
{
    Task<WorkloadAggregationResultDto> GetAggregatedWorkloadsAsync(string? clusterId = null, string? namespaceName = null, CancellationToken ct = default);
    Task<WorkloadAggregationResultDto> GetAggregatedWorkloadsAsync(string? clusterId, string? namespaceName, bool includeAll, CancellationToken ct = default);
    Task<List<K8sPodSummaryDto>> GetWorkloadPodsAsync(string clusterId, string namespaceName, string deploymentName, CancellationToken ct = default);
    Task<bool> RestartWorkloadAsync(string clusterId, string namespaceName, string name, CancellationToken ct = default);
    Task<bool> RestartWorkloadAsync(string clusterId, string namespaceName, string name, string kind, CancellationToken ct = default);
    Task<bool> UpdateWorkloadImageAsync(string clusterId, string namespaceName, string name, string newImage, string? kind = "Deployment", string? containerName = null, CancellationToken ct = default);
    Task<bool> RecreateWorkloadPodsAsync(string clusterId, string namespaceName, string name, string kind = "Deployment", CancellationToken ct = default);
    Task<bool> ScaleWorkloadAsync(string clusterId, string namespaceName, string deploymentName, int replicas, CancellationToken ct = default);
    Task<bool> TriggerCronJobAsync(string clusterId, string namespaceName, string cronJobName, CancellationToken ct = default);
    Task<K8sAppBundleDto?> GetAppBundleAsync(string clusterId, string namespaceName, string appName, CancellationToken ct = default);
    Task<string?> GetResourceYamlAsync(string clusterId, string namespaceName, string name, string? kind = null, CancellationToken ct = default);
    Task<K8sApplyResultDto> ApplyManifestYamlAsync(string clusterId, string yamlContent, bool dryRun = false, CancellationToken ct = default);
    Task<bool> DeleteAppBundleAsync(string clusterId, string namespaceName, string appName, K8sDeleteOptionsDto options, CancellationToken ct = default);
    Task<List<K8sServiceSummaryDto>> ListServicesAsync(string clusterId, string? namespaceName = null, CancellationToken ct = default);
    Task<K8sServiceDetailDto?> GetServiceAsync(string clusterId, string namespaceName, string serviceName, CancellationToken ct = default);
    Task<K8sResourceOperationResultDto> UpdateServiceAsync(string clusterId, string namespaceName, string serviceName, K8sUpdateServiceRequestDto request, CancellationToken ct = default);
    Task<List<K8sIngressSummaryDto>> ListIngressesAsync(string clusterId, string? namespaceName = null, CancellationToken ct = default);
    Task<K8sIngressDetailDto?> GetIngressAsync(string clusterId, string namespaceName, string ingressName, CancellationToken ct = default);
    Task<K8sResourceOperationResultDto> UpdateIngressAsync(string clusterId, string namespaceName, string ingressName, K8sUpdateIngressRequestDto request, CancellationToken ct = default);
    Task<List<K8sCertificateSummaryDto>> ListCertificatesAsync(string clusterId, string? namespaceName = null, CancellationToken ct = default);
    Task<K8sStorageOverviewDto> GetStorageOverviewAsync(string clusterId, string? namespaceName = null, CancellationToken ct = default);
    Task<K8sClusterVitalsDto> GetClusterVitalsAsync(string clusterId, CancellationToken ct = default);
    Task<bool> DeleteNamespaceAsync(string clusterId, string namespaceName, string confirmedName, CancellationToken ct = default);
    Task<bool> CreateNamespaceAsync(string clusterId, string namespaceName, Dictionary<string, string>? labels = null, Dictionary<string, string>? annotations = null, CancellationToken ct = default);

    // Secrets & ConfigMaps
    Task<List<K8sSecretSummaryDto>> ListSecretsAsync(string clusterId, string? namespaceName = null, CancellationToken ct = default);
    Task<K8sSecretDetailDto?> GetSecretAsync(string clusterId, string namespaceName, string secretName, bool maskValues = true, CancellationToken ct = default);
    Task<K8sResourceOperationResultDto> CreateSecretAsync(string clusterId, string namespaceName, K8sCreateSecretRequestDto request, CancellationToken ct = default);
    Task<K8sResourceOperationResultDto> UpdateSecretAsync(string clusterId, string namespaceName, string secretName, K8sCreateSecretRequestDto request, CancellationToken ct = default);
    Task<bool> DeleteSecretAsync(string clusterId, string namespaceName, string secretName, CancellationToken ct = default);
    Task<List<K8sConfigMapSummaryDto>> ListConfigMapsAsync(string clusterId, string? namespaceName = null, CancellationToken ct = default);
    Task<K8sConfigMapDetailDto?> GetConfigMapAsync(string clusterId, string namespaceName, string configMapName, CancellationToken ct = default);
    Task<K8sResourceOperationResultDto> CreateConfigMapAsync(string clusterId, string namespaceName, K8sCreateConfigMapRequestDto request, CancellationToken ct = default);
    Task<K8sResourceOperationResultDto> UpdateConfigMapAsync(string clusterId, string namespaceName, string configMapName, K8sCreateConfigMapRequestDto request, CancellationToken ct = default);
    Task<bool> DeleteConfigMapAsync(string clusterId, string namespaceName, string configMapName, CancellationToken ct = default);

    // Helm operations & Catalog
    Task<List<HelmReleaseSummaryDto>> ListHelmReleasesAsync(string clusterId, string? namespaceName = null, CancellationToken ct = default);
    Task<HelmReleaseDetailDto?> GetHelmReleaseAsync(string clusterId, string namespaceName, string releaseName, CancellationToken ct = default);
    Task<HelmReleaseDetailDto?> GetHelmReleaseAsync(string clusterId, string namespaceName, string releaseName, int? revision, CancellationToken ct = default)
        => GetHelmReleaseAsync(clusterId, namespaceName, releaseName, ct);
    Task<List<HelmReleaseRevisionDto>> GetHelmReleaseHistoryAsync(string clusterId, string namespaceName, string releaseName, CancellationToken ct = default);
    Task<HelmOperationResultDto> InstallOrUpgradeHelmReleaseAsync(string clusterId, InstallHelmReleaseRequestDto request, CancellationToken ct = default);
    Task<HelmOperationResultDto> RollbackHelmReleaseAsync(string clusterId, string namespaceName, string releaseName, int revision, CancellationToken ct = default);
    Task<HelmOperationResultDto> UninstallHelmReleaseAsync(string clusterId, string namespaceName, string releaseName, CancellationToken ct = default);
    IReadOnlyList<HelmCatalogItemDto> GetHelmCatalog();

    // Pod Logs, Revisions & Rollbacks, Events
    Task<string> GetPodLogsAsync(string clusterId, string namespaceName, string podName, string? container = null, int? tailLines = 100, CancellationToken ct = default);
    Task<List<K8sDeploymentRevisionDto>> GetDeploymentRevisionsAsync(string clusterId, string namespaceName, string deploymentName, CancellationToken ct = default);
    Task<bool> RollbackDeploymentAsync(string clusterId, string namespaceName, string deploymentName, int revision, CancellationToken ct = default);
    Task<List<K8sEventDto>> GetClusterEventsAsync(string? clusterId, string? namespaceName = null, string? type = null, CancellationToken ct = default);
}

public class WorkloadService : IWorkloadService
{
    private readonly IAdapterConfigService _configService;
    private readonly IKubernetesClientFactory _clientFactory;
    private readonly IHelmClient? _helmClient;
    private readonly IHelmCatalogService? _catalogService;
    private readonly IImageUpdateService? _imageUpdateService;
    private readonly IHelmUpdateService? _helmUpdateService;
    private readonly ILogger<WorkloadService> _logger;

    public WorkloadService(
        IAdapterConfigService configService,
        IKubernetesClientFactory clientFactory,
        ILogger<WorkloadService> logger,
        IHelmClient? helmClient = null,
        IHelmCatalogService? catalogService = null,
        IImageUpdateService? imageUpdateService = null,
        IHelmUpdateService? helmUpdateService = null)
    {
        _configService = configService;
        _clientFactory = clientFactory;
        _logger = logger;
        _helmClient = helmClient;
        _catalogService = catalogService;
        _imageUpdateService = imageUpdateService;
        _helmUpdateService = helmUpdateService;
    }

    public Task<WorkloadAggregationResultDto> GetAggregatedWorkloadsAsync(
        string? clusterId = null,
        string? namespaceName = null,
        CancellationToken ct = default)
        => GetAggregatedWorkloadsAsync(clusterId, namespaceName, false, ct);

    public async Task<WorkloadAggregationResultDto> GetAggregatedWorkloadsAsync(
        string? clusterId,
        string? namespaceName,
        bool includeAll,
        CancellationToken ct = default)
    {
        var clusters = await _configService.GetKubernetesClustersAsync(ct);
        if (!string.IsNullOrWhiteSpace(clusterId))
        {
            clusters = clusters.Where(c => c.Id.Equals(clusterId, StringComparison.OrdinalIgnoreCase)
                                        || c.Name.Equals(clusterId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var allItems = new List<WorkloadSummaryDto>();
        var allNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clusterNames = clusters.Select(c => c.Name).Distinct().ToList();

        foreach (var cluster in clusters)
        {
            try
            {
                var adapter = await _clientFactory.CreateAdapterAsync(cluster.Id, ct);

                // Collect namespaces
                try
                {
                    var nsList = await adapter.ListNamespacesAsync(ct);
                    foreach (var ns in nsList)
                    {
                        allNamespaces.Add(ns);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to list namespaces for cluster {ClusterId}", cluster.Id);
                }

                // Collect workloads and optionally extended resources
                var workloads = await adapter.ListAllWorkloadsAsync(namespaceName, includeAll, ct);
                foreach (var w in workloads)
                {
                    allNamespaces.Add(w.Namespace);

                    string status;
                    if (w.Kind == "CronJob")
                    {
                        status = w.Suspend == true ? "ScaledDown" : "Ready";
                    }
                    else if (w.Kind is "Service" or "Ingress" or "ConfigMap" or "Secret")
                    {
                        status = "Ready";
                    }
                    else if (w.Kind == "Job")
                    {
                        status = w.ReadyReplicas > 0 ? "Ready" : w.AvailableReplicas > 0 ? "Progressing" : "Ready";
                    }
                    else if (w.DesiredReplicas == 0)
                    {
                        status = "ScaledDown";
                    }
                    else if (w.ReadyReplicas >= w.DesiredReplicas)
                    {
                        status = "Ready";
                    }
                    else if (w.ReadyReplicas == 0)
                    {
                        status = "Degraded";
                    }
                    else
                    {
                        status = "Progressing";
                    }

                    ImageUpdateInfoDto? imageUpdate = null;
                    var primaryImage = w.Images.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(primaryImage) && _imageUpdateService != null)
                    {
                        imageUpdate = _imageUpdateService.GetCached(primaryImage);
                    }

                    allItems.Add(new WorkloadSummaryDto(
                        ClusterId: cluster.Id,
                        ClusterName: cluster.Name,
                        Namespace: w.Namespace,
                        Name: w.Name,
                        DesiredReplicas: w.DesiredReplicas,
                        ReadyReplicas: w.ReadyReplicas,
                        AvailableReplicas: w.AvailableReplicas,
                        Images: w.Images,
                        CreationTimestamp: w.CreationTimestamp,
                        Status: status,
                        Kind: w.Kind,
                        IsProtected: w.IsProtected,
                        Schedule: w.Schedule,
                        LastScheduleTime: w.LastScheduleTime,
                        ImageUpdate: imageUpdate
                    ));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch workloads for cluster {ClusterId} ({ClusterName})", cluster.Id, cluster.Name);
            }
        }

        // Fire-and-forget background pre-warm for uncached images
        if (_imageUpdateService != null)
        {
            var uncachedImages = allItems
                .SelectMany(w => w.Images)
                .Where(img => !string.IsNullOrWhiteSpace(img) && _imageUpdateService.GetCached(img) == null)
                .Distinct()
                .ToList();

            if (uncachedImages.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _imageUpdateService.CheckImagesAsync(uncachedImages, false, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Background image update check prewarm failed");
                    }
                });
            }
        }

        int healthy = allItems.Count(w => w.Status == "Ready" || w.Status == "ScaledDown");

        return new WorkloadAggregationResultDto(
            Items: allItems,
            Clusters: clusterNames,
            Namespaces: allNamespaces.OrderBy(n => n).ToList(),
            TotalDeployments: allItems.Count,
            HealthyDeployments: healthy
        );
    }

    public async Task<List<K8sPodSummaryDto>> GetWorkloadPodsAsync(
        string clusterId,
        string namespaceName,
        string deploymentName,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            var pods = await adapter.ListPodsAsync(namespaceName, ct: ct);

            // Filter pods matching the workload prefix (name-...)
            var prefix = $"{deploymentName}-";
            return pods.Where(p => p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                                   p.Name.Equals(deploymentName, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch pods for workload '{Namespace}/{Deployment}' in cluster {ClusterId}",
                namespaceName, deploymentName, clusterId);
            return new List<K8sPodSummaryDto>();
        }
    }

    public Task<bool> RestartWorkloadAsync(
        string clusterId,
        string namespaceName,
        string name,
        CancellationToken ct = default)
    {
        return RestartWorkloadAsync(clusterId, namespaceName, name, "Deployment", ct);
    }

    public async Task<bool> RestartWorkloadAsync(
        string clusterId,
        string namespaceName,
        string name,
        string kind,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.RestartWorkloadAsync(kind, namespaceName, name, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart {Kind} '{Namespace}/{Name}' in cluster {ClusterId}",
                kind, namespaceName, name, clusterId);
            return false;
        }
    }

    public async Task<bool> UpdateWorkloadImageAsync(
        string clusterId,
        string namespaceName,
        string name,
        string newImage,
        string? kind = "Deployment",
        string? containerName = null,
        CancellationToken ct = default)
    {
        try
        {
            var targetKind = !string.IsNullOrWhiteSpace(kind) ? kind : "Deployment";
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.UpdateWorkloadImageAsync(targetKind, namespaceName, name, newImage, containerName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update image for {Kind} '{Namespace}/{Name}' to '{Image}' in cluster {ClusterId}",
                kind, namespaceName, name, newImage, clusterId);
            return false;
        }
    }

    public async Task<bool> RecreateWorkloadPodsAsync(
        string clusterId,
        string namespaceName,
        string name,
        string kind = "Deployment",
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.RecreateWorkloadPodsAsync(kind, namespaceName, name, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recreate pods for {Kind} '{Namespace}/{Name}' in cluster {ClusterId}",
                kind, namespaceName, name, clusterId);
            return false;
        }
    }

    public async Task<bool> ScaleWorkloadAsync(
        string clusterId,
        string namespaceName,
        string deploymentName,
        int replicas,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.ScaleDeploymentAsync(namespaceName, deploymentName, replicas, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scale deployment '{Namespace}/{Deployment}' in cluster {ClusterId}",
                namespaceName, deploymentName, clusterId);
            return false;
        }
    }

    public async Task<bool> TriggerCronJobAsync(
        string clusterId,
        string namespaceName,
        string cronJobName,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.TriggerCronJobAsync(namespaceName, cronJobName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger CronJob '{Namespace}/{Name}' in cluster {ClusterId}",
                namespaceName, cronJobName, clusterId);
            return false;
        }
    }

    public async Task<K8sAppBundleDto?> GetAppBundleAsync(
        string clusterId,
        string namespaceName,
        string appName,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.GetAppBundleAsync(namespaceName, appName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get app bundle for '{Namespace}/{Name}' in cluster {ClusterId}",
                namespaceName, appName, clusterId);
            return null;
        }
    }

    public async Task<string?> GetResourceYamlAsync(
        string clusterId,
        string namespaceName,
        string name,
        string? kind = null,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.GetResourceYamlAsync(namespaceName, name, kind, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get resource YAML for {Kind}/{Name} in namespace '{Namespace}' in cluster {ClusterId}",
                kind, name, namespaceName, clusterId);
            return null;
        }
    }

    public async Task<K8sApplyResultDto> ApplyManifestYamlAsync(
        string clusterId,
        string yamlContent,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.ApplyManifestYamlAsync(yamlContent, dryRun, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply manifest YAML in cluster {ClusterId}", clusterId);
            return new K8sApplyResultDto(false, $"Error applying manifest: {ex.Message}", new List<string>());
        }
    }

    public async Task<bool> DeleteAppBundleAsync(
        string clusterId,
        string namespaceName,
        string appName,
        K8sDeleteOptionsDto options,
        CancellationToken ct = default)
    {
        // Guardrail: if protected namespace, require typed confirmation
        if (KubernetesAdapter.ProtectedNamespaces.Contains(namespaceName) &&
            !string.Equals(options.ConfirmedName?.Trim(), appName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Blocked deletion of protected workload '{Namespace}/{Name}': confirmation mismatch ('{Confirmed}')",
                namespaceName, appName, options.ConfirmedName);
            throw new InvalidOperationException($"Deleting protected system workload in '{namespaceName}' requires exact name confirmation.");
        }

        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.DeleteAppBundleAsync(namespaceName, appName, options, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cascading delete app bundle '{Namespace}/{Name}' in cluster {ClusterId}",
                namespaceName, appName, clusterId);
            return false;
        }
    }

    public async Task<bool> DeleteNamespaceAsync(
        string clusterId,
        string namespaceName,
        string confirmedName,
        CancellationToken ct = default)
    {
        if (KubernetesAdapter.SystemCriticalNamespaces.Contains(namespaceName))
        {
            _logger.LogWarning("Blocked attempt to delete system-critical namespace '{Namespace}' in cluster {ClusterId}",
                namespaceName, clusterId);
            throw new InvalidOperationException($"Namespace '{namespaceName}' is system-critical and cannot be deleted.");
        }

        if (!string.Equals(confirmedName?.Trim(), namespaceName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Blocked namespace deletion of '{Namespace}': confirmation mismatch ('{Confirmed}')",
                namespaceName, confirmedName);
            throw new InvalidOperationException($"Deleting namespace '{namespaceName}' requires typing the exact namespace name to confirm.");
        }

        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.DeleteNamespaceAsync(namespaceName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete namespace '{Namespace}' in cluster {ClusterId}",
                namespaceName, clusterId);
            throw;
        }
    }

    private static readonly Regex NamespaceNameRegex = new(@"^[a-z0-9]([-a-z0-9]*[a-z0-9])?$", RegexOptions.Compiled);

    public async Task<bool> CreateNamespaceAsync(
        string clusterId,
        string namespaceName,
        Dictionary<string, string>? labels = null,
        Dictionary<string, string>? annotations = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(namespaceName) || namespaceName.Length > 63 || !NamespaceNameRegex.IsMatch(namespaceName))
        {
            _logger.LogWarning("Invalid namespace name provided: '{Namespace}'", namespaceName);
            throw new ArgumentException($"Invalid namespace name '{namespaceName}'. Must be 1-63 characters, contain only lowercase letters, numbers, and dashes, and start/end with an alphanumeric character.");
        }

        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.CreateNamespaceAsync(namespaceName, labels, annotations, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create namespace '{Namespace}' in cluster {ClusterId}",
                namespaceName, clusterId);
            throw;
        }
    }

    public async Task<List<K8sServiceSummaryDto>> ListServicesAsync(
        string clusterId,
        string? namespaceName = null,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.ListServicesAsync(namespaceName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Services in cluster {ClusterId}", clusterId);
            return new List<K8sServiceSummaryDto>();
        }
    }

    public async Task<K8sServiceDetailDto?> GetServiceAsync(
        string clusterId,
        string namespaceName,
        string serviceName,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.GetServiceAsync(namespaceName, serviceName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Service '{Namespace}/{Name}' in cluster {ClusterId}", namespaceName, serviceName, clusterId);
            return null;
        }
    }

    public async Task<K8sResourceOperationResultDto> UpdateServiceAsync(
        string clusterId,
        string namespaceName,
        string serviceName,
        K8sUpdateServiceRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.UpdateServiceAsync(namespaceName, serviceName, request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Service '{Namespace}/{Name}' in cluster {ClusterId}", namespaceName, serviceName, clusterId);
            return new K8sResourceOperationResultDto(false, $"Failed to update Service: {ex.Message}", serviceName);
        }
    }

    public async Task<List<K8sIngressSummaryDto>> ListIngressesAsync(
        string clusterId,
        string? namespaceName = null,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.ListIngressesAsync(namespaceName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Ingresses in cluster {ClusterId}", clusterId);
            return new List<K8sIngressSummaryDto>();
        }
    }

    public async Task<K8sIngressDetailDto?> GetIngressAsync(
        string clusterId,
        string namespaceName,
        string ingressName,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.GetIngressAsync(namespaceName, ingressName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Ingress '{Namespace}/{Name}' in cluster {ClusterId}", namespaceName, ingressName, clusterId);
            return null;
        }
    }

    public async Task<K8sResourceOperationResultDto> UpdateIngressAsync(
        string clusterId,
        string namespaceName,
        string ingressName,
        K8sUpdateIngressRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.UpdateIngressAsync(namespaceName, ingressName, request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Ingress '{Namespace}/{Name}' in cluster {ClusterId}", namespaceName, ingressName, clusterId);
            return new K8sResourceOperationResultDto(false, $"Failed to update Ingress: {ex.Message}", ingressName);
        }
    }

    public async Task<List<K8sCertificateSummaryDto>> ListCertificatesAsync(
        string clusterId,
        string? namespaceName = null,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.ListCertificatesAsync(namespaceName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list certificates in cluster {ClusterId}", clusterId);
            return new List<K8sCertificateSummaryDto>();
        }
    }

    public async Task<K8sStorageOverviewDto> GetStorageOverviewAsync(
        string clusterId,
        string? namespaceName = null,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.GetStorageOverviewAsync(namespaceName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get storage overview in cluster {ClusterId}", clusterId);
            return new K8sStorageOverviewDto(0, 0, 0, new List<K8sPvcSummaryDto>(), new List<K8sStorageClassDto>(), false);
        }
    }

    public async Task<K8sClusterVitalsDto> GetClusterVitalsAsync(
        string clusterId,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.GetClusterVitalsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cluster vitals in cluster {ClusterId}", clusterId);
            return new K8sClusterVitalsDto(false, 0, 0, 0, 0, new List<K8sNodeVitalDto>(), new List<K8sPodVitalDto>());
        }
    }

    public async Task<List<HelmReleaseSummaryDto>> ListHelmReleasesAsync(
        string clusterId,
        string? namespaceName = null,
        CancellationToken ct = default)
    {
        try
        {
            var rawCluster = await _configService.GetRawKubernetesClusterAsync(clusterId, ct);
            var kubeconfig = rawCluster?.EncryptedKubeConfig;
            var url = rawCluster?.ApiServerUrl;
            var token = rawCluster?.EncryptedToken;
            var skipTls = rawCluster?.SkipTlsVerify ?? true;

            List<HelmReleaseSummaryDto> releases;
            if (_helmClient != null)
            {
                releases = await _helmClient.ListReleasesAsync(kubeconfig, url, token, skipTls, namespaceName, ct);
            }
            else
            {
                var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
                releases = await adapter.ListHelmReleasesAsync(namespaceName, ct);
            }

            if (_helmUpdateService != null && releases.Count > 0)
            {
                var uncachedReleases = new List<HelmReleaseSummaryDto>();
                var enriched = new List<HelmReleaseSummaryDto>(releases.Count);

                foreach (var rel in releases)
                {
                    var update = _helmUpdateService.GetCached(rel.ChartName, rel.ChartVersion);
                    if (update == null)
                    {
                        uncachedReleases.Add(rel);
                    }
                    enriched.Add(rel with { UpdateInfo = update });
                }

                if (uncachedReleases.Count > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _helmUpdateService.CheckReleasesAsync(uncachedReleases, false, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Background Helm update check prewarm failed");
                        }
                    });
                }

                return enriched;
            }

            return releases;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Helm releases in cluster {ClusterId}", clusterId);
            return new List<HelmReleaseSummaryDto>();
        }
    }

    public Task<HelmReleaseDetailDto?> GetHelmReleaseAsync(
        string clusterId,
        string namespaceName,
        string releaseName,
        CancellationToken ct = default)
        => GetHelmReleaseAsync(clusterId, namespaceName, releaseName, null, ct);

    public async Task<HelmReleaseDetailDto?> GetHelmReleaseAsync(
        string clusterId,
        string namespaceName,
        string releaseName,
        int? revision,
        CancellationToken ct = default)
    {
        try
        {
            var rawCluster = await _configService.GetRawKubernetesClusterAsync(clusterId, ct);
            var kubeconfig = rawCluster?.EncryptedKubeConfig;
            var url = rawCluster?.ApiServerUrl;
            var token = rawCluster?.EncryptedToken;
            var skipTls = rawCluster?.SkipTlsVerify ?? true;

            HelmReleaseDetailDto? detail;
            if (_helmClient != null)
            {
                detail = await _helmClient.GetReleaseDetailAsync(kubeconfig, url, token, skipTls, namespaceName, releaseName, revision, ct);
            }
            else
            {
                var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
                detail = await adapter.GetHelmReleaseAsync(namespaceName, releaseName, revision, ct);
            }

            if (detail != null && _helmUpdateService != null)
            {
                var update = _helmUpdateService.GetCached(detail.ChartName, detail.ChartVersion);
                detail = detail with { UpdateInfo = update };
            }

            return detail;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Helm release '{Namespace}/{Release}' (rev: {Revision}) in cluster {ClusterId}",
                namespaceName, releaseName, revision, clusterId);
            return null;
        }
    }

    public async Task<List<HelmReleaseRevisionDto>> GetHelmReleaseHistoryAsync(
        string clusterId,
        string namespaceName,
        string releaseName,
        CancellationToken ct = default)
    {
        try
        {
            var rawCluster = await _configService.GetRawKubernetesClusterAsync(clusterId, ct);
            var kubeconfig = rawCluster?.EncryptedKubeConfig;
            var url = rawCluster?.ApiServerUrl;
            var token = rawCluster?.EncryptedToken;
            var skipTls = rawCluster?.SkipTlsVerify ?? true;

            if (_helmClient != null)
            {
                return await _helmClient.GetReleaseHistoryAsync(kubeconfig, url, token, skipTls, namespaceName, releaseName, ct);
            }

            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.GetHelmReleaseHistoryAsync(namespaceName, releaseName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Helm release history for '{Namespace}/{Release}' in cluster {ClusterId}",
                namespaceName, releaseName, clusterId);
            return new List<HelmReleaseRevisionDto>();
        }
    }

    public async Task<HelmOperationResultDto> InstallOrUpgradeHelmReleaseAsync(
        string clusterId,
        InstallHelmReleaseRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            var rawCluster = await _configService.GetRawKubernetesClusterAsync(clusterId, ct);
            var kubeconfig = rawCluster?.EncryptedKubeConfig;
            var url = rawCluster?.ApiServerUrl;
            var token = rawCluster?.EncryptedToken;
            var skipTls = rawCluster?.SkipTlsVerify ?? true;

            if (_helmClient != null)
            {
                return await _helmClient.InstallOrUpgradeReleaseAsync(kubeconfig, url, token, skipTls, request, ct);
            }

            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.InstallOrUpgradeHelmReleaseAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install/upgrade Helm release '{Release}' in cluster {ClusterId}",
                request.ReleaseName, clusterId);
            return new HelmOperationResultDto(false, $"Error installing release: {ex.Message}", request.ReleaseName);
        }
    }

    public async Task<HelmOperationResultDto> RollbackHelmReleaseAsync(
        string clusterId,
        string namespaceName,
        string releaseName,
        int revision,
        CancellationToken ct = default)
    {
        try
        {
            var rawCluster = await _configService.GetRawKubernetesClusterAsync(clusterId, ct);
            var kubeconfig = rawCluster?.EncryptedKubeConfig;
            var url = rawCluster?.ApiServerUrl;
            var token = rawCluster?.EncryptedToken;
            var skipTls = rawCluster?.SkipTlsVerify ?? true;

            if (_helmClient != null)
            {
                return await _helmClient.RollbackReleaseAsync(kubeconfig, url, token, skipTls, namespaceName, releaseName, revision, ct);
            }

            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.RollbackHelmReleaseAsync(namespaceName, releaseName, revision, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback Helm release '{Release}' in cluster {ClusterId}",
                releaseName, clusterId);
            return new HelmOperationResultDto(false, $"Error rolling back release: {ex.Message}", releaseName, revision);
        }
    }

    public async Task<HelmOperationResultDto> UninstallHelmReleaseAsync(
        string clusterId,
        string namespaceName,
        string releaseName,
        CancellationToken ct = default)
    {
        try
        {
            var rawCluster = await _configService.GetRawKubernetesClusterAsync(clusterId, ct);
            var kubeconfig = rawCluster?.EncryptedKubeConfig;
            var url = rawCluster?.ApiServerUrl;
            var token = rawCluster?.EncryptedToken;
            var skipTls = rawCluster?.SkipTlsVerify ?? true;

            if (_helmClient != null)
            {
                return await _helmClient.UninstallReleaseAsync(kubeconfig, url, token, skipTls, namespaceName, releaseName, ct);
            }

            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.UninstallHelmReleaseAsync(namespaceName, releaseName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall Helm release '{Release}' in cluster {ClusterId}",
                releaseName, clusterId);
            return new HelmOperationResultDto(false, $"Error uninstalling release: {ex.Message}", releaseName);
        }
    }

    public IReadOnlyList<HelmCatalogItemDto> GetHelmCatalog()
    {
        return _catalogService?.GetCatalog() ?? new HelmCatalogService().GetCatalog();
    }

    // --- Secrets & ConfigMaps ---

    public async Task<List<K8sSecretSummaryDto>> ListSecretsAsync(string clusterId, string? namespaceName = null, CancellationToken ct = default)
    {
        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.ListSecretsAsync(namespaceName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list secrets in cluster {ClusterId}", clusterId);
            return new List<K8sSecretSummaryDto>();
        }
    }

    public async Task<K8sSecretDetailDto?> GetSecretAsync(string clusterId, string namespaceName, string secretName, bool maskValues = true, CancellationToken ct = default)
    {
        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.GetSecretAsync(namespaceName, secretName, maskValues, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get secret {Namespace}/{Name} in cluster {ClusterId}", namespaceName, secretName, clusterId);
            return null;
        }
    }

    public async Task<K8sResourceOperationResultDto> CreateSecretAsync(string clusterId, string namespaceName, K8sCreateSecretRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.CreateSecretAsync(namespaceName, request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create secret {Namespace}/{Name} in cluster {ClusterId}", namespaceName, request.Name, clusterId);
            return new K8sResourceOperationResultDto(false, ex.Message, request.Name);
        }
    }

    public async Task<K8sResourceOperationResultDto> UpdateSecretAsync(string clusterId, string namespaceName, string secretName, K8sCreateSecretRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.UpdateSecretAsync(namespaceName, secretName, request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update secret {Namespace}/{Name} in cluster {ClusterId}", namespaceName, secretName, clusterId);
            return new K8sResourceOperationResultDto(false, ex.Message, secretName);
        }
    }

    public async Task<bool> DeleteSecretAsync(string clusterId, string namespaceName, string secretName, CancellationToken ct = default)
    {
        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.DeleteSecretAsync(namespaceName, secretName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete secret {Namespace}/{Name} in cluster {ClusterId}", namespaceName, secretName, clusterId);
            throw;
        }
    }

    public async Task<List<K8sConfigMapSummaryDto>> ListConfigMapsAsync(string clusterId, string? namespaceName = null, CancellationToken ct = default)
    {
        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.ListConfigMapsAsync(namespaceName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list config maps in cluster {ClusterId}", clusterId);
            return new List<K8sConfigMapSummaryDto>();
        }
    }

    public async Task<K8sConfigMapDetailDto?> GetConfigMapAsync(string clusterId, string namespaceName, string configMapName, CancellationToken ct = default)
    {
        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.GetConfigMapAsync(namespaceName, configMapName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get config map {Namespace}/{Name} in cluster {ClusterId}", namespaceName, configMapName, clusterId);
            return null;
        }
    }

    public async Task<K8sResourceOperationResultDto> CreateConfigMapAsync(string clusterId, string namespaceName, K8sCreateConfigMapRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.CreateConfigMapAsync(namespaceName, request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create config map {Namespace}/{Name} in cluster {ClusterId}", namespaceName, request.Name, clusterId);
            return new K8sResourceOperationResultDto(false, ex.Message, request.Name);
        }
    }

    public async Task<K8sResourceOperationResultDto> UpdateConfigMapAsync(string clusterId, string namespaceName, string configMapName, K8sCreateConfigMapRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.UpdateConfigMapAsync(namespaceName, configMapName, request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update config map {Namespace}/{Name} in cluster {ClusterId}", namespaceName, configMapName, clusterId);
            return new K8sResourceOperationResultDto(false, ex.Message, configMapName);
        }
    }

    public async Task<bool> DeleteConfigMapAsync(string clusterId, string namespaceName, string configMapName, CancellationToken ct = default)
    {
        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.DeleteConfigMapAsync(namespaceName, configMapName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete config map {Namespace}/{Name} in cluster {ClusterId}", namespaceName, configMapName, clusterId);
            throw;
        }
    }

    public async Task<string> GetPodLogsAsync(string clusterId, string namespaceName, string podName, string? container = null, int? tailLines = 100, CancellationToken ct = default)
    {
        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.GetPodLogsAsync(namespaceName, podName, container, tailLines, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get logs for pod {Namespace}/{Pod} in cluster {ClusterId}", namespaceName, podName, clusterId);
            return $"[ControlPlane] Error retrieving pod logs: {ex.Message}";
        }
    }

    public async Task<List<K8sDeploymentRevisionDto>> GetDeploymentRevisionsAsync(string clusterId, string namespaceName, string deploymentName, CancellationToken ct = default)
    {
        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.GetDeploymentRevisionsAsync(namespaceName, deploymentName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get deployment revisions for {Namespace}/{Deployment} in cluster {ClusterId}", namespaceName, deploymentName, clusterId);
            return new List<K8sDeploymentRevisionDto> { new(1, DateTime.UtcNow, new List<string>(), 1, 1, true) };
        }
    }

    public async Task<bool> RollbackDeploymentAsync(string clusterId, string namespaceName, string deploymentName, int revision, CancellationToken ct = default)
    {
        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.RollbackDeploymentAsync(namespaceName, deploymentName, revision, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback deployment {Namespace}/{Deployment} to revision {Revision} in cluster {ClusterId}", namespaceName, deploymentName, revision, clusterId);
            return false;
        }
    }

    public async Task<List<K8sEventDto>> GetClusterEventsAsync(string? clusterId, string? namespaceName = null, string? type = null, CancellationToken ct = default)
    {
        try
        {
            var effectiveClusterId = string.IsNullOrWhiteSpace(clusterId) ? null : clusterId.Trim();
            var adapter = await _clientFactory.CreateAdapterAsync(effectiveClusterId, ct);
            return await adapter.ListEventsAsync(namespaceName, type, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cluster events for cluster {ClusterId}", clusterId);
            return new List<K8sEventDto>();
        }
    }
}
