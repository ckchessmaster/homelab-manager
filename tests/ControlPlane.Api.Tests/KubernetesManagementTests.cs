using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Idrac;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Kubernetes.Helm;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Workloads;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ControlPlane.Api.Tests;

public class KubernetesManagementTests
{
    private class FakeFullKubernetesAdapter : IKubernetesAdapter
    {
        public List<K8sWorkloadItemDto> Workloads { get; set; } = new();
        public List<string> Namespaces { get; set; } = new();
        public List<K8sPodSummaryDto> Pods { get; set; } = new();
        public List<K8sIngressSummaryDto> Ingresses { get; set; } = new();
        public List<K8sCertificateSummaryDto> Certificates { get; set; } = new();
        public List<K8sSecretSummaryDto> Secrets { get; set; } = new();
        public List<K8sConfigMapSummaryDto> ConfigMaps { get; set; } = new();
        public K8sStorageOverviewDto Storage { get; set; } = new(0, 0, 0, new(), new(), false);
        public K8sClusterVitalsDto Vitals { get; set; } = new(false, 0, 0, 0, 0, new(), new());
        public K8sAppBundleDto? Bundle { get; set; }

        public string? LastRestartedKind { get; private set; }
        public string? LastRestartedName { get; private set; }
        public string? LastRecreatedName { get; private set; }
        public string? LastTriggeredCronJob { get; private set; }
        public string? LastDeletedApp { get; private set; }
        public string? LastDeletedNamespace { get; private set; }
        public string? LastCreatedNamespace { get; private set; }
        public Dictionary<string, string>? LastCreatedNamespaceLabels { get; private set; }
        public Dictionary<string, string>? LastCreatedNamespaceAnnotations { get; private set; }
        public K8sCreateSecretRequestDto? LastCreatedSecret { get; private set; }
        public string? LastDeletedSecret { get; private set; }
        public K8sCreateConfigMapRequestDto? LastCreatedConfigMap { get; private set; }
        public string? LastDeletedConfigMap { get; private set; }
        public K8sDeleteOptionsDto? LastDeleteOptions { get; private set; }
        public string? LastAppliedYaml { get; private set; }
        public bool? LastApplyDryRun { get; private set; }

