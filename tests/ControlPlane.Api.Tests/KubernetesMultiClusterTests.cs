using System.Net;
using System.Text;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Discovery;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Features.Security;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using k8s;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ControlPlane.Api.Tests;

public class KubernetesMultiClusterTests
{
    private static (ControlPlaneDbContext Db, SqliteConnection Conn) CreateTestDbContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseSqlite(conn)
            .UseSnakeCaseNamingConvention()
            .Options;
        var db = new ControlPlaneDbContext(options);
        db.Database.EnsureCreated();
        return (db, conn);
    }

    private static ISecretEncryptionService CreateEncryptionService()
    {
        var key = Encoding.UTF8.GetBytes("12345678901234567890123456789012");
        return new SecretEncryptionService(new TestKeyProvider(key));
    }

    private class TestKeyProvider : ISecurityKeyProvider
    {
        private readonly byte[] _key;
        public TestKeyProvider(byte[] key) => _key = key;
        public byte[] GetMasterKey() => (byte[])_key.Clone();
        public string KeySource => "Test";
        public string? KeyFilePath => null;
    }

    private class FakeProxmoxClient : IProxmoxClient
    {
        public List<ProxmoxClusterResourceDto> Resources { get; set; } = new();
        public List<ProxmoxNodeDto> Nodes { get; set; } = new();
        public Dictionary<(string, int), string> GuestIps { get; set; } = new();

        public Task<List<ProxmoxClusterResourceDto>> DiscoverClusterResourcesAsync(CancellationToken ct = default)
            => Task.FromResult(Resources);

        public Task<List<ProxmoxNodeDto>> ListNodesAsync(CancellationToken ct = default)
            => Task.FromResult(Nodes);

        public Task<string?> TryGetGuestIpAddressAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default)
        {
            if (GuestIps.TryGetValue((node, vmid), out var ip)) return Task.FromResult<string?>(ip);
            return Task.FromResult<string?>(null);
        }

        public Dictionary<(string, int), string> GuestOsTypes { get; set; } = new();

        public Task<string?> TryGetGuestOsTypeAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default)
        {
            if (GuestOsTypes.TryGetValue((node, vmid), out var os)) return Task.FromResult<string?>(os);
            return Task.FromResult<string?>(null);
        }

        public Task<string> CreateVmSnapshotAsync(string node, int vmid, string snapName, string? description = null, bool isLxc = false, CancellationToken ct = default) => Task.FromResult("UPID:pve:001");
        public Task<string> RollbackVmSnapshotAsync(string node, int vmid, string snapName, bool isLxc = false, CancellationToken ct = default) => Task.FromResult("UPID:pve:002");
        public Task<string> DeleteVmSnapshotAsync(string node, int vmid, string snapName, bool isLxc = false, CancellationToken ct = default) => Task.FromResult("UPID:pve:003");
        public Task<List<ProxmoxSnapshotItem>> ListVmSnapshotsAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) => Task.FromResult(new List<ProxmoxSnapshotItem>());
        public Task<ProxmoxTaskStatus> GetTaskStatusAsync(string node, string upid, CancellationToken ct = default) => Task.FromResult(new ProxmoxTaskStatus("stopped", "OK"));
        public Task<ProxmoxTaskStatus> PollTaskCompletionAsync(string node, string upid, TimeSpan? timeout = null, CancellationToken ct = default) => Task.FromResult(new ProxmoxTaskStatus("stopped", "OK"));
        public Task<bool> HasSnapshotFeatureAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> HasVmAuditPermissionAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private class FakeKubernetesAdapter : IKubernetesAdapter
    {
        public List<K8sDiscoveredNodeDto> NodesToReturn { get; set; } = new();

        public Task<bool> CordonNodeAsync(string nodeName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UncordonNodeAsync(string nodeName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<K8sDrainResult> DrainNodeAsync(string nodeName, TimeSpan timeout, bool ignoreDaemonSets = true, bool deleteEmptyDirData = true, CancellationToken ct = default) =>
            Task.FromResult(new K8sDrainResult(nodeName, true, 1, 0, null));
        public Task<K8sNodeStatus?> GetNodeStatusAsync(string nodeName, CancellationToken ct = default) =>
            Task.FromResult<K8sNodeStatus?>(new K8sNodeStatus(nodeName, true, false, "10.0.0.1", 5));
        public Task<List<K8sDiscoveredNodeDto>> ListNodesAsync(CancellationToken ct = default) =>
            Task.FromResult(NodesToReturn);
        public Task<KubernetesClusterTestResultDto> TestConnectionAsync(CancellationToken ct = default) =>
            Task.FromResult(new KubernetesClusterTestResultDto(true, "v1.31.0", NodesToReturn.Count, 15, "OK"));
        public Task<List<string>> ListNamespacesAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<string> { "default", "kube-system" });
        public Task<List<K8sDeploymentSummaryDto>> ListDeploymentsAsync(string? namespaceName = null, CancellationToken ct = default) =>
            Task.FromResult(new List<K8sDeploymentSummaryDto>());
        public Task<bool> RestartDeploymentAsync(string namespaceName, string deploymentName, CancellationToken ct = default) =>
            Task.FromResult(true);
        public Task<bool> ScaleDeploymentAsync(string namespaceName, string deploymentName, int replicas, CancellationToken ct = default) =>
            Task.FromResult(true);
        public Task<List<K8sPodSummaryDto>> ListPodsAsync(string? namespaceName = null, string? nodeName = null, CancellationToken ct = default) =>
            Task.FromResult(new List<K8sPodSummaryDto>());
    }

    private class FakeKubernetesClientFactory : IKubernetesClientFactory
    {
        public Dictionary<string, FakeKubernetesAdapter> Adapters { get; } = new();

        public Task<IKubernetes> CreateClientAsync(string? clusterId = null, CancellationToken ct = default) =>
            Task.FromResult<IKubernetes>(new Kubernetes(new KubernetesClientConfiguration { Host = "http://localhost:8080" }));

        public Task<IKubernetesAdapter> CreateAdapterAsync(string? clusterId = null, CancellationToken ct = default)
        {
            if (clusterId != null && Adapters.TryGetValue(clusterId, out var adapter))
            {
                return Task.FromResult<IKubernetesAdapter>(adapter);
            }
            return Task.FromResult<IKubernetesAdapter>(new FakeKubernetesAdapter());
        }
    }

    [Fact]
    public async Task AdapterConfigService_SavesAndRetrieves_MultipleKubernetesClusters()
    {
        var (db, conn) = CreateTestDbContext();
        using var _ = conn;
        using var __ = db;
        var encryptionService = CreateEncryptionService();

        var service = new AdapterConfigService(
            db,
            Options.Create(new ProxmoxOptions()),
            encryptionService,
            NullLogger<AdapterConfigService>.Instance
        );

        // 1. Add first cluster
        var req1 = new SaveKubernetesClusterRequest(
            Id: null,
            Name: "k8s-homelab-prod",
            ApiServerUrl: "https://192.168.1.50:6443",
            KubeConfigRaw: "apiVersion: v1\nkind: Config\nclusters: []",
            Token: null,
            ContextName: "default",
            SkipTlsVerify: true
        );
        var cluster1 = await service.SaveKubernetesClusterAsync(req1);
        Assert.NotNull(cluster1.Id);
        Assert.Equal("k8s-homelab-prod", cluster1.Name);
        Assert.True(cluster1.HasKubeConfig);

        // 2. Add second cluster
        var req2 = new SaveKubernetesClusterRequest(
            Id: null,
            Name: "k3s-edge-01",
            ApiServerUrl: "https://192.168.1.60:6443",
            KubeConfigRaw: null,
            Token: "secret-bearer-token-123",
            ContextName: null,
            SkipTlsVerify: false
        );
        var cluster2 = await service.SaveKubernetesClusterAsync(req2);
        Assert.NotNull(cluster2.Id);
        Assert.Equal("k3s-edge-01", cluster2.Name);
        Assert.True(cluster2.HasToken);

        // 3. List all
        var all = await service.GetKubernetesClustersAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, c => c.Name == "k8s-homelab-prod");
        Assert.Contains(all, c => c.Name == "k3s-edge-01");

        // 4. Raw cluster retrieval decrypts secrets
        var raw1 = await service.GetRawKubernetesClusterAsync(cluster1.Id);
        Assert.NotNull(raw1);
        Assert.Equal("apiVersion: v1\nkind: Config\nclusters: []", raw1.EncryptedKubeConfig);

        var raw2 = await service.GetRawKubernetesClusterAsync(cluster2.Id);
        Assert.NotNull(raw2);
        Assert.Equal("secret-bearer-token-123", raw2.EncryptedToken);

        // 5. Delete cluster
        var deleted = await service.DeleteKubernetesClusterAsync(cluster1.Id);
        Assert.True(deleted);

        var remaining = await service.GetKubernetesClustersAsync();
        Assert.Single(remaining);
        Assert.Equal("k3s-edge-01", remaining[0].Name);
    }

    [Fact]
    public async Task KubernetesAdapter_WorkloadOperations_Succeed()
    {
        var deploymentJson = @"{
            ""items"": [
                {
                    ""metadata"": { ""name"": ""caddy-ingress"", ""namespace"": ""ingress"" },
                    ""spec"": { ""replicas"": 2, ""template"": { ""spec"": { ""containers"": [{ ""image"": ""caddy:2.8-alpine"" }] } } },
                    ""status"": { ""readyReplicas"": 2, ""availableReplicas"": 2 }
                }
            ]
        }";

        var podsJson = @"{
            ""items"": [
                {
                    ""metadata"": { ""name"": ""caddy-ingress-abc"", ""namespace"": ""ingress"" },
                    ""spec"": { ""nodeName"": ""k8s-worker-01"" },
                    ""status"": { ""phase"": ""Running"", ""podIP"": ""10.244.1.15"", ""conditions"": [{ ""type"": ""Ready"", ""status"": ""True"" }] }
                }
            ]
        }";

        var handler = new MockDelegatingHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("/apis/apps/v1/deployments") || path.Contains("/apis/apps/v1/namespaces/ingress/deployments"))
            {
                if (req.Method == HttpMethod.Patch)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(deploymentJson, Encoding.UTF8, "application/json")
                };
            }

            if (path.Contains("/api/v1/namespaces"))
            {
                var nsJson = @"{ ""items"": [{ ""metadata"": { ""name"": ""default"" } }, { ""metadata"": { ""name"": ""ingress"" } }] }";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(nsJson, Encoding.UTF8, "application/json")
                };
            }

            if (path.Contains("/api/v1/pods") || path.Contains("/api/v1/namespaces/ingress/pods"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(podsJson, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new Kubernetes(new KubernetesClientConfiguration { Host = "http://localhost:8080" }, handler);
        var adapter = new KubernetesAdapter(client, NullLogger<KubernetesAdapter>.Instance);

        // 1. List Namespaces
        var ns = await adapter.ListNamespacesAsync();
        Assert.Equal(2, ns.Count);
        Assert.Contains("default", ns);
        Assert.Contains("ingress", ns);

        // 2. List Deployments
        var deployments = await adapter.ListDeploymentsAsync();
        Assert.Single(deployments);
        Assert.Equal("caddy-ingress", deployments[0].Name);
        Assert.Equal(2, deployments[0].DesiredReplicas);
        Assert.Equal("caddy:2.8-alpine", deployments[0].Images[0]);

        // 3. Restart Deployment
        var restarted = await adapter.RestartDeploymentAsync("ingress", "caddy-ingress");
        Assert.True(restarted);

        // 4. Scale Deployment
        var scaled = await adapter.ScaleDeploymentAsync("ingress", "caddy-ingress", 4);
        Assert.True(scaled);

        // 5. List Pods
        var pods = await adapter.ListPodsAsync();
        Assert.Single(pods);
        Assert.Equal("caddy-ingress-abc", pods[0].Name);
        Assert.Equal("Running", pods[0].Phase);
        Assert.True(pods[0].IsReady);
    }

    [Fact]
    public async Task DiscoveryService_ScansAcross_MultipleKubernetesClusters()
    {
        var (db, conn) = CreateTestDbContext();
        using var _ = conn;
        using var __ = db;
        var encryptionService = CreateEncryptionService();

        var configService = new AdapterConfigService(
            db,
            Options.Create(new ProxmoxOptions()),
            encryptionService,
            NullLogger<AdapterConfigService>.Instance
        );

        // Add 2 clusters to config
        await configService.SaveKubernetesClusterAsync(new SaveKubernetesClusterRequest("c1", "Cluster One", "https://c1:6443", null, "tok", null));
        await configService.SaveKubernetesClusterAsync(new SaveKubernetesClusterRequest("c2", "Cluster Two", "https://c2:6443", null, "tok", null));

        var mockAdapter1 = new FakeKubernetesAdapter
        {
            NodesToReturn = new List<K8sDiscoveredNodeDto>
            {
                new("c1-node-01", "10.0.1.1", new List<string> { "control-plane" }, true, false, "Ubuntu 24.04", "6.8.0", "containerd://1.7", new Dictionary<string, string>())
            }
        };

        var mockAdapter2 = new FakeKubernetesAdapter
        {
            NodesToReturn = new List<K8sDiscoveredNodeDto>
            {
                new("c2-node-01", "10.0.2.1", new List<string> { "worker" }, true, false, "Debian 12", "6.1.0", "containerd://1.7", new Dictionary<string, string>())
            }
        };

        var factory = new FakeKubernetesClientFactory();
        factory.Adapters["c1"] = mockAdapter1;
        factory.Adapters["c2"] = mockAdapter2;

        var hostService = new HostService(db, NullLogger<HostService>.Instance);
        var discoveryService = new DiscoveryService(
            db,
            new FakeProxmoxClient(),
            mockAdapter1,
            hostService,
            Options.Create(new ProxmoxOptions()),
            NullLogger<DiscoveryService>.Instance,
            configService,
            null,
            factory
        );

        var scanResult = await discoveryService.ScanAsync(includeProxmox: false, includeKubernetes: true);
        Assert.Equal(2, scanResult.TotalDiscovered);

        var cand1 = scanResult.Candidates.FirstOrDefault(c => c.Name == "c1-node-01");
        Assert.NotNull(cand1);
        Assert.Equal("c1", cand1.K8sClusterId);
        Assert.Equal("k8s:c1:c1-node-01", cand1.Id);

        var cand2 = scanResult.Candidates.FirstOrDefault(c => c.Name == "c2-node-01");
        Assert.NotNull(cand2);
        Assert.Equal("c2", cand2.K8sClusterId);
        Assert.Equal("k8s:c2:c2-node-01", cand2.Id);

        // Adopt candidate 1
        var importRes = await discoveryService.ImportCandidateAsync(new ImportCandidateRequest(
            Name: cand1.Name,
            IpAddress: cand1.IpAddress!,
            TargetType: cand1.TargetType,
            OsFamily: cand1.OsFamily,
            K8sClusterId: cand1.K8sClusterId,
            K8sNodeName: cand1.K8sNodeName
        ));

        Assert.True(importRes.Success);
        var savedHost = await db.Hosts.FirstAsync(h => h.Id == importRes.HostId);
        Assert.NotNull(savedHost.Kubernetes);
        Assert.Equal("c1", savedHost.Kubernetes.ClusterId);
        Assert.Equal("c1-node-01", savedHost.Kubernetes.NodeName);
    }

    private class MockDelegatingHandler : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockDelegatingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _handler(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}
