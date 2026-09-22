using System.Net;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Adapters.Kubernetes;
using ControlPlane.Api.Features.Adapters.OPNsense;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Adapters.UniFi;
using ControlPlane.Api.Features.Discovery;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Tests;

public class DiscoveryTests
{
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
        public List<K8sDiscoveredNodeDto> Nodes { get; set; } = new();

        public Task<List<K8sDiscoveredNodeDto>> ListNodesAsync(CancellationToken ct = default)
            => Task.FromResult(Nodes);

        public Task<bool> CordonNodeAsync(string nodeName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UncordonNodeAsync(string nodeName, CancellationToken ct = default) => Task.FromResult(true);
        public Task<K8sDrainResult> DrainNodeAsync(string nodeName, TimeSpan timeout, bool ignoreDaemonSets = true, bool deleteEmptyDirData = true, CancellationToken ct = default, Func<string, Task>? onProgress = null) => Task.FromResult(new K8sDrainResult(nodeName, true, 0, 0, null));
        public Task<K8sNodeStatus?> GetNodeStatusAsync(string nodeName, CancellationToken ct = default) => Task.FromResult<K8sNodeStatus?>(new K8sNodeStatus(nodeName, true, false, "192.168.1.10", 5));
    }

    private (ControlPlaneDbContext Db, SqliteConnection Conn) CreateInMemoryDbContext()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseSqlite(conn)
            .UseSnakeCaseNamingConvention()
            .Options;
        var db = new ControlPlaneDbContext(options);
        db.Database.EnsureCreated();
        return (db, conn);
    }

    [Fact]
    public async Task ScanAsync_DiscoversProxmoxAndCorrelatesExistingHosts()
    {
        var (db, conn) = CreateInMemoryDbContext();
        using var _ = conn;
        using var __ = db;

        // Seed existing host
        var existing = new Host
        {
            Id = Guid.NewGuid(),
            Hostname = "existing-pve-vm",
            IpAddress = "192.168.1.101",
            OsFamily = "linux_debian",
            TargetType = "proxmox_vm",
            Proxmox = new ProxmoxTarget { Node = "pve1", Vmid = 101 }
        };
        db.Hosts.Add(existing);
        await db.SaveChangesAsync();

        var fakePve = new FakeProxmoxClient
        {
            Resources = new List<ProxmoxClusterResourceDto>
            {
                new("qemu/101", "pve1", "qemu", 101, "existing-pve-vm", "running"),
                new("lxc/102", "pve1", "lxc", 102, "new-lxc-container", "running")
            },
            GuestIps = new Dictionary<(string, int), string>
            {
                { ("pve1", 101), "192.168.1.101" },
                { ("pve1", 102), "192.168.1.102" }
            }
        };

        var fakeK8s = new FakeKubernetesAdapter();
        var hostService = new HostService(db, NullLogger<HostService>.Instance);
        var pveOpts = Options.Create(new ProxmoxOptions { BaseUrl = "https://pve:8006", ApiTokenId = "token" });

        var service = new DiscoveryService(
            db,
            fakePve,
            fakeK8s,
            hostService,
            pveOpts,
            NullLogger<DiscoveryService>.Instance
        );

        var result = await service.ScanAsync(includeProxmox: true, includeKubernetes: false);

        Assert.Equal(2, result.TotalDiscovered);
        Assert.Equal(1, result.AlreadyManaged);
        Assert.Equal(1, result.UnmanagedCount);

        var managed = result.Candidates.First(c => c.Name == "existing-pve-vm");
        Assert.True(managed.IsManaged);
        Assert.Equal(existing.Id, managed.ExistingHostId);

        var unmanaged = result.Candidates.First(c => c.Name == "new-lxc-container");
        Assert.False(unmanaged.IsManaged);
        Assert.Equal("192.168.1.102", unmanaged.IpAddress);
        Assert.Equal("proxmox_lxc", unmanaged.TargetType);
    }

    [Fact]
    public async Task ScanAsync_DiscoversKubernetesNodesAndMergesWithProxmox()
    {
        var (db, conn) = CreateInMemoryDbContext();
        using var _ = conn;
        using var __ = db;

        var fakePve = new FakeProxmoxClient
        {
            Resources = new List<ProxmoxClusterResourceDto>
            {
                new("qemu/200", "pve1", "qemu", 200, "k8s-worker-1", "running")
            },
            GuestIps = new Dictionary<(string, int), string>
            {
                { ("pve1", 200), "192.168.1.200" }
            }
        };

        var fakeK8s = new FakeKubernetesAdapter
        {
            Nodes = new List<K8sDiscoveredNodeDto>
            {
                new(
                    Name: "k8s-worker-1",
                    InternalIp: "192.168.1.200",
                    Roles: new List<string> { "worker" },
                    IsReady: true,
                    Unschedulable: false,
                    OsImage: "Ubuntu 22.04 LTS",
                    KernelVersion: "5.15.0",
                    ContainerRuntimeVersion: "containerd://1.6.8",
                    Labels: new Dictionary<string, string>()
                ),
                new(
                    Name: "k8s-master-1",
                    InternalIp: "192.168.1.199",
                    Roles: new List<string> { "control-plane" },
                    IsReady: true,
                    Unschedulable: false,
                    OsImage: "Ubuntu 22.04 LTS",
                    KernelVersion: "5.15.0",
                    ContainerRuntimeVersion: "containerd://1.6.8",
                    Labels: new Dictionary<string, string>()
                )
            }
        };

        var hostService = new HostService(db, NullLogger<HostService>.Instance);
        var pveOpts = Options.Create(new ProxmoxOptions { BaseUrl = "https://pve:8006", ApiTokenId = "token" });

        var service = new DiscoveryService(
            db,
            fakePve,
            fakeK8s,
            hostService,
            pveOpts,
            NullLogger<DiscoveryService>.Instance
        );

        var result = await service.ScanAsync(includeProxmox: true, includeKubernetes: true);

        // 2 unique candidates (k8s-worker-1 was merged between PVE and K8s)
        Assert.Equal(2, result.TotalDiscovered);

        var worker = result.Candidates.First(c => c.Name == "k8s-worker-1");
        Assert.Equal("192.168.1.200", worker.IpAddress);
        Assert.Equal("k8s-worker-1", worker.K8sNodeName);
        Assert.Equal("pve1", worker.ProxmoxNode);
        Assert.Equal(200, worker.ProxmoxVmid);
        Assert.Contains("k8s-worker", worker.Roles);

        var master = result.Candidates.First(c => c.Name == "k8s-master-1");
        Assert.Equal("192.168.1.199", master.IpAddress);
        Assert.Contains("k8s-control-plane", master.Roles);
    }

    [Fact]
    public async Task ImportCandidateAsync_ValidRequest_CreatesHostInDb()
    {
        var (db, conn) = CreateInMemoryDbContext();
        using var _ = conn;
        using var __ = db;

        var hostService = new HostService(db, NullLogger<HostService>.Instance);
        var fakePve = new FakeProxmoxClient();
        var fakeK8s = new FakeKubernetesAdapter();
        var pveOpts = Options.Create(new ProxmoxOptions());

        var service = new DiscoveryService(
            db,
            fakePve,
            fakeK8s,
            hostService,
            pveOpts,
            NullLogger<DiscoveryService>.Instance
        );

        var request = new ImportCandidateRequest(
            Name: "imported-vm",
            IpAddress: "192.168.1.155",
            TargetType: "proxmox_vm",
            OsFamily: "linux_debian",
            FriendlyName: "Test Imported VM",
            ProxmoxNode: "pve1",
            ProxmoxVmid: 155
        );

        var response = await service.ImportCandidateAsync(request);

        Assert.True(response.Success);
        Assert.NotNull(response.HostId);
        Assert.Equal("imported-vm", response.Hostname);

        var hostInDb = await db.Hosts.FindAsync(response.HostId.Value);
        Assert.NotNull(hostInDb);
        Assert.Equal("imported-vm", hostInDb.Hostname);
        Assert.Equal("192.168.1.155", hostInDb.IpAddress);
        Assert.Equal("pve1", hostInDb.Proxmox?.Node);
        Assert.Equal(155, hostInDb.Proxmox?.Vmid);
    }

    [Fact]
    public async Task ImportCandidateAsync_WithHostnameAddress_ImportsSuccessfully()
    {
        var (db, conn) = CreateInMemoryDbContext();
        using var _ = conn;
        using var __ = db;

        var hostService = new HostService(db, NullLogger<HostService>.Instance);
        var fakePve = new FakeProxmoxClient();
        var fakeK8s = new FakeKubernetesAdapter();
        var pveOpts = Options.Create(new ProxmoxOptions());

        var service = new DiscoveryService(
            db,
            fakePve,
            fakeK8s,
            hostService,
            pveOpts,
            NullLogger<DiscoveryService>.Instance
        );

        var request = new ImportCandidateRequest(
            Name: "pve-root-node",
            IpAddress: "proxmox.local.chriskingdon.com",
            TargetType: "baremetal",
            OsFamily: "linux_debian",
            FriendlyName: "Primary Proxmox Hypervisor",
            ProxmoxNode: "proxmox",
            ProxmoxVmid: null
        );

        var response = await service.ImportCandidateAsync(request);

        Assert.True(response.Success);
        Assert.NotNull(response.HostId);
        Assert.Equal("pve-root-node", response.Hostname);

        var hostInDb = await db.Hosts.FindAsync(response.HostId.Value);
        Assert.NotNull(hostInDb);
        Assert.Equal("pve-root-node", hostInDb.Hostname);
        Assert.False(string.IsNullOrWhiteSpace(hostInDb.IpAddress));
    }

    [Fact]
    public async Task ImportCandidatesBatchAsync_ImportsMultipleCandidatesSuccessfully()
    {
        var (db, conn) = CreateInMemoryDbContext();
        using var _ = conn;
        using var __ = db;

        var hostService = new HostService(db, NullLogger<HostService>.Instance);
        var fakePve = new FakeProxmoxClient();
        var fakeK8s = new FakeKubernetesAdapter();
        var pveOpts = Options.Create(new ProxmoxOptions());

        var service = new DiscoveryService(
            db,
            fakePve,
            fakeK8s,
            hostService,
            pveOpts,
            NullLogger<DiscoveryService>.Instance
        );

        var batchRequest = new BatchImportCandidatesRequest(
            Candidates: new List<ImportCandidateRequest>
            {
                new("batch-node-01", "192.168.1.111", "kubernetes_node", "linux_debian", "Batch Node 1", K8sClusterId: "k8s-prod", K8sNodeName: "batch-node-01"),
                new("batch-node-02", "192.168.1.112", "kubernetes_node", "linux_debian", "Batch Node 2", K8sClusterId: "k8s-prod", K8sNodeName: "batch-node-02"),
                new("batch-node-03", "", "baremetal", "linux_debian") // Missing IP, should fail gracefully
            },
            CommonTargetType: "kubernetes_node",
            CommonOsFamily: "linux_debian"
        );

        var response = await service.ImportCandidatesBatchAsync(batchRequest);

        Assert.Equal(3, response.TotalRequested);
        Assert.Equal(2, response.SucceededCount);
        Assert.Equal(1, response.FailedCount);

        var item1 = response.Results.First(r => r.Name == "batch-node-01");
        Assert.True(item1.Success);
        Assert.NotNull(item1.HostId);

        var item2 = response.Results.First(r => r.Name == "batch-node-02");
        Assert.True(item2.Success);
        Assert.NotNull(item2.HostId);

        var item3 = response.Results.First(r => r.Name == "batch-node-03");
        Assert.False(item3.Success);
        Assert.Contains("valid IP address is required", item3.ErrorMessage);
    }

    [Fact]

    public async Task DiscoverClusterResourcesAsync_HandlesFloatingPointMetricsAndMissingFields()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/cluster/resources"))
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("""
                    {
                        "data": [
                            {
                                "id": "qemu/100",
                                "node": "proxmox",
                                "type": "qemu",
                                "vmid": "100",
                                "name": "prod-k8s-cp",
                                "status": "running",
                                "disk": 10485760.5,
                                "maxdisk": 34359738368.0,
                                "uptime": 12345.6,
                                "mem": 4294967296,
                                "maxmem": 8589934592
                            },
                            {
                                "id": "lxc/200",
                                "node": "proxmox",
                                "type": "lxc",
                                "vmid": 200,
                                "name": "dns-pihole",
                                "status": "stopped",
                                "disk": 0.0,
                                "maxdisk": 10737418240,
                                "uptime": 0,
                                "mem": 0,
                                "maxmem": 1073741824
                            }
                        ]
                    }
                    """)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new HttpClient(handler);
        var factory = new MockHttpClientFactory(client);
        var pveOpts = Options.Create(new ProxmoxOptions
        {
            BaseUrl = "https://192.168.1.30:8006",
            ApiTokenId = "root@pam!cp",
            ApiTokenSecret = "secret"
        });

        var proxmoxClient = new ProxmoxClient(
            factory,
            pveOpts,
            new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance),
            NullLogger<ProxmoxClient>.Instance
        );

        var resources = await proxmoxClient.DiscoverClusterResourcesAsync();

        Assert.Equal(2, resources.Count);
        var qemu = resources.First(r => r.Vmid == 100);
        Assert.Equal("prod-k8s-cp", qemu.Name);
        Assert.Equal("qemu", qemu.Type);
        Assert.Equal("proxmox", qemu.Node);
        Assert.Equal(10485760, qemu.Disk);

        var lxc = resources.First(r => r.Vmid == 200);
        Assert.Equal("dns-pihole", lxc.Name);
        Assert.Equal("lxc", lxc.Type);
    }

    [Fact]
    public async Task DiscoverClusterResourcesAsync_FallsBackToPerNodeWhenClusterForbidden()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/cluster/resources"))
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.Forbidden,
                    Content = new StringContent("""{"data":null,"errors":{"permission":"denied"}}""")
                };
            }
            if (path.EndsWith("/nodes"))
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("""{"data":[{"node":"proxmox","status":"online","maxcpu":8,"mem":16000000000,"maxmem":32000000000}]}""")
                };
            }
            if (path.Contains("/nodes/proxmox/qemu"))
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("""
                    {
                        "data": [
                            {
                                "vmid": 105,
                                "name": "fallback-vm",
                                "status": "running",
                                "disk": 5000000.0,
                                "maxdisk": 20000000000,
                                "uptime": 3600.0
                            }
                        ]
                    }
                    """)
                };
            }
            if (path.Contains("/nodes/proxmox/lxc"))
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("""{"data":[]}""")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new HttpClient(handler);
        var factory = new MockHttpClientFactory(client);
        var pveOpts = Options.Create(new ProxmoxOptions
        {
            BaseUrl = "https://192.168.1.30:8006",
            ApiTokenId = "root@pam!cp",
            ApiTokenSecret = "secret"
        });

        var proxmoxClient = new ProxmoxClient(
            factory,
            pveOpts,
            new ProxmoxTaskPoller(NullLogger<ProxmoxTaskPoller>.Instance),
            NullLogger<ProxmoxClient>.Instance
        );

        var resources = await proxmoxClient.DiscoverClusterResourcesAsync();

        Assert.Single(resources);
        Assert.Equal(105, resources[0].Vmid);
        Assert.Equal("fallback-vm", resources[0].Name);
        Assert.Equal("proxmox", resources[0].Node);
    }

    [Fact]
    public async Task ScanAsync_DetectsUbuntuGuestOs_ProperlySetsOsFamilyToUbuntu()
    {
        var (db, conn) = CreateInMemoryDbContext();
        using var _ = conn;
        using var __ = db;

        var hostService = new HostService(db, NullLogger<HostService>.Instance);
        var fakePve = new FakeProxmoxClient();
        fakePve.Resources.Add(new ProxmoxClusterResourceDto("qemu/200", "proxmox", "qemu", 200, "ubuntu-worker-node", "running"));
        fakePve.GuestIps[("proxmox", 200)] = "192.168.1.200";
        fakePve.GuestOsTypes[("proxmox", 200)] = "ubuntu";

        fakePve.Resources.Add(new ProxmoxClusterResourceDto("qemu/201", "proxmox", "qemu", 201, "debian-worker-node", "running"));
        fakePve.GuestIps[("proxmox", 201)] = "192.168.1.201";
        fakePve.GuestOsTypes[("proxmox", 201)] = "Debian GNU/Linux 12";

        var fakeK8s = new FakeKubernetesAdapter();
        fakeK8s.Nodes.Add(new K8sDiscoveredNodeDto(
            Name: "k8s-node-ubuntu",
            InternalIp: "192.168.1.202",
            Roles: new List<string> { "worker" },
            IsReady: true,
            Unschedulable: false,
            OsImage: "Ubuntu 22.04.4 LTS",
            KernelVersion: "6.5.0-generic",
            ContainerRuntimeVersion: "containerd://1.7.0",
            Labels: new Dictionary<string, string>()
        ));

        var pveOpts = Options.Create(new ProxmoxOptions { BaseUrl = "https://pve:8006", ApiTokenId = "token" });
        var service = new DiscoveryService(db, fakePve, fakeK8s, hostService, pveOpts, NullLogger<DiscoveryService>.Instance);

        var result = await service.ScanAsync(includeProxmox: true, includeKubernetes: true);

        var ubuntuVm = result.Candidates.FirstOrDefault(c => c.Name == "ubuntu-worker-node");
        Assert.NotNull(ubuntuVm);
        Assert.Equal("linux_ubuntu", ubuntuVm.OsFamily);

        var debianVm = result.Candidates.FirstOrDefault(c => c.Name == "debian-worker-node");
        Assert.NotNull(debianVm);
        Assert.Equal("linux_debian", debianVm.OsFamily);

        var k8sNode = result.Candidates.FirstOrDefault(c => c.Name == "k8s-node-ubuntu");
        Assert.NotNull(k8sNode);
        Assert.Equal("linux_ubuntu", k8sNode.OsFamily);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) => Task.FromResult(_handler(req));
    }

    private class MockHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public MockHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private class FakeOPNsenseClient : IOPNsenseClient
    {
        public List<OPNsenseDhcpLease> Leases { get; set; } = new();
        public Task<OPNsenseTestResultDto> TestConnectionAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default) => Task.FromResult(new OPNsenseTestResultDto(true, "opn", "1.0", "OK", 10, null));
        public Task<OPNsenseTelemetryResponse> GetTelemetryAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default) => Task.FromResult(new OPNsenseTelemetryResponse("opn", "1.0", "OK", new(), new(), new(), DateTimeOffset.UtcNow));
        public Task<List<OPNsenseGatewayStatus>> GetGatewaysAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default) => Task.FromResult(new List<OPNsenseGatewayStatus>());
        public Task<List<OPNsenseInterfaceInfo>> GetInterfacesAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default) => Task.FromResult(new List<OPNsenseInterfaceInfo>());
        public Task<List<OPNsenseServiceItem>> GetServicesAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default) => Task.FromResult(new List<OPNsenseServiceItem>());
        public Task<OPNsenseServiceActionResult> RestartServiceAsync(string baseUrl, string apiKey, string apiSecret, string serviceName, bool allowSelfSigned = true, CancellationToken ct = default) => Task.FromResult(new OPNsenseServiceActionResult(true, "OK"));
        public Task<List<OPNsenseDhcpLease>> GetDhcpLeasesAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default) => Task.FromResult(Leases);
        public Task<OPNsenseFirmwareInfo> GetFirmwareStatusAsync(string baseUrl, string apiKey, string apiSecret, bool allowSelfSigned = true, CancellationToken ct = default) => Task.FromResult(new OPNsenseFirmwareInfo("1.0", "OK", 0, null, null));
    }

    private class FakeOPNsenseClientFactory : IOPNsenseClientFactory
    {
        public FakeOPNsenseClient Client { get; } = new();
        public List<OPNsenseStoredInstance> Instances { get; set; } = new();

        public Task<(IOPNsenseClient Client, OPNsenseStoredInstance Config, string ApiSecret)> ResolveAsync(string instanceId, CancellationToken ct = default)
        {
            var match = Instances.First(i => i.Id == instanceId);
            return Task.FromResult<(IOPNsenseClient, OPNsenseStoredInstance, string)>((Client, match, "secret"));
        }

        public Task<List<(OPNsenseStoredInstance Config, string ApiSecret)>> ResolveAllAsync(CancellationToken ct = default)
        {
            return Task.FromResult(Instances.Select(i => (i, "secret")).ToList());
        }

        public IOPNsenseClient GetClient() => Client;
    }

    private class FakeUniFiClient : IUniFiClient
    {
        public List<UniFiDeviceDto> Devices { get; set; } = new();
        public List<UniFiMacLease> Clients { get; set; } = new();

        public Task<bool> LoginAsync(string controllerUrl, string username, string password, CancellationToken ct = default) => Task.FromResult(true);
        public Task<UniFiTestResultDto> TestConnectionAsync(string controllerUrl, string? username, string? password, string site = "default", string? apiKey = null, CancellationToken ct = default) => Task.FromResult(new UniFiTestResultDto(true, "8.0", 1, 1, new List<string> { "default" }, 5, null));
        public Task<List<UniFiDeviceDto>> GetDevicesAsync(string controllerUrl, string? username, string? password, string site = "default", string? apiKey = null, CancellationToken ct = default) => Task.FromResult(Devices);
        public Task<bool> RestartDeviceAsync(string controllerUrl, string? username, string? password, string deviceMac, string site = "default", string? apiKey = null, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UpgradeDeviceAsync(string controllerUrl, string? username, string? password, string deviceMac, string site = "default", string? apiKey = null, CancellationToken ct = default) => Task.FromResult(true);
        public Task<UniFiBounceResult> CyclePoEPortAsync(string controllerUrl, string? username, string? password, string switchMac, int portNumber, string site = "default", int delaySeconds = 5, string? apiKey = null, CancellationToken ct = default) => Task.FromResult(new UniFiBounceResult(true, "OK", switchMac, portNumber));
        public Task<List<UniFiMacLease>> GetActiveClientsAsync(string controllerUrl, string? username, string? password, string site = "default", string? apiKey = null, CancellationToken ct = default) => Task.FromResult(Clients);
    }

    private class FakeUniFiClientFactory : IUniFiClientFactory
    {
        public FakeUniFiClient Client { get; } = new();
        public List<UniFiStoredInstance> Instances { get; set; } = new();

        public Task<(IUniFiClient Client, UniFiStoredInstance Config, string Password, string? ApiKey)> ResolveAsync(string instanceId, CancellationToken ct = default)
        {
            var match = Instances.First(i => i.Id == instanceId);
            return Task.FromResult<(IUniFiClient, UniFiStoredInstance, string, string?)>((Client, match, "password", "api-key"));
        }

        public Task<List<(UniFiStoredInstance Config, string Password, string? ApiKey)>> ResolveAllAsync(CancellationToken ct = default)
        {
            return Task.FromResult(Instances.Select(i => (i, "password", (string?)"api-key")).ToList());
        }

        public IUniFiClient GetClient() => Client;
    }

    [Fact]
    public async Task ScanAsync_WithOPNsense_DiscoversFirewallHostAndDhcpLeases()
    {
        var (db, conn) = CreateInMemoryDbContext();
        using var _ = conn;

        var hostService = new HostService(db, NullLogger<HostService>.Instance);
        var fakePve = new FakeProxmoxClient();
        var fakeK8s = new FakeKubernetesAdapter();
        var fakeOpnFactory = new FakeOPNsenseClientFactory();
        fakeOpnFactory.Instances.Add(new OPNsenseStoredInstance
        {
            Id = "opn-core",
            Name = "Core Firewall",
            BaseUrl = "https://192.168.1.1",
            ApiKey = "key",
            EncryptedApiSecret = "sec"
        });
        fakeOpnFactory.Client.Leases.Add(new OPNsenseDhcpLease(
            Ip: "192.168.1.88",
            Mac: "11:22:33:44:55:66",
            Hostname: "nas-storage",
            Starts: null,
            Ends: null,
            Status: "active"
        ));

        var pveOpts = Options.Create(new ProxmoxOptions());
        var service = new DiscoveryService(
            db,
            fakePve,
            fakeK8s,
            hostService,
            pveOpts,
            NullLogger<DiscoveryService>.Instance,
            opnsenseClientFactory: fakeOpnFactory);

        var result = await service.ScanAsync(includeProxmox: false, includeKubernetes: false, includeUniFi: false, includeOPNsense: true);

        Assert.Equal(2, result.Candidates.Count);

        // 1. OPNsense Firewall Machine
        var fw = result.Candidates.FirstOrDefault(c => c.Id == "opnsense:host:opn-core");
        Assert.NotNull(fw);
        Assert.Equal("OPNsense", fw.Source);
        Assert.Equal("opn", fw.Name);
        Assert.Equal("192.168.1.1", fw.IpAddress);
        Assert.Equal("firewall", fw.TargetType);
        Assert.Equal("freebsd", fw.OsFamily);
        Assert.Contains("firewall", fw.Roles ?? new());
        Assert.False(fw.IsManaged);

        // 2. DHCP Lease
        var lease = result.Candidates.FirstOrDefault(c => c.Id == "opnsense:opn-core:112233445566");
        Assert.NotNull(lease);
        Assert.Equal("nas-storage", lease.Name);
        Assert.Equal("192.168.1.88", lease.IpAddress);
        Assert.False(lease.IsManaged);
    }

    [Fact]
    public async Task ScanAsync_WithUniFi_DiscoversDevicesAndClientsAsCandidates()
    {
        var (db, conn) = CreateInMemoryDbContext();
        using var _ = conn;

        var hostService = new HostService(db, NullLogger<HostService>.Instance);
        var fakePve = new FakeProxmoxClient();
        var fakeK8s = new FakeKubernetesAdapter();
        var fakeUniFiFactory = new FakeUniFiClientFactory();
        fakeUniFiFactory.Instances.Add(new UniFiStoredInstance
        {
            Id = "unifi-site-01",
            Name = "Primary Controller",
            ControllerUrl = "https://192.168.1.2:8443",
            Username = "admin",
            Site = "default"
        });

        // 1 Switch, 1 AP
        fakeUniFiFactory.Client.Devices.Add(new UniFiDeviceDto(
            Mac: "00:11:22:33:44:55",
            Name: "Core-Switch-24",
            Model: "USW-24-PoE",
            Type: "usw",
            Ip: "192.168.1.5",
            State: "online",
            Version: "6.5.59",
            UpgradeAvailable: false,
            UptimeSeconds: 7200,
            Temperature: 44.5,
            Ports: new List<UniFiPortDto>()
        ));
        fakeUniFiFactory.Client.Devices.Add(new UniFiDeviceDto(
            Mac: "aa:bb:cc:dd:ee:11",
            Name: "Living-Room-AP",
            Model: "U6-Pro",
            Type: "uap",
            Ip: "192.168.1.6",
            State: "online",
            Version: "6.5.54",
            UpgradeAvailable: false,
            UptimeSeconds: 7200,
            Temperature: null,
            Ports: new List<UniFiPortDto>()
        ));

        // 1 Client
        fakeUniFiFactory.Client.Clients.Add(new UniFiMacLease(
            Mac: "ff:ee:dd:cc:bb:aa",
            Ip: "192.168.1.150",
            Hostname: "workstation-pc",
            LastSeen: DateTimeOffset.UtcNow
        ));

        var pveOpts = Options.Create(new ProxmoxOptions());
        var service = new DiscoveryService(
            db,
            fakePve,
            fakeK8s,
            hostService,
            pveOpts,
            NullLogger<DiscoveryService>.Instance,
            unifiClientFactory: fakeUniFiFactory);

        var result = await service.ScanAsync(includeProxmox: false, includeKubernetes: false, includeUniFi: true, includeOPNsense: false);

        Assert.Equal(3, result.Candidates.Count);

        // Verify Switch
        var sw = result.Candidates.FirstOrDefault(c => c.Name == "Core-Switch-24");
        Assert.NotNull(sw);
        Assert.Equal("UniFi", sw.Source);
        Assert.Equal("switch", sw.TargetType);
        Assert.Equal("unifi_os", sw.OsFamily);
        Assert.Equal("00:11:22:33:44:55", sw.UnifiSwitchMac);
        Assert.Equal("192.168.1.5", sw.IpAddress);
        Assert.Contains("network-device", sw.Roles ?? new());

        // Verify AP
        var ap = result.Candidates.FirstOrDefault(c => c.Name == "Living-Room-AP");
        Assert.NotNull(ap);
        Assert.Equal("access_point", ap.TargetType);
        Assert.Equal("192.168.1.6", ap.IpAddress);

        // Verify Client
        var client = result.Candidates.FirstOrDefault(c => c.Name == "workstation-pc");
        Assert.NotNull(client);
        Assert.Equal("baremetal", client.TargetType);
        Assert.Contains("network-client", client.Roles ?? new());
    }

    [Fact]
    public async Task ImportCandidateAsync_WithUniFiSwitchAndOPNsenseFirewall_ImportsSuccessfully()
    {
        var (db, conn) = CreateInMemoryDbContext();
        using var _ = conn;

        var hostService = new HostService(db, NullLogger<HostService>.Instance);
        var fakePve = new FakeProxmoxClient();
        var fakeK8s = new FakeKubernetesAdapter();
        var pveOpts = Options.Create(new ProxmoxOptions());
        var service = new DiscoveryService(db, fakePve, fakeK8s, hostService, pveOpts, NullLogger<DiscoveryService>.Instance);

        // 1. Import UniFi Switch
        var swReq = new ImportCandidateRequest(
            Name: "Core Switch 24", // Has spaces, should be slugified and original saved as FriendlyName
            IpAddress: "192.168.1.5",
            TargetType: "switch",
            OsFamily: "unifi_os",
            UnifiSwitchMac: "00:11:22:33:44:55"
        );
        var swRes = await service.ImportCandidateAsync(swReq);
        Assert.True(swRes.Success);
        Assert.Equal("core-switch-24", swRes.Hostname);

        var swHost = await db.Hosts.Include(h => h.NetworkPort).FirstAsync(h => h.Id == swRes.HostId);
        Assert.Equal("core-switch-24", swHost.Hostname);
        Assert.Equal("Core Switch 24", swHost.FriendlyName);
        Assert.Equal("switch", swHost.TargetType);
        Assert.Equal("unifi_os", swHost.OsFamily);
        Assert.NotNull(swHost.NetworkPort);
        Assert.Equal("00:11:22:33:44:55", swHost.NetworkPort.SwitchMac);

        // 2. Import OPNsense Firewall
        var fwReq = new ImportCandidateRequest(
            Name: "opnsense-core",
            IpAddress: "192.168.1.1",
            TargetType: "firewall",
            OsFamily: "freebsd",
            FriendlyName: "Primary Gateway & Firewall"
        );
        var fwRes = await service.ImportCandidateAsync(fwReq);
        Assert.True(fwRes.Success);
        Assert.Equal("opnsense-core", fwRes.Hostname);

        var fwHost = await db.Hosts.FirstAsync(h => h.Id == fwRes.HostId);
        Assert.Equal("opnsense-core", fwHost.Hostname);
        Assert.Equal("Primary Gateway & Firewall", fwHost.FriendlyName);
        Assert.Equal("firewall", fwHost.TargetType);
        Assert.Equal("freebsd", fwHost.OsFamily);
    }
}
