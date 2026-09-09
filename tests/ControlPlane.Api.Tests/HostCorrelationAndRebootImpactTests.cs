using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

namespace ControlPlane.Api.Tests;

public class HostCorrelationAndRebootImpactTests
{
    private class FakeProxmoxClientForImpact : IProxmoxClient
    {
        public List<ProxmoxClusterResourceDto> Resources { get; set; } = new();

        public Task<List<ProxmoxClusterResourceDto>> DiscoverClusterResourcesAsync(CancellationToken ct = default)
            => Task.FromResult(Resources);

        public Task<string> CreateVmSnapshotAsync(string node, int vmid, string snapName, string? description = null, bool isLxc = false, CancellationToken ct = default) => Task.FromResult("UPID:001");
        public Task<string> RollbackVmSnapshotAsync(string node, int vmid, string snapName, bool isLxc = false, CancellationToken ct = default) => Task.FromResult("UPID:002");
        public Task<string> DeleteVmSnapshotAsync(string node, int vmid, string snapName, bool isLxc = false, CancellationToken ct = default) => Task.FromResult("UPID:003");
        public Task<ProxmoxTaskStatus> GetTaskStatusAsync(string node, string upid, CancellationToken ct = default) => Task.FromResult(new ProxmoxTaskStatus("stopped", "OK"));
        public Task<ProxmoxTaskStatus> PollTaskCompletionAsync(string node, string upid, TimeSpan? timeout = null, CancellationToken ct = default) => Task.FromResult(new ProxmoxTaskStatus("stopped", "OK"));
        public Task<string?> TryGetGuestIpAddressAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) => Task.FromResult<string?>("192.168.1.100");
        public Task<List<ProxmoxSnapshotItem>> ListVmSnapshotsAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) => Task.FromResult(new List<ProxmoxSnapshotItem>());
        public Task<List<ProxmoxNodeDto>> ListNodesAsync(CancellationToken ct = default) => Task.FromResult(new List<ProxmoxNodeDto> { new("pve-01", "online", 1, 4, 1000, 4000, 50000) });
        public Task<bool> HasVmAuditPermissionAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> HasSnapshotFeatureAsync(string node, int vmid, bool isLxc = false, CancellationToken ct = default) => Task.FromResult(true);
    }

    private class FakeKubernetesAdapterForImpact : IKubernetesAdapter
    {
        public List<K8sDiscoveredNodeDto> Nodes { get; set; } = new();
        public List<K8sPodSummaryDto> Pods { get; set; } = new();

        public Task<List<K8sDiscoveredNodeDto>> ListNodesAsync(CancellationToken ct = default)
            => Task.FromResult(Nodes);

        public Task<List<K8sPodSummaryDto>> ListPodsAsync(string? namespaceName = null, string? nodeName = null, CancellationToken ct = default)
            => Task.FromResult(string.IsNullOrWhiteSpace(nodeName) ? Pods : Pods.Where(p => string.Equals(p.NodeName, nodeName, StringComparison.OrdinalIgnoreCase)).ToList());

        public Task<bool> CordonNodeAsync(string nodeName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UncordonNodeAsync(string nodeName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<K8sDrainResult> DrainNodeAsync(string nodeName, TimeSpan timeout, bool ignoreDaemonSets = true, bool deleteEmptyDirData = true, CancellationToken ct = default, Func<string, Task>? onProgress = null) => Task.FromResult(new K8sDrainResult(nodeName, true, 0, 0, null));
        public Task<K8sNodeStatus?> GetNodeStatusAsync(string nodeName, CancellationToken ct = default) => Task.FromResult<K8sNodeStatus?>(null);
    }

    private class FakeProxmoxClientFactory : IProxmoxClientFactory
    {
        private readonly IProxmoxClient _client;
        public FakeProxmoxClientFactory(IProxmoxClient client) => _client = client;
        public Task<IProxmoxClient> GetClientAsync(string? instanceId = null, CancellationToken ct = default) => Task.FromResult(_client);
        public IProxmoxClient CreateClient(ProxmoxOptions options) => _client;
    }

    private class FakeKubernetesClientFactory : IKubernetesClientFactory
    {
        private readonly IKubernetesAdapter _adapter;
        public FakeKubernetesClientFactory(IKubernetesAdapter adapter) => _adapter = adapter;
        public Task<IKubernetesAdapter> CreateAdapterAsync(string? clusterId = null, CancellationToken ct = default) => Task.FromResult(_adapter);
        public Task<k8s.IKubernetes> CreateClientAsync(string? clusterId = null, CancellationToken ct = default) => Task.FromResult<k8s.IKubernetes>(null!);
    }

    private class CorrelationTestAppFactory : WebApplicationFactory<Program>
    {
        private readonly string _tempDbFile = Path.Combine(Path.GetTempPath(), $"cp-test-corr-{Guid.NewGuid():N}.db");
        public FakeProxmoxClientForImpact FakePve { get; } = new();
        public FakeKubernetesAdapterForImpact FakeK8s { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("STANDBY_MODE", "true");
            builder.UseSetting("ControlPlane:ApiKey", "dev-secret-key-123");
            builder.UseSetting("ConnectionStrings:PostgresDatabase", "");
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ControlPlaneDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<ControlPlaneDbContext>(options =>
                {
                    options.UseSqlite($"Data Source={_tempDbFile}")
                        .UseSnakeCaseNamingConvention();
                });

                services.AddSingleton<IProxmoxClient>(FakePve);
                services.AddSingleton<IKubernetesAdapter>(FakeK8s);

                var pveFactoryDesc = services.SingleOrDefault(d => d.ServiceType == typeof(IProxmoxClientFactory));
                if (pveFactoryDesc != null) services.Remove(pveFactoryDesc);
                services.AddSingleton<IProxmoxClientFactory>(new FakeProxmoxClientFactory(FakePve));

                var k8sFactoryDesc = services.SingleOrDefault(d => d.ServiceType == typeof(IKubernetesClientFactory));
                if (k8sFactoryDesc != null) services.Remove(k8sFactoryDesc);
                services.AddSingleton<IKubernetesClientFactory>(new FakeKubernetesClientFactory(FakeK8s));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (File.Exists(_tempDbFile))
            {
                try { File.Delete(_tempDbFile); } catch { }
            }
        }
    }

    private static HttpClient CreateAuthClient(CorrelationTestAppFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ControlPlane-Key", "dev-secret-key-123");
        return client;
    }

    [Fact]
    public async Task GetRebootImpact_ForHypervisor_DetectsRunningVmsAndRequiresConfirmation()
    {
        using var factory = new CorrelationTestAppFactory();
        var client = CreateAuthClient(factory);

        var pveHostId = Guid.NewGuid();
        var vm1HostId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Hosts.Add(new HostEntity
            {
                Id = pveHostId,
                Hostname = "pve-01",
                IpAddress = "10.200.1.10",
                OsFamily = "linux_debian",
                TargetType = "proxmox_node",
                Proxmox = new ProxmoxTarget { Node = "pve-01", Vmid = 0 },
                Agent = new AgentState { Installed = true }
            });

            db.Hosts.Add(new HostEntity
            {
                Id = vm1HostId,
                Hostname = "k8s-worker-vm",
                IpAddress = "10.200.1.25",
                OsFamily = "linux_debian",
                TargetType = "proxmox_vm",
                Proxmox = new ProxmoxTarget { Node = "pve-01", Vmid = 101 },
                Kubernetes = new KubernetesTarget { ClusterId = "k8s-prod", NodeName = "k8s-worker-vm" },
                Agent = new AgentState { Installed = true }
            });

            await db.SaveChangesAsync();
        }

        // Configure fake Proxmox to report VM 101 as running
        factory.FakePve.Resources = new List<ProxmoxClusterResourceDto>
        {
            new("qemu/101", "pve-01", "qemu", 101, "k8s-worker-vm", "running")
        };

        var response = await client.GetAsync($"/api/v1/hosts/{pveHostId}/reboot-impact");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var impact = await response.Content.ReadFromJsonAsync<HostRebootImpactDto>();
        Assert.NotNull(impact);
        Assert.True(impact.IsHypervisor);
        Assert.Equal("pve-01", impact.HypervisorNode);
        Assert.True(impact.HasWarnings);
        Assert.True(impact.RequiresConfirmation);
        Assert.Single(impact.AffectedRunningVms);

        var runningVm = impact.AffectedRunningVms[0];
        Assert.Equal(101, runningVm.Vmid);
        Assert.Equal("k8s-worker-vm", runningVm.Name);
        Assert.Equal("k8s-prod", runningVm.K8sClusterId);

        // Warning messages must mention running VM and Kubernetes cluster impact
        Assert.Contains(impact.WarningMessages, m => m.Contains("1 virtual machine"));
        Assert.Contains(impact.WarningMessages, m => m.Contains("k8s-prod"));
    }

    [Fact]
    public async Task RebootHost_RejectsHypervisorWithRunningVms_UnlessForced()
    {
        using var factory = new CorrelationTestAppFactory();
        var client = CreateAuthClient(factory);

        var pveHostId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Hosts.Add(new HostEntity
            {
                Id = pveHostId,
                Hostname = "pve-02",
                IpAddress = "10.200.2.11",
                OsFamily = "linux_debian",
                TargetType = "proxmox_node",
                Proxmox = new ProxmoxTarget { Node = "pve-02", Vmid = 0 },
                Agent = new AgentState { Installed = true }
            });
            await db.SaveChangesAsync();
        }

        // Fake running VM on pve-02
        factory.FakePve.Resources = new List<ProxmoxClusterResourceDto>
        {
            new("qemu/200", "pve-02", "qemu", 200, "db-vm", "running")
        };

        // Connect agent WebSocket to make host online
        var wsClient = factory.Server.CreateWebSocketClient();
        var wsUri = new Uri(factory.Server.BaseAddress, $"/agent-hub?token=dev-secret-key-123&hostId={pveHostId}");
        using var ws = await wsClient.ConnectAsync(wsUri, CancellationToken.None);

        var connMgr = factory.Services.GetRequiredService<AgentConnectionManager>();
        for (int i = 0; i < 50 && !connMgr.IsOnline(pveHostId); i++)
        {
            await Task.Delay(50);
        }

        // 1. Without force -> 409 Conflict with impact details
        var unforcedResp = await client.PostAsJsonAsync($"/api/v1/hosts/{pveHostId}/reboot", new RebootHostRequest(Force: false));
        Assert.Equal(HttpStatusCode.Conflict, unforcedResp.StatusCode);
        var conflictBody = await unforcedResp.Content.ReadAsStringAsync();
        Assert.Contains("confirmation required", conflictBody, StringComparison.OrdinalIgnoreCase);

        // 2. With force -> 202 Accepted
        var forcedResp = await client.PostAsJsonAsync($"/api/v1/hosts/{pveHostId}/reboot", new RebootHostRequest(Force: true));
        Assert.Equal(HttpStatusCode.Accepted, forcedResp.StatusCode);
    }

    [Fact]
    public async Task GetRebootImpact_ForKubernetesNode_WarnsOnSoleControlPlaneAndQuorum()
    {
        using var factory = new CorrelationTestAppFactory();
        var client = CreateAuthClient(factory);

        var k8sHostId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Hosts.Add(new HostEntity
            {
                Id = k8sHostId,
                Hostname = "k8s-master-01",
                IpAddress = "10.200.3.30",
                OsFamily = "linux_debian",
                TargetType = "kubernetes_node",
                Kubernetes = new KubernetesTarget { ClusterId = "k8s-prod", NodeName = "k8s-master-01" },
                Agent = new AgentState { Installed = true }
            });
            await db.SaveChangesAsync();
        }

        // Configure fake Kubernetes adapter with single master node
        factory.FakeK8s.Nodes = new List<K8sDiscoveredNodeDto>
        {
            new("k8s-master-01", "10.200.3.30", new List<string> { "control-plane", "master" }, true, false, "linux", "6.1", "containerd", null)
        };
        factory.FakeK8s.Pods = new List<K8sPodSummaryDto>
        {
            new("coredns", "kube-system", "Running", "k8s-master-01", "10.244.0.5", 0, true, null),
            new("api-deployment-123", "default", "Running", "k8s-master-01", "10.244.0.6", 0, true, null)
        };

        var response = await client.GetAsync($"/api/v1/hosts/{k8sHostId}/reboot-impact");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var impact = await response.Content.ReadFromJsonAsync<HostRebootImpactDto>();
        Assert.NotNull(impact);
        Assert.True(impact.IsKubernetesNode);
        Assert.NotNull(impact.KubernetesImpact);
        Assert.True(impact.KubernetesImpact.IsControlPlane);
        Assert.True(impact.KubernetesImpact.IsOnlyControlPlane);
        Assert.True(impact.KubernetesImpact.QuorumAtRisk);
        Assert.Equal(2, impact.KubernetesImpact.RunningPodsCount);
        Assert.True(impact.RequiresConfirmation);
        Assert.Contains(impact.WarningMessages, m => m.Contains("sole control plane node"));
    }

    [Fact]
    public async Task GetHostCorrelation_ReturnsCorrelatedHypervisorAndVms()
    {
        using var factory = new CorrelationTestAppFactory();
        var client = CreateAuthClient(factory);

        var pveHostId = Guid.NewGuid();
        var vmHostId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Hosts.Add(new HostEntity
            {
                Id = pveHostId,
                Hostname = "pve-alpha",
                IpAddress = "10.200.4.5",
                OsFamily = "linux_debian",
                TargetType = "proxmox_node",
                Proxmox = new ProxmoxTarget { Node = "pve-alpha", Vmid = 0 },
                Agent = new AgentState { Installed = true }
            });

            db.Hosts.Add(new HostEntity
            {
                Id = vmHostId,
                Hostname = "vm-worker",
                IpAddress = "10.200.4.6",
                OsFamily = "linux_debian",
                TargetType = "proxmox_vm",
                Proxmox = new ProxmoxTarget { Node = "pve-alpha", Vmid = 105 },
                Kubernetes = new KubernetesTarget { ClusterId = "k8s-dev", NodeName = "vm-worker" },
                Agent = new AgentState { Installed = true }
            });

            await db.SaveChangesAsync();
        }

        // 1. Hypervisor Correlation
        var hypResp = await client.GetAsync($"/api/v1/hosts/{pveHostId}/correlation");
        Assert.Equal(HttpStatusCode.OK, hypResp.StatusCode);
        var hypCorr = await hypResp.Content.ReadFromJsonAsync<HostCorrelationDto>();
        Assert.NotNull(hypCorr);
        Assert.True(hypCorr.IsHypervisor);
        Assert.Single(hypCorr.HostedVms);
        Assert.Equal(105, hypCorr.HostedVms[0].Vmid);

        // 2. VM Correlation
        var vmResp = await client.GetAsync($"/api/v1/hosts/{vmHostId}/correlation");
        Assert.Equal(HttpStatusCode.OK, vmResp.StatusCode);
        var vmCorr = await vmResp.Content.ReadFromJsonAsync<HostCorrelationDto>();
        Assert.NotNull(vmCorr);
        Assert.True(vmCorr.IsVm);
        Assert.NotNull(vmCorr.Hypervisor);
        Assert.Equal("pve-alpha", vmCorr.Hypervisor.ProxmoxNode);
        Assert.Equal(pveHostId, vmCorr.Hypervisor.HostId);
        Assert.True(vmCorr.IsKubernetesNode);
        Assert.NotNull(vmCorr.Kubernetes);
        Assert.Equal("k8s-dev", vmCorr.Kubernetes.ClusterId);
    }

    [Fact]
    public async Task SyncHostCorrelations_PersistsKubernetesAndProxmoxLinksToDatabase()
    {
        using var factory = new CorrelationTestAppFactory();
        var client = CreateAuthClient(factory);

        var vmHostId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            await db.Database.EnsureCreatedAsync();

            // Seed host that is only a Proxmox VM with NO Kubernetes correlation in DB
            db.Hosts.Add(new HostEntity
            {
                Id = vmHostId,
                Hostname = "k8s-node-worker-99",
                IpAddress = "10.200.5.99",
                OsFamily = "linux_debian",
                TargetType = "proxmox_vm",
                Proxmox = new ProxmoxTarget { Node = "pve-01", Vmid = 250 },
                Kubernetes = null,
                Agent = new AgentState { Installed = true }
            });
            await db.SaveChangesAsync();
        }

        // Configure fake Kubernetes adapter with a node that matches the VM's IP and Hostname
        factory.FakeK8s.Nodes = new List<K8sDiscoveredNodeDto>
        {
            new("k8s-node-worker-99", "10.200.5.99", new List<string> { "worker" }, true, false, "linux", "6.1", "containerd", null)
        };

        // Call sync-correlation endpoint
        var syncResp = await client.PostAsync("/api/v1/hosts/sync-correlation", null);
        Assert.Equal(HttpStatusCode.OK, syncResp.StatusCode);

        var syncResult = await syncResp.Content.ReadFromJsonAsync<SyncCorrelationResultDto>();
        Assert.NotNull(syncResult);
        Assert.True(syncResult.Success);
        Assert.True(syncResult.CorrelatedKubernetesNodes >= 1);
        Assert.True(syncResult.TotalHostsUpdated >= 1);

        // Verify that the host's Kubernetes correlation is now saved and persisted in the database!
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            var reloadedHost = await db.Hosts.FindAsync(vmHostId);
            Assert.NotNull(reloadedHost);
            Assert.NotNull(reloadedHost.Kubernetes);
            Assert.Equal("default", reloadedHost.Kubernetes.ClusterId);
            Assert.Equal("k8s-node-worker-99", reloadedHost.Kubernetes.NodeName);
        }
    }

    [Fact]
    public async Task GetRebootImpact_ForVmNode_DoesNotClassifyVmAsHypervisor()
    {
        using var factory = new CorrelationTestAppFactory();
        var client = CreateAuthClient(factory);

        var pveHostId = Guid.NewGuid();
        var vmControlId = Guid.NewGuid();
        var vmMediaId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
            await db.Database.EnsureCreatedAsync();

            db.Hosts.Add(new HostEntity
            {
                Id = pveHostId,
                Hostname = "proxmox",
                IpAddress = "10.200.6.10",
                OsFamily = "linux_debian",
                TargetType = "proxmox_node",
                Proxmox = new ProxmoxTarget { Node = "proxmox", Vmid = 0 },
                Agent = new AgentState { Installed = true }
            });

            db.Hosts.Add(new HostEntity
            {
                Id = vmControlId,
                Hostname = "kube-control-01",
                IpAddress = "10.200.6.20",
                OsFamily = "linux_debian",
                TargetType = "proxmox_vm",
                Proxmox = new ProxmoxTarget { Node = "proxmox", Vmid = 105 },
                Kubernetes = new KubernetesTarget { ClusterId = "main-cluster", NodeName = "kube-control-01" },
                Agent = new AgentState { Installed = true }
            });

            db.Hosts.Add(new HostEntity
            {
                Id = vmMediaId,
                Hostname = "media-host",
                IpAddress = "10.200.6.30",
                OsFamily = "linux_debian",
                TargetType = "proxmox_vm",
                Proxmox = new ProxmoxTarget { Node = "proxmox", Vmid = 103 },
                Agent = new AgentState { Installed = true }
            });

            await db.SaveChangesAsync();
        }

        // Proxmox reports 15 running VMs on node 'proxmox'
        factory.FakePve.Resources = new List<ProxmoxClusterResourceDto>
        {
            new("qemu/105", "proxmox", "qemu", 105, "kube-control-01", "running"),
            new("qemu/103", "proxmox", "qemu", 103, "media-host", "running"),
            new("qemu/100", "proxmox", "qemu", 100, "HomeAssistant", "running")
        };

        // K8s has 3 control plane nodes in HA cluster
        factory.FakeK8s.Nodes = new List<K8sDiscoveredNodeDto>
        {
            new("kube-control-01", "10.200.6.20", new List<string> { "control-plane", "master" }, true, false, "linux", "6.1", "containerd", null),
            new("kube-control-02", "10.200.6.21", new List<string> { "control-plane", "master" }, true, false, "linux", "6.1", "containerd", null),
            new("kube-control-03", "10.200.6.22", new List<string> { "control-plane", "master" }, true, false, "linux", "6.1", "containerd", null),
        };

        // Query reboot impact for kube-control-01
        var response = await client.GetAsync($"/api/v1/hosts/{vmControlId}/reboot-impact");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var impact = await response.Content.ReadFromJsonAsync<HostRebootImpactDto>();
        Assert.NotNull(impact);

        // Crucial invariant: kube-control-01 is a VM, NOT a hypervisor!
        Assert.False(impact.IsHypervisor);
        Assert.Empty(impact.AffectedRunningVms);

        // It IS a Kubernetes node
        Assert.True(impact.IsKubernetesNode);
        Assert.NotNull(impact.KubernetesImpact);
        Assert.True(impact.KubernetesImpact.IsControlPlane);
        Assert.False(impact.KubernetesImpact.IsOnlyControlPlane);
        Assert.False(impact.KubernetesImpact.QuorumAtRisk);

        // Does NOT contain hypervisor warning
        Assert.DoesNotContain(impact.WarningMessages, m => m.Contains("Proxmox hypervisor", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(impact.WarningMessages, m => m.Contains("virtual machine(s)", StringComparison.OrdinalIgnoreCase));

        // DOES contain HA control plane summary
        Assert.Contains(impact.WarningMessages, m => m.Contains("high-availability cluster", StringComparison.OrdinalIgnoreCase));
    }
}