        public Task<bool> CordonNodeAsync(string nodeName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UncordonNodeAsync(string nodeName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<K8sDrainResult> DrainNodeAsync(string nodeName, TimeSpan timeout, bool ignoreDaemonSets = true, bool deleteEmptyDirData = true, CancellationToken ct = default, Func<string, Task>? onProgress = null)
            => Task.FromResult(new K8sDrainResult(nodeName, true, 0, 0, null));
        public Task<K8sNodeStatus?> GetNodeStatusAsync(string nodeName, CancellationToken ct = default) => Task.FromResult<K8sNodeStatus?>(null);
        public Task<List<K8sDiscoveredNodeDto>> ListNodesAsync(CancellationToken ct = default) => Task.FromResult(new List<K8sDiscoveredNodeDto>());
        public Task<List<string>> ListNamespacesAsync(CancellationToken ct = default) => Task.FromResult(Namespaces);
        public Task<List<K8sDeploymentSummaryDto>> ListDeploymentsAsync(string? namespaceName = null, CancellationToken ct = default) => Task.FromResult(new List<K8sDeploymentSummaryDto>());
        public Task<bool> RestartDeploymentAsync(string namespaceName, string deploymentName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ScaleDeploymentAsync(string namespaceName, string deploymentName, int replicas, CancellationToken ct = default) => Task.FromResult(true);
        public Task<List<K8sPodSummaryDto>> ListPodsAsync(string? namespaceName = null, string? nodeName = null, CancellationToken ct = default) => Task.FromResult(Pods);

        public Task<List<K8sWorkloadItemDto>> ListAllWorkloadsAsync(string? namespaceName = null, CancellationToken ct = default)
        {
            var items = Workloads;
            if (!string.IsNullOrWhiteSpace(namespaceName))
            {
                items = items.Where(w => w.Namespace == namespaceName).ToList();
            }
            return Task.FromResult(items);
        }

        public Task<bool> RestartWorkloadAsync(string kind, string namespaceName, string name, CancellationToken ct = default)
        {
            LastRestartedKind = kind;
            LastRestartedName = name;
            return Task.FromResult(true);
        }

        public Task<bool> RecreateWorkloadPodsAsync(string kind, string namespaceName, string name, CancellationToken ct = default)
        {
            LastRecreatedName = name;
            return Task.FromResult(true);
        }

        public Task<bool> TriggerCronJobAsync(string namespaceName, string cronJobName, CancellationToken ct = default)
        {
            LastTriggeredCronJob = cronJobName;
            return Task.FromResult(true);
        }

        public Task<K8sAppBundleDto?> GetAppBundleAsync(string namespaceName, string appName, CancellationToken ct = default)
            => Task.FromResult(Bundle);

        public Task<K8sApplyResultDto> ApplyManifestYamlAsync(string yamlContent, bool dryRun = false, CancellationToken ct = default)
        {
            LastAppliedYaml = yamlContent;
            LastApplyDryRun = dryRun;
            return Task.FromResult(new K8sApplyResultDto(true, dryRun ? "Dry run passed" : "Applied", new List<string> { "Deployment/web" }));
        }

        public Task<bool> DeleteAppBundleAsync(string namespaceName, string appName, K8sDeleteOptionsDto options, CancellationToken ct = default)
        {
            LastDeletedApp = appName;
            LastDeleteOptions = options;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteNamespaceAsync(string namespaceName, CancellationToken ct = default)
        {
            LastDeletedNamespace = namespaceName;
            return Task.FromResult(true);
        }

        public Task<bool> CreateNamespaceAsync(string namespaceName, Dictionary<string, string>? labels = null, Dictionary<string, string>? annotations = null, CancellationToken ct = default)
        {
            LastCreatedNamespace = namespaceName;
            LastCreatedNamespaceLabels = labels;
            LastCreatedNamespaceAnnotations = annotations;
            if (!Namespaces.Contains(namespaceName))
            {
                Namespaces.Add(namespaceName);
            }
            return Task.FromResult(true);
        }

        public Task<List<K8sIngressSummaryDto>> ListIngressesAsync(string? namespaceName = null, CancellationToken ct = default)
            => Task.FromResult(Ingresses);

        public Task<List<K8sCertificateSummaryDto>> ListCertificatesAsync(string? namespaceName = null, CancellationToken ct = default)
            => Task.FromResult(Certificates);

        public Task<K8sStorageOverviewDto> GetStorageOverviewAsync(string? namespaceName = null, CancellationToken ct = default)
            => Task.FromResult(Storage);

        public Task<K8sClusterVitalsDto> GetClusterVitalsAsync(CancellationToken ct = default)
            => Task.FromResult(Vitals);

        public Task<List<K8sSecretSummaryDto>> ListSecretsAsync(string? namespaceName = null, CancellationToken ct = default)
        {
            var items = Secrets;
            if (!string.IsNullOrWhiteSpace(namespaceName)) items = items.Where(s => s.Namespace == namespaceName).ToList();
            return Task.FromResult(items);
        }

        public Task<K8sResourceOperationResultDto> CreateSecretAsync(string namespaceName, K8sCreateSecretRequestDto request, CancellationToken ct = default)
        {
            LastCreatedSecret = request;
            Secrets.Add(new K8sSecretSummaryDto(request.Name, namespaceName, request.Type, request.StringData?.Count ?? 0, request.StringData?.Keys.ToList() ?? new(), DateTime.UtcNow));
            return Task.FromResult(new K8sResourceOperationResultDto(true, "Created", request.Name));
        }

        public Task<K8sResourceOperationResultDto> UpdateSecretAsync(string namespaceName, string secretName, K8sCreateSecretRequestDto request, CancellationToken ct = default)
        {
            LastCreatedSecret = request;
            Secrets.RemoveAll(s => s.Namespace == namespaceName && s.Name == secretName);
            Secrets.Add(new K8sSecretSummaryDto(secretName, namespaceName, request.Type, request.StringData?.Count ?? 0, request.StringData?.Keys.ToList() ?? new(), DateTime.UtcNow));
            return Task.FromResult(new K8sResourceOperationResultDto(true, "Updated", secretName));
        }

        public Task<bool> DeleteSecretAsync(string namespaceName, string secretName, CancellationToken ct = default)
        {
            LastDeletedSecret = secretName;
            Secrets.RemoveAll(s => s.Namespace == namespaceName && s.Name == secretName);
            return Task.FromResult(true);
        }

        public Task<List<K8sConfigMapSummaryDto>> ListConfigMapsAsync(string? namespaceName = null, CancellationToken ct = default)
        {
            var items = ConfigMaps;
            if (!string.IsNullOrWhiteSpace(namespaceName)) items = items.Where(c => c.Namespace == namespaceName).ToList();
            return Task.FromResult(items);
        }

        public Task<K8sResourceOperationResultDto> CreateConfigMapAsync(string namespaceName, K8sCreateConfigMapRequestDto request, CancellationToken ct = default)
        {
            LastCreatedConfigMap = request;
            ConfigMaps.Add(new K8sConfigMapSummaryDto(request.Name, namespaceName, request.Data?.Count ?? 0, request.Data?.Keys.ToList() ?? new(), DateTime.UtcNow));
            return Task.FromResult(new K8sResourceOperationResultDto(true, "Created", request.Name));
        }

        public Task<K8sResourceOperationResultDto> UpdateConfigMapAsync(string namespaceName, string configMapName, K8sCreateConfigMapRequestDto request, CancellationToken ct = default)
        {
            LastCreatedConfigMap = request;
            ConfigMaps.RemoveAll(c => c.Namespace == namespaceName && c.Name == configMapName);
            ConfigMaps.Add(new K8sConfigMapSummaryDto(configMapName, namespaceName, request.Data?.Count ?? 0, request.Data?.Keys.ToList() ?? new(), DateTime.UtcNow));
            return Task.FromResult(new K8sResourceOperationResultDto(true, "Updated", configMapName));
        }

        public Task<bool> DeleteConfigMapAsync(string namespaceName, string configMapName, CancellationToken ct = default)
        {
            LastDeletedConfigMap = configMapName;
            ConfigMaps.RemoveAll(c => c.Namespace == namespaceName && c.Name == configMapName);
            return Task.FromResult(true);
        }
    }

    private class FakeFactory : IKubernetesClientFactory
    {
        public Dictionary<string, FakeFullKubernetesAdapter> Adapters { get; } = new();

        public Task<k8s.IKubernetes> CreateClientAsync(string? clusterId = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IKubernetesAdapter> CreateAdapterAsync(string? clusterId = null, CancellationToken ct = default)
        {
            var id = clusterId ?? "cluster-1";
            if (!Adapters.TryGetValue(id, out var adapter))
            {
                adapter = new FakeFullKubernetesAdapter();
                Adapters[id] = adapter;
            }
            return Task.FromResult<IKubernetesAdapter>(adapter);
        }
    }

    private class FakeConfigService : IAdapterConfigService
    {
        public List<KubernetesClusterDto> Clusters { get; set; } = new()
        {
            new("cluster-1", "Homelab K8s", "https://192.168.1.100:6443", "default", true, false, true, DateTimeOffset.UtcNow)
        };

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
    public async Task GetAggregatedWorkloads_ReturnsAllWorkloadKindsAndProtectedStatus()
    {
        var configService = new FakeConfigService();
        var factory = new FakeFactory();
        var adapter = new FakeFullKubernetesAdapter
        {
            Workloads = new List<K8sWorkloadItemDto>
            {
                new("nginx-ingress-controller", "ingress-nginx", "Deployment", 2, 2, 2, new() { "nginx/ingress:1.0" }, DateTime.UtcNow, null, null, null, true),
                new("postgres", "database", "StatefulSet", 1, 1, 1, new() { "postgres:16" }, DateTime.UtcNow, null, null, null, false),
                new("cilium-node", "kube-system", "DaemonSet", 3, 3, 3, new() { "cilium/cilium:1.15" }, DateTime.UtcNow, null, null, null, true),
                new("backup-cron", "default", "CronJob", 0, 0, 0, new() { "restic:latest" }, DateTime.UtcNow, "0 2 * * *", false, DateTime.UtcNow.AddHours(-3), false),
            },
            Namespaces = new() { "ingress-nginx", "database", "kube-system", "default" }
        };
        factory.Adapters["cluster-1"] = adapter;

        var service = new WorkloadService(configService, factory, NullLogger<WorkloadService>.Instance);
        var result = await service.GetAggregatedWorkloadsAsync("cluster-1");

        Assert.Equal(4, result.TotalDeployments);
        Assert.Equal(4, result.Items.Count);

        var ingress = result.Items.Single(w => w.Name == "nginx-ingress-controller");
        Assert.True(ingress.IsProtected);
        Assert.Equal("Deployment", ingress.Kind);

        var db = result.Items.Single(w => w.Name == "postgres");
        Assert.False(db.IsProtected);
        Assert.Equal("StatefulSet", db.Kind);

        var cj = result.Items.Single(w => w.Name == "backup-cron");
        Assert.Equal("CronJob", cj.Kind);
        Assert.Equal("0 2 * * *", cj.Schedule);
    }

    [Fact]
    public async Task WorkloadRestartsAndCronTrigger_RouteToAdapter()
    {
        var configService = new FakeConfigService();
        var factory = new FakeFactory();
        var adapter = new FakeFullKubernetesAdapter();
        factory.Adapters["cluster-1"] = adapter;

        var service = new WorkloadService(configService, factory, NullLogger<WorkloadService>.Instance);

        // StatefulSet restart
        var restarted = await service.RestartWorkloadAsync("cluster-1", "database", "postgres", "StatefulSet");
        Assert.True(restarted);
        Assert.Equal("StatefulSet", adapter.LastRestartedKind);
        Assert.Equal("postgres", adapter.LastRestartedName);

        // Pod recreate
        var recreated = await service.RecreateWorkloadPodsAsync("cluster-1", "default", "web-app", "Deployment");
        Assert.True(recreated);
        Assert.Equal("web-app", adapter.LastRecreatedName);

        // CronJob trigger
        var triggered = await service.TriggerCronJobAsync("cluster-1", "default", "nightly-backup");
        Assert.True(triggered);
        Assert.Equal("nightly-backup", adapter.LastTriggeredCronJob);
    }

    [Fact]
    public async Task DeleteAppBundle_EnforcesProtectionConfirmation()
    {
        var configService = new FakeConfigService();
        var factory = new FakeFactory();
        var adapter = new FakeFullKubernetesAdapter();
        factory.Adapters["cluster-1"] = adapter;

        var service = new WorkloadService(configService, factory, NullLogger<WorkloadService>.Instance);

        // In protected namespace (e.g. kube-system) without exact confirmed name: must throw
        var unconfirmedOptions = new K8sDeleteOptionsDto(true, true, true, false, ConfirmedName: "wrong-name");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAppBundleAsync("cluster-1", "kube-system", "coredns", unconfirmedOptions));

        // With exact confirmed name: succeeds
        var confirmedOptions = new K8sDeleteOptionsDto(true, true, true, false, ConfirmedName: "coredns");
        var success = await service.DeleteAppBundleAsync("cluster-1", "kube-system", "coredns", confirmedOptions);
        Assert.True(success);
        Assert.Equal("coredns", adapter.LastDeletedApp);
        Assert.False(adapter.LastDeleteOptions?.DeletePvc);
    }

    [Fact]
    public async Task StorageAndVitals_ReturnExpectedData()
    {
        var configService = new FakeConfigService();
        var factory = new FakeFactory();
        var adapter = new FakeFullKubernetesAdapter
        {
            Storage = new K8sStorageOverviewDto(
                TotalPvcs: 2,
                BoundPvcs: 2,
                TotalCapacityBytes: 20 * 1024L * 1024L * 1024L,
                Pvcs: new List<K8sPvcSummaryDto>
                {
                    new("data-pvc", "default", "Bound", "pvc-vol-1", "10Gi", "longhorn", new() { "ReadWriteOnce" }, new() { "web-pod-1" }, DateTime.UtcNow)
                },
                StorageClasses: new List<K8sStorageClassDto>
                {
                    new("longhorn", "driver.longhorn.io", "Delete", "Immediate", true)
                },
                LonghornDetected: true
            ),
            Vitals = new K8sClusterVitalsDto(
                MetricsServerAvailable: true,
                TotalCpuUsageMillis: 2400,
                TotalCpuAllocatableMillis: 8000,
                TotalMemoryUsageBytes: 16 * 1024L * 1024L * 1024L,
                TotalMemoryAllocatableBytes: 32 * 1024L * 1024L * 1024L,
                Nodes: new List<K8sNodeVitalDto>
                {
                    new("k8s-node-1", 1200, 4000, 8000000000, 16000000000, false, false, false, true)
                },
                TopPods: new List<K8sPodVitalDto>
                {
                    new("heavy-pod", "default", 850, 4000000000)
                }
            )
        };
        factory.Adapters["cluster-1"] = adapter;

        var service = new WorkloadService(configService, factory, NullLogger<WorkloadService>.Instance);

        var storage = await service.GetStorageOverviewAsync("cluster-1");
        Assert.True(storage.LonghornDetected);
        Assert.Equal(2, storage.TotalPvcs);
        Assert.Single(storage.Pvcs);
        Assert.Equal("data-pvc", storage.Pvcs[0].Name);

        var vitals = await service.GetClusterVitalsAsync("cluster-1");
        Assert.True(vitals.MetricsServerAvailable);
        Assert.Equal(2400, vitals.TotalCpuUsageMillis);
        Assert.Single(vitals.Nodes);
        Assert.Single(vitals.TopPods);
    }

    [Fact]
    public async Task DeleteNamespace_SystemCritical_ThrowsInvalidOperationException()
    {
        var configService = new FakeConfigService();
        var factory = new FakeFactory();
        factory.Adapters["cluster-1"] = new FakeFullKubernetesAdapter();

        var service = new WorkloadService(configService, factory, NullLogger<WorkloadService>.Instance);

        // Test kube-system
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteNamespaceAsync("cluster-1", "kube-system", "kube-system"));
        Assert.Contains("system-critical", ex1.Message);

        // Test default
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteNamespaceAsync("cluster-1", "default", "default"));
        Assert.Contains("system-critical", ex2.Message);

        // Test longhorn-system
        var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteNamespaceAsync("cluster-1", "longhorn-system", "longhorn-system"));
        Assert.Contains("system-critical", ex3.Message);
    }

    [Fact]
    public async Task DeleteNamespace_MismatchedConfirmation_ThrowsInvalidOperationException()
    {
        var configService = new FakeConfigService();
        var factory = new FakeFactory();
        factory.Adapters["cluster-1"] = new FakeFullKubernetesAdapter();

        var service = new WorkloadService(configService, factory, NullLogger<WorkloadService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteNamespaceAsync("cluster-1", "my-app-ns", "wrong-name"));
        Assert.Contains("requires typing the exact namespace name", ex.Message);
    }

    [Fact]
    public async Task DeleteNamespace_ValidConfirmation_CallsAdapter()
    {
        var configService = new FakeConfigService();
        var factory = new FakeFactory();
        var adapter = new FakeFullKubernetesAdapter();
        factory.Adapters["cluster-1"] = adapter;

        var service = new WorkloadService(configService, factory, NullLogger<WorkloadService>.Instance);

        var result = await service.DeleteNamespaceAsync("cluster-1", "my-app-ns", "my-app-ns");
        Assert.True(result);
        Assert.Equal("my-app-ns", adapter.LastDeletedNamespace);
    }

    [Fact]
    public async Task CreateNamespace_ValidName_CallsAdapter()
    {
        var configService = new FakeConfigService();
        var factory = new FakeFactory();
        var adapter = new FakeFullKubernetesAdapter();
        factory.Adapters["cluster-1"] = adapter;

        var service = new WorkloadService(configService, factory, NullLogger<WorkloadService>.Instance);

        var labels = new Dictionary<string, string> { { "environment", "production" } };
        var result = await service.CreateNamespaceAsync("cluster-1", "app-prod", labels);

        Assert.True(result);
        Assert.Equal("app-prod", adapter.LastCreatedNamespace);
        Assert.Equal("production", adapter.LastCreatedNamespaceLabels?["environment"]);
        Assert.Contains("app-prod", adapter.Namespaces);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("InvalidUpperCase")]
    [InlineData("-starts-with-dash")]
    [InlineData("ends-with-dash-")]
    [InlineData("contains_underscore")]
    [InlineData("special!char")]
    [InlineData("this-namespace-name-is-way-too-long-because-it-exceeds-the-maximum-allowed-length-of-63-characters-in-kubernetes")]
    public async Task CreateNamespace_InvalidName_ThrowsArgumentException(string invalidName)
    {
        var configService = new FakeConfigService();
        var factory = new FakeFactory();
        factory.Adapters["cluster-1"] = new FakeFullKubernetesAdapter();

        var service = new WorkloadService(configService, factory, NullLogger<WorkloadService>.Instance);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateNamespaceAsync("cluster-1", invalidName));
        Assert.Contains("Invalid namespace name", ex.Message);
    }

