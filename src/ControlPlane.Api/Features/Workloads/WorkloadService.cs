using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Kubernetes;

namespace ControlPlane.Api.Features.Workloads;

public interface IWorkloadService
{
    Task<WorkloadAggregationResultDto> GetAggregatedWorkloadsAsync(string? clusterId = null, string? namespaceName = null, CancellationToken ct = default);
    Task<List<K8sPodSummaryDto>> GetWorkloadPodsAsync(string clusterId, string namespaceName, string deploymentName, CancellationToken ct = default);
    Task<bool> RestartWorkloadAsync(string clusterId, string namespaceName, string deploymentName, CancellationToken ct = default);
    Task<bool> ScaleWorkloadAsync(string clusterId, string namespaceName, string deploymentName, int replicas, CancellationToken ct = default);
}

public class WorkloadService : IWorkloadService
{
    private readonly IAdapterConfigService _configService;
    private readonly IKubernetesClientFactory _clientFactory;
    private readonly ILogger<WorkloadService> _logger;

    public WorkloadService(
        IAdapterConfigService configService,
        IKubernetesClientFactory clientFactory,
        ILogger<WorkloadService> logger)
    {
        _configService = configService;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task<WorkloadAggregationResultDto> GetAggregatedWorkloadsAsync(
        string? clusterId = null,
        string? namespaceName = null,
        CancellationToken ct = default)
    {
        var clusters = await _configService.GetKubernetesClustersAsync(ct);
        if (!string.IsNullOrWhiteSpace(clusterId))
        {
            clusters = clusters.Where(c => c.Id.Equals(clusterId, StringComparison.OrdinalIgnoreCase)).ToList();
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

                // Collect deployments
                var deployments = await adapter.ListDeploymentsAsync(namespaceName, ct);
                foreach (var dep in deployments)
                {
                    allNamespaces.Add(dep.Namespace);

                    string status;
                    if (dep.DesiredReplicas == 0)
                    {
                        status = "ScaledDown";
                    }
                    else if (dep.ReadyReplicas >= dep.DesiredReplicas)
                    {
                        status = "Ready";
                    }
                    else if (dep.ReadyReplicas == 0)
                    {
                        status = "Degraded";
                    }
                    else
                    {
                        status = "Progressing";
                    }

                    allItems.Add(new WorkloadSummaryDto(
                        ClusterId: cluster.Id,
                        ClusterName: cluster.Name,
                        Namespace: dep.Namespace,
                        Name: dep.Name,
                        DesiredReplicas: dep.DesiredReplicas,
                        ReadyReplicas: dep.ReadyReplicas,
                        AvailableReplicas: dep.AvailableReplicas,
                        Images: dep.Images,
                        CreationTimestamp: dep.CreationTimestamp,
                        Status: status
                    ));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch workloads for cluster {ClusterId} ({ClusterName})", cluster.Id, cluster.Name);
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

            // Filter pods matching the deployment prefix (deploymentName-...)
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

    public async Task<bool> RestartWorkloadAsync(
        string clusterId,
        string namespaceName,
        string deploymentName,
        CancellationToken ct = default)
    {
        try
        {
            var adapter = await _clientFactory.CreateAdapterAsync(clusterId, ct);
            return await adapter.RestartDeploymentAsync(namespaceName, deploymentName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart deployment '{Namespace}/{Deployment}' in cluster {ClusterId}",
                namespaceName, deploymentName, clusterId);
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
}
