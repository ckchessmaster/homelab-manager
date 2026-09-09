using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Workloads;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ControlPlane.Api.Tests;

public class WorkloadServiceTests
{
    private class FakeKubernetesAdapter : IKubernetesAdapter
    {
        public List<K8sDeploymentSummaryDto> Deployments { get; set; } = new();
        public List<string> Namespaces { get; set; } = new();
        public List<K8sPodSummaryDto> Pods { get; set; } = new();
        public bool RestartResult { get; set; } = true;
        public bool ScaleResult { get; set; } = true;

        public Task<bool> CordonNodeAsync(string nodeName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UncordonNodeAsync(string nodeName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<K8sDrainResult> DrainNodeAsync(string nodeName, TimeSpan timeout, bool ignoreDaemonSets = true, bool deleteEmptyDirData = true, CancellationToken ct = default, Func<string, Task>? onProgress = null)
            => Task.FromResult(new K8sDrainResult(nodeName, true, 0, 0, null));
        public Task<K8sNodeStatus?> GetNodeStatusAsync(string nodeName, CancellationToken ct = default) => Task.FromResult<K8sNodeStatus?>(null);
        public Task<List<K8sDiscoveredNodeDto>> ListNodesAsync(CancellationToken ct = default) => Task.FromResult(new List<K8sDiscoveredNodeDto>());
        public Task<List<string>> ListNamespacesAsync(CancellationToken ct = default) => Task.FromResult(Namespaces);
        public Task<List<K8sDeploymentSummaryDto>> ListDeploymentsAsync(string? namespaceName = null, CancellationToken ct = default)
        {
            var res = Deployments;
            if (!string.IsNullOrWhiteSpace(namespaceName))
            {
                res = res.Where(d => d.Namespace == namespaceName).ToList();
            }
            return Task.FromResult(res);
        }
        public Task<bool> RestartDeploymentAsync(string namespaceName, string deploymentName, CancellationToken ct = default) => Task.FromResult(RestartResult);
        public Task<bool> ScaleDeploymentAsync(string namespaceName, string deploymentName, int replicas, CancellationToken ct = default) => Task.FromResult(ScaleResult);
        public Task<List<K8sPodSummaryDto>> ListPodsAsync(string? namespaceName = null, string? nodeName = null, CancellationToken ct = default)
        {
            var res = Pods;
            if (!string.IsNullOrWhiteSpace(namespaceName))
            {
                res = res.Where(p => p.Namespace == namespaceName).ToList();
            }
            return Task.FromResult(res);
        }
    }

    private class FakeKubernetesClientFactory : IKubernetesClientFactory
    {
        public Dictionary<string, FakeKubernetesAdapter> Adapters { get; } = new();

        public Task<k8s.IKubernetes> CreateClientAsync(string? clusterId = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IKubernetesAdapter> CreateAdapterAsync(string? clusterId = null, CancellationToken ct = default)
        {
            var id = clusterId ?? "default";
            if (!Adapters.TryGetValue(id, out var adapter))
            {
                adapter = new FakeKubernetesAdapter();
                Adapters[id] = adapter;
            }
            return Task.FromResult<IKubernetesAdapter>(adapter);
        }
    }

    private class FakeAdapterConfigService : IAdapterConfigService
    {
        public List<KubernetesClusterDto> Clusters { get; set; } = new();

        public Task<ProxmoxConfigDto> GetProxmoxConfigAsync(CancellationToken ct = default) => Task.FromResult(new ProxmoxConfigDto("", "", "", false, false, 300, 1000, null));
        public Task<ProxmoxConfigDto> SaveProxmoxConfigAsync(SaveProxmoxConfigRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ProxmoxOptions> GetActiveProxmoxOptionsAsync(CancellationToken ct = default) => Task.FromResult(new ProxmoxOptions());
        public Task<ProxmoxOptions> GetActiveProxmoxOptionsAsync(string? instanceId, CancellationToken ct = default) => Task.FromResult(new ProxmoxOptions());

        public Task<List<ProxmoxInstanceDto>> GetProxmoxInstancesAsync(CancellationToken ct = default) => Task.FromResult(new List<ProxmoxInstanceDto>());
        public Task<ProxmoxInstanceDto?> GetProxmoxInstanceAsync(string id, CancellationToken ct = default) => Task.FromResult<ProxmoxInstanceDto?>(null);
        public Task<ProxmoxStoredInstance?> GetRawProxmoxInstanceAsync(string id, CancellationToken ct = default) => Task.FromResult<ProxmoxStoredInstance?>(null);
        public Task<ProxmoxInstanceDto> SaveProxmoxInstanceAsync(SaveProxmoxInstanceRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteProxmoxInstanceAsync(string id, CancellationToken ct = default) => Task.FromResult(true);

        public Task<List<KubernetesClusterDto>> GetKubernetesClustersAsync(CancellationToken ct = default) => Task.FromResult(Clusters);
        public Task<KubernetesClusterDto?> GetKubernetesClusterAsync(string id, CancellationToken ct = default) => Task.FromResult(Clusters.FirstOrDefault(c => c.Id == id));
        public Task<KubernetesStoredCluster?> GetRawKubernetesClusterAsync(string id, CancellationToken ct = default) => Task.FromResult<KubernetesStoredCluster?>(null);
        public Task<KubernetesClusterDto> SaveKubernetesClusterAsync(SaveKubernetesClusterRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteKubernetesClusterAsync(string id, CancellationToken ct = default) => Task.FromResult(true);

        public Task<List<UniFiInstanceDto>> GetUniFiInstancesAsync(CancellationToken ct = default) => Task.FromResult(new List<UniFiInstanceDto>());
        public Task<UniFiInstanceDto?> GetUniFiInstanceAsync(string id, CancellationToken ct = default) => Task.FromResult<UniFiInstanceDto?>(null);
        public Task<UniFiStoredInstance?> GetRawUniFiInstanceAsync(string id, CancellationToken ct = default) => Task.FromResult<UniFiStoredInstance?>(null);
        public Task<UniFiInstanceDto> SaveUniFiInstanceAsync(SaveUniFiInstanceRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteUniFiInstanceAsync(string id, CancellationToken ct = default) => Task.FromResult(true);

        public Task<List<OPNsenseInstanceDto>> GetOPNsenseInstancesAsync(CancellationToken ct = default) => Task.FromResult(new List<OPNsenseInstanceDto>());
        public Task<OPNsenseInstanceDto?> GetOPNsenseInstanceAsync(string id, CancellationToken ct = default) => Task.FromResult<OPNsenseInstanceDto?>(null);
        public Task<OPNsenseStoredInstance?> GetRawOPNsenseInstanceAsync(string id, CancellationToken ct = default) => Task.FromResult<OPNsenseStoredInstance?>(null);
        public Task<OPNsenseInstanceDto> SaveOPNsenseInstanceAsync(SaveOPNsenseInstanceRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteOPNsenseInstanceAsync(string id, CancellationToken ct = default) => Task.FromResult(true);

        public Task<List<IdracInstanceDto>> GetIdracInstancesAsync(CancellationToken ct = default) => Task.FromResult(new List<IdracInstanceDto>());
        public Task<IdracInstanceDto?> GetIdracInstanceAsync(string id, CancellationToken ct = default) => Task.FromResult<IdracInstanceDto?>(null);
        public Task<IdracStoredInstance?> GetRawIdracInstanceAsync(string id, CancellationToken ct = default) => Task.FromResult<IdracStoredInstance?>(null);
        public Task<IdracInstanceDto> SaveIdracInstanceAsync(SaveIdracInstanceRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteIdracInstanceAsync(string id, CancellationToken ct = default) => Task.FromResult(true);
    }

    [Fact]
    public async Task GetAggregatedWorkloadsAsync_AggregatesAcrossClustersAndComputesStatus()
    {
        var configService = new FakeAdapterConfigService
        {
            Clusters = new List<KubernetesClusterDto>
            {
                new("cluster-1", "Prod Cluster", "https://10.0.0.1:6443", "default", true, false, true, DateTimeOffset.UtcNow),
                new("cluster-2", "Staging Cluster", "https://10.0.0.2:6443", "default", true, false, true, DateTimeOffset.UtcNow)
            }
        };

        var factory = new FakeKubernetesClientFactory();
        var adapter1 = new FakeKubernetesAdapter
        {
            Namespaces = new List<string> { "default", "monitoring" },
            Deployments = new List<K8sDeploymentSummaryDto>
            {
                new("web-app", "default", 3, 3, 3, new List<string> { "nginx:alpine" }, DateTime.UtcNow),
                new("grafana", "monitoring", 1, 0, 0, new List<string> { "grafana/grafana:latest" }, DateTime.UtcNow)
            }
        };
        var adapter2 = new FakeKubernetesAdapter
        {
            Namespaces = new List<string> { "default", "staging" },
            Deployments = new List<K8sDeploymentSummaryDto>
            {
                new("worker", "staging", 2, 1, 1, new List<string> { "redis:alpine" }, DateTime.UtcNow),
                new("stopped-svc", "staging", 0, 0, 0, new List<string> { "busybox" }, DateTime.UtcNow)
            }
        };
        factory.Adapters["cluster-1"] = adapter1;
        factory.Adapters["cluster-2"] = adapter2;

        var service = new WorkloadService(configService, factory, NullLogger<WorkloadService>.Instance);

        var result = await service.GetAggregatedWorkloadsAsync();

        Assert.Equal(4, result.TotalDeployments);
        Assert.Equal(2, result.HealthyDeployments); // web-app (Ready) and stopped-svc (ScaledDown)
        Assert.Equal(4, result.Items.Count);

        var web = result.Items.Single(w => w.Name == "web-app");
        Assert.Equal("Ready", web.Status);
        Assert.Equal("Prod Cluster", web.ClusterName);

        var grafana = result.Items.Single(w => w.Name == "grafana");
        Assert.Equal("Degraded", grafana.Status);

        var worker = result.Items.Single(w => w.Name == "worker");
        Assert.Equal("Progressing", worker.Status);

        var stopped = result.Items.Single(w => w.Name == "stopped-svc");
        Assert.Equal("ScaledDown", stopped.Status);

        Assert.Contains("default", result.Namespaces);
        Assert.Contains("monitoring", result.Namespaces);
        Assert.Contains("staging", result.Namespaces);
    }

    [Fact]
    public async Task GetWorkloadPodsAsync_ReturnsPodsMatchingDeploymentPrefix()
    {
        var configService = new FakeAdapterConfigService();
        var factory = new FakeKubernetesClientFactory();
        var adapter = new FakeKubernetesAdapter
        {
            Pods = new List<K8sPodSummaryDto>
            {
                new("web-app-6d8b9-1111", "default", "Running", "node-1", "10.244.0.5", 0, true, DateTime.UtcNow),
                new("web-app-6d8b9-2222", "default", "Running", "node-2", "10.244.1.5", 0, true, DateTime.UtcNow),
                new("other-pod-abc", "default", "Running", "node-1", "10.244.0.6", 0, true, DateTime.UtcNow),
            }
        };
        factory.Adapters["cluster-1"] = adapter;

        var service = new WorkloadService(configService, factory, NullLogger<WorkloadService>.Instance);

        var pods = await service.GetWorkloadPodsAsync("cluster-1", "default", "web-app");

        Assert.Equal(2, pods.Count);
        Assert.All(pods, p => Assert.StartsWith("web-app-", p.Name));
    }

    [Fact]
    public async Task ScaleAndRestartWorkload_InvokesAdapterMethods()
    {
        var configService = new FakeAdapterConfigService();
        var factory = new FakeKubernetesClientFactory();
        var adapter = new FakeKubernetesAdapter();
        factory.Adapters["cluster-1"] = adapter;

        var service = new WorkloadService(configService, factory, NullLogger<WorkloadService>.Instance);

        var restartSuccess = await service.RestartWorkloadAsync("cluster-1", "default", "web-app");
        Assert.True(restartSuccess);

        var scaleSuccess = await service.ScaleWorkloadAsync("cluster-1", "default", "web-app", 5);
        Assert.True(scaleSuccess);
    }
}