    private class FakeHelmClient : IHelmClient
    {
        public List<HelmReleaseSummaryDto> Releases { get; set; } = new();
        public HelmReleaseDetailDto? Detail { get; set; }
        public List<HelmReleaseRevisionDto> History { get; set; } = new();
        public InstallHelmReleaseRequestDto? LastInstallRequest { get; private set; }
        public (string? Namespace, string? Name, int? Revision)? LastRollback { get; private set; }
        public (string? Namespace, string? Name)? LastUninstall { get; private set; }

        public Task<List<HelmReleaseSummaryDto>> ListReleasesAsync(
            string? kubeconfigYaml,
            string? apiServerUrl,
            string? token,
            bool skipTlsVerify,
            string? namespaceName = null,
            CancellationToken ct = default)
        {
            var result = Releases;
            if (!string.IsNullOrWhiteSpace(namespaceName))
            {
                result = result.Where(r => r.Namespace == namespaceName).ToList();
            }
            return Task.FromResult(result);
        }

        public Task<HelmReleaseDetailDto?> GetReleaseDetailAsync(
            string? kubeconfigYaml,
            string? apiServerUrl,
            string? token,
            bool skipTlsVerify,
            string namespaceName,
            string releaseName,
            CancellationToken ct = default)
            => Task.FromResult(Detail);

        public Task<List<HelmReleaseRevisionDto>> GetReleaseHistoryAsync(
            string? kubeconfigYaml,
            string? apiServerUrl,
            string? token,
            bool skipTlsVerify,
            string namespaceName,
            string releaseName,
            CancellationToken ct = default)
            => Task.FromResult(History);

        public Task<HelmOperationResultDto> InstallOrUpgradeReleaseAsync(
            string? kubeconfigYaml,
            string? apiServerUrl,
            string? token,
            bool skipTlsVerify,
            InstallHelmReleaseRequestDto request,
            CancellationToken ct = default)
        {
            LastInstallRequest = request;
            return Task.FromResult(new HelmOperationResultDto(true, $"Release {request.ReleaseName} deployed", request.ReleaseName));
        }

        public Task<HelmOperationResultDto> RollbackReleaseAsync(
            string? kubeconfigYaml,
            string? apiServerUrl,
            string? token,
            bool skipTlsVerify,
            string namespaceName,
            string releaseName,
            int revision,
            CancellationToken ct = default)
        {
            LastRollback = (namespaceName, releaseName, revision);
            return Task.FromResult(new HelmOperationResultDto(true, $"Release {releaseName} rolled back to {revision}", releaseName, revision));
        }

        public Task<HelmOperationResultDto> UninstallReleaseAsync(
            string? kubeconfigYaml,
            string? apiServerUrl,
            string? token,
            bool skipTlsVerify,
            string namespaceName,
            string releaseName,
            CancellationToken ct = default)
        {
            LastUninstall = (namespaceName, releaseName);
            return Task.FromResult(new HelmOperationResultDto(true, $"Release {releaseName} uninstalled", releaseName));
        }
    }

    [Fact]
    public async Task ListHelmReleases_ReturnsReleasesAndFiltersByNamespace()
    {
        var configService = new FakeConfigService();
        var factory = new FakeFactory();
        var helmClient = new FakeHelmClient
        {
            Releases = new List<HelmReleaseSummaryDto>
            {
                new("ingress-nginx", "ingress-nginx", 1, DateTimeOffset.UtcNow, "deployed", "ingress-nginx-4.11.2", "ingress-nginx", "4.11.2", "1.11.2"),
                new("cert-manager", "cert-manager", 2, DateTimeOffset.UtcNow, "deployed", "cert-manager-v1.16.0", "cert-manager", "v1.16.0", "v1.16.0"),
                new("pihole", "pihole", 1, DateTimeOffset.UtcNow, "deployed", "pihole-2.28.0", "pihole", "2.28.0", "2024.07.0")
            }
        };

        var service = new WorkloadService(
            configService,
            factory,
            NullLogger<WorkloadService>.Instance,
            helmClient,
            new HelmCatalogService()
        );

        // All namespaces
        var all = await service.ListHelmReleasesAsync("cluster-1");
        Assert.Equal(3, all.Count);

        // Filtered namespace
        var filtered = await service.ListHelmReleasesAsync("cluster-1", "ingress-nginx");
        Assert.Single(filtered);
        Assert.Equal("ingress-nginx", filtered[0].Name);
    }

    [Fact]
    public async Task GetHelmRelease_ReturnsDetailsAndValues()
    {
        var configService = new FakeConfigService();
        var factory = new FakeFactory();
        var helmClient = new FakeHelmClient
        {
            Detail = new HelmReleaseDetailDto(
                Name: "ingress-nginx",
                Namespace: "ingress-nginx",
                Revision: 1,
                Updated: DateTimeOffset.UtcNow,
                Status: "deployed",
                Chart: "ingress-nginx-4.11.2",
                ChartName: "ingress-nginx",
                ChartVersion: "4.11.2",
                AppVersion: "1.11.2",
                ValuesYaml: "controller:\n  service:\n    type: LoadBalancer",
                Manifest: "apiVersion: apps/v1\nkind: Deployment\n..."
            )
        };

        var service = new WorkloadService(
            configService,
            factory,
            NullLogger<WorkloadService>.Instance,
            helmClient,
            new HelmCatalogService()
        );

        var detail = await service.GetHelmReleaseAsync("cluster-1", "ingress-nginx", "ingress-nginx");
        Assert.NotNull(detail);
        Assert.Equal("ingress-nginx", detail.Name);
        Assert.Contains("LoadBalancer", detail.ValuesYaml);
        Assert.Contains("Deployment", detail.Manifest);
    }

    [Fact]
    public async Task InstallOrUpgradeHelmRelease_InvokesClientAndReturnsSuccess()
    {
        var configService = new FakeConfigService();
        var factory = new FakeFactory();
        var helmClient = new FakeHelmClient();

        var service = new WorkloadService(
            configService,
            factory,
            NullLogger<WorkloadService>.Instance,
            helmClient,
            new HelmCatalogService()
        );

        var request = new InstallHelmReleaseRequestDto(
            ReleaseName: "longhorn",
            Namespace: "longhorn-system",
            ChartName: "longhorn",
            RepoUrl: "https://charts.longhorn.io",
            Version: "1.7.0",
            ValuesYaml: "persistence:\n  defaultClassReplicaCount: 2",
            CreateNamespace: true
        );

        var result = await service.InstallOrUpgradeHelmReleaseAsync("cluster-1", request);
        Assert.True(result.Success);
        Assert.Equal("longhorn", result.ReleaseName);
        Assert.NotNull(helmClient.LastInstallRequest);
        Assert.Equal("https://charts.longhorn.io", helmClient.LastInstallRequest.RepoUrl);
        Assert.Equal("longhorn-system", helmClient.LastInstallRequest.Namespace);
    }

    [Fact]
    public async Task RollbackAndUninstallHelmRelease_InvokeClient()
    {
        var configService = new FakeConfigService();
        var factory = new FakeFactory();
        var helmClient = new FakeHelmClient();

        var service = new WorkloadService(
            configService,
            factory,
            NullLogger<WorkloadService>.Instance,
            helmClient,
            new HelmCatalogService()
        );

        // Rollback
        var rollbackResult = await service.RollbackHelmReleaseAsync("cluster-1", "cert-manager", "cert-manager", 1);
        Assert.True(rollbackResult.Success);
        Assert.Equal(("cert-manager", "cert-manager", 1), helmClient.LastRollback);

        // Uninstall
        var uninstallResult = await service.UninstallHelmReleaseAsync("cluster-1", "cert-manager", "cert-manager");
        Assert.True(uninstallResult.Success);
        Assert.Equal(("cert-manager", "cert-manager"), helmClient.LastUninstall);
    }

    [Fact]
    public void GetHelmCatalog_ReturnsCuratedHomelabCharts()
    {
        var catalogService = new HelmCatalogService();
        var catalog = catalogService.GetCatalog();

        Assert.NotEmpty(catalog);
        Assert.Contains(catalog, c => c.Id == "ingress-nginx");
        Assert.Contains(catalog, c => c.Id == "cert-manager");
        Assert.Contains(catalog, c => c.Id == "longhorn");
        Assert.Contains(catalog, c => c.Id == "kube-prometheus-stack");
        Assert.Contains(catalog, c => c.Id == "pihole");

        var ingress = catalogService.GetCatalogItem("ingress-nginx");
        Assert.NotNull(ingress);
        Assert.Equal("Networking", ingress.Category);
        Assert.Contains("https://kubernetes.github.io/ingress-nginx", ingress.RepoUrl);
    }

    [Fact]
    public async Task CreateSecretAsync_CreatesSecretInNamespace()
    {
        var config = new FakeConfigService();
        var factory = new FakeFactory();
        var service = new WorkloadService(config, factory, NullLogger<WorkloadService>.Instance);

        var req = new K8sCreateSecretRequestDto(
            Name: "db-credentials",
            Namespace: "production",
            Type: "Opaque",
            StringData: new Dictionary<string, string>
            {
                ["DB_USER"] = "postgres",
                ["DB_PASS"] = "s3cr3tP@ssword"
            }
        );

        var res = await service.CreateSecretAsync("cluster-1", "production", req);

        Assert.True(res.Success);
        var secrets = await service.ListSecretsAsync("cluster-1", "production");
        Assert.Single(secrets);
        Assert.Equal("db-credentials", secrets[0].Name);
        Assert.Equal("production", secrets[0].Namespace);
        Assert.Equal(2, secrets[0].KeysCount);
        Assert.Contains("DB_USER", secrets[0].Keys);
        Assert.Contains("DB_PASS", secrets[0].Keys);
    }

    [Fact]
    public async Task DeleteSecretAsync_RemovesSecret()
    {
        var config = new FakeConfigService();
        var factory = new FakeFactory();
        var service = new WorkloadService(config, factory, NullLogger<WorkloadService>.Instance);

        var req = new K8sCreateSecretRequestDto(
            Name: "temp-secret",
            Namespace: "default",
            Type: "Opaque",
            StringData: new Dictionary<string, string> { ["API_KEY"] = "xyz" }
        );
        await service.CreateSecretAsync("cluster-1", "default", req);

        var success = await service.DeleteSecretAsync("cluster-1", "default", "temp-secret");

        Assert.True(success);
        var secrets = await service.ListSecretsAsync("cluster-1", "default");
        Assert.Empty(secrets);
    }

    [Fact]
    public async Task CreateConfigMapAsync_CreatesAndListsConfigMap()
    {
        var config = new FakeConfigService();
        var factory = new FakeFactory();
        var service = new WorkloadService(config, factory, NullLogger<WorkloadService>.Instance);

        var req = new K8sCreateConfigMapRequestDto(
            Name: "app-config",
            Namespace: "default",
            Data: new Dictionary<string, string>
            {
                ["APP_ENV"] = "production",
                ["FEATURE_FLAGS"] = "{\"flagA\": true}"
            }
        );

        var res = await service.CreateConfigMapAsync("cluster-1", "default", req);

        Assert.True(res.Success);
        var cms = await service.ListConfigMapsAsync("cluster-1", "default");
        Assert.Single(cms);
        Assert.Equal("app-config", cms[0].Name);
        Assert.Equal(2, cms[0].KeysCount);

        var delSuccess = await service.DeleteConfigMapAsync("cluster-1", "default", "app-config");
        Assert.True(delSuccess);
        var cmsAfter = await service.ListConfigMapsAsync("cluster-1", "default");
        Assert.Empty(cmsAfter);
    }

    [Fact]
    public async Task UpdateSecretAsync_UpdatesExistingSecret()
    {
        var config = new FakeConfigService();
        var factory = new FakeFactory();
        var service = new WorkloadService(config, factory, NullLogger<WorkloadService>.Instance);

        var initialReq = new K8sCreateSecretRequestDto(
            Name: "db-secret",
            Namespace: "zitadel",
            Type: "Opaque",
            StringData: new Dictionary<string, string>
            {
                ["dsn"] = "postgresql://zitadel:old@postgres.database:5432/zitadel"
            }
        );
        await service.CreateSecretAsync("cluster-1", "zitadel", initialReq);

        var updateReq = new K8sCreateSecretRequestDto(
            Name: "db-secret",
            Namespace: "zitadel",
            Type: "Opaque",
            StringData: new Dictionary<string, string>
            {
                ["dsn"] = "postgresql://zitadel:new@postgres-rw.cnpg-services:5432/zitadel",
                ["extra"] = "value"
            }
        );

        var updateRes = await service.UpdateSecretAsync("cluster-1", "zitadel", "db-secret", updateReq);

        Assert.True(updateRes.Success);
        var secrets = await service.ListSecretsAsync("cluster-1", "zitadel");
        Assert.Single(secrets);
        Assert.Equal("db-secret", secrets[0].Name);
        Assert.Equal(2, secrets[0].KeysCount);
    }

    [Fact]
    public async Task UpdateConfigMapAsync_UpdatesExistingConfigMap()
    {
        var config = new FakeConfigService();
        var factory = new FakeFactory();
        var service = new WorkloadService(config, factory, NullLogger<WorkloadService>.Instance);

        var initialReq = new K8sCreateConfigMapRequestDto(
            Name: "ui-config",
            Namespace: "default",
            Data: new Dictionary<string, string>
            {
                ["theme"] = "dark"
            }
        );
        await service.CreateConfigMapAsync("cluster-1", "default", initialReq);

        var updateReq = new K8sCreateConfigMapRequestDto(
            Name: "ui-config",
            Namespace: "default",
            Data: new Dictionary<string, string>
            {
                ["theme"] = "system",
                ["locale"] = "en-US"
            }
        );

        var updateRes = await service.UpdateConfigMapAsync("cluster-1", "default", "ui-config", updateReq);

        Assert.True(updateRes.Success);
        var cms = await service.ListConfigMapsAsync("cluster-1", "default");
        Assert.Single(cms);
        Assert.Equal("ui-config", cms[0].Name);
        Assert.Equal(2, cms[0].KeysCount);
    }
}
