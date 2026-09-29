using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Config;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Hosts.Hardware;
using ControlPlane.Api.Features.Orchestration;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using HostEntity = ControlPlane.Api.Storage.Entities.Host;

using System.Text.Json.Serialization;

namespace ControlPlane.Api.Features.Demo;

public class DemoDataSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    public static readonly Guid HostPveNode01Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid HostK8sCp01Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid HostK8sWorker01Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid HostK8sWorker02Id = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid HostStorageNas01Id = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly ControlPlaneDbContext _db;
    private readonly IAdapterConfigService _adapterConfigService;
    private readonly AgentConnectionManager _connectionManager;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(
        ControlPlaneDbContext db,
        IAdapterConfigService adapterConfigService,
        AgentConnectionManager connectionManager,
        ILogger<DemoDataSeeder> logger)
    {
        _db = db;
        _adapterConfigService = adapterConfigService;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task SeedAsync(bool forceReset = false, CancellationToken ct = default)
    {
        if (forceReset)
        {
            _logger.LogInformation("Resetting existing demo state...");
            _db.StepLogs.RemoveRange(_db.StepLogs);
            _db.UpdateJobs.RemoveRange(_db.UpdateJobs);
            _db.Hosts.RemoveRange(_db.Hosts);
            await _db.SaveChangesAsync(ct);
        }

        var hostsExist = await _db.Hosts.AnyAsync(ct);
        if (!hostsExist)
        {
            _logger.LogInformation("Seeding demo homelab hosts and inventory...");
            await SeedHostsAsync(ct);
            await SeedRecentJobsAsync(ct);
        }

        await SeedAdapterConfigsAsync(ct);
        RegisterSimulatedAgentSessions();
        _logger.LogInformation("Demo data seeding completed successfully.");
    }

    public void RegisterSimulatedAgentSessions()
    {
        // pve-node-01
        var s1 = _connectionManager.RegisterSimulated(HostPveNode01Id, "pve-node-01");
        _connectionManager.UpdateMetrics(HostPveNode01Id, new AgentMetrics { CpuUsagePct = 28.4, MemoryUsagePct = 45.2, DiskFreePct = 72.1 });
        s1.KernelVersion = "Linux 6.8.4-2-pve";

        // k8s-cp-01
        var s2 = _connectionManager.RegisterSimulated(HostK8sCp01Id, "k8s-cp-01");
        _connectionManager.UpdateMetrics(HostK8sCp01Id, new AgentMetrics { CpuUsagePct = 18.2, MemoryUsagePct = 52.8, DiskFreePct = 64.0 });
        s2.KernelVersion = "Linux 6.8.0-31-generic";

        // k8s-worker-01
        var s3 = _connectionManager.RegisterSimulated(HostK8sWorker01Id, "k8s-worker-01");
        _connectionManager.UpdateMetrics(HostK8sWorker01Id, new AgentMetrics { CpuUsagePct = 34.5, MemoryUsagePct = 68.1, DiskFreePct = 55.4 });
        s3.KernelVersion = "Linux 6.8.0-31-generic";

        // k8s-worker-02
        var s4 = _connectionManager.RegisterSimulated(HostK8sWorker02Id, "k8s-worker-02");
        _connectionManager.UpdateMetrics(HostK8sWorker02Id, new AgentMetrics { CpuUsagePct = 41.0, MemoryUsagePct = 62.3, DiskFreePct = 58.9 });
        s4.KernelVersion = "Linux 6.8.0-31-generic";

        // storage-nas-01
        var s5 = _connectionManager.RegisterSimulated(HostStorageNas01Id, "storage-nas-01");
        _connectionManager.UpdateMetrics(HostStorageNas01Id, new AgentMetrics { CpuUsagePct = 12.1, MemoryUsagePct = 82.5, DiskFreePct = 42.0 });
        s5.KernelVersion = "Linux 6.6.20-production+truenas";
    }

    private async Task SeedHostsAsync(CancellationToken ct)
    {
        var pveHwInventory = new HostHardwareInventoryDto(
            HostId: HostPveNode01Id,
            CollectedAt: DateTimeOffset.UtcNow,
            OverallHealth: HardwareHealthStatus.Ok,
            HealthAlerts: new List<string>(),
            Disks: new List<PhysicalDiskDto>
            {
                new("nvme0n1", "Samsung SSD 980 PRO 1TB", "980 PRO 1TB", "S5GXNF0R123456", "NVMe", 1000204886016, HardwareHealthStatus.Ok, 2.0, 34.0, "M.2 Slot 1", "PASSED"),
                new("sda", "Crucial MX500 2TB", "MX500", "2140E5E01234", "SATA SSD", 2000398934016, HardwareHealthStatus.Ok, 5.0, 31.0, "Bay 0", "PASSED"),
                new("sdb", "Crucial MX500 2TB", "MX500", "2140E5E01235", "SATA SSD", 2000398934016, HardwareHealthStatus.Ok, 4.0, 32.0, "Bay 1", "PASSED")
            },
            Controllers: new List<StorageControllerDto>
            {
                new("RAID.Integrated.1-1", "PERC H730P Mini", HardwareHealthStatus.Ok, "H730P Mini", "25.5.9.0001", true)
            },
            PowerSupplies: new List<PowerSupplyDto>
            {
                new("PSU.Slot.1", "Power Supply 1 (750W Platinum)", HardwareHealthStatus.Ok, 95.0, 85.0, 120.0, true),
                new("PSU.Slot.2", "Power Supply 2 (750W Platinum)", HardwareHealthStatus.Ok, 90.0, 80.0, 120.0, true)
            },
            MemoryModules: new List<MemoryModuleDto>
            {
                new("DIMM.Socket.A1", 34359738368, "2933", HardwareHealthStatus.Ok, 0, 0),
                new("DIMM.Socket.A2", 34359738368, "2933", HardwareHealthStatus.Ok, 0, 0),
                new("DIMM.Socket.B1", 34359738368, "2933", HardwareHealthStatus.Ok, 0, 0),
                new("DIMM.Socket.B2", 34359738368, "2933", HardwareHealthStatus.Ok, 0, 0)
            },
            ZfsPools: new List<ZfsPoolHealthDto>
            {
                new("rpool", "ONLINE", 950000000000, 320000000000, 630000000000, "12%", "Healthy mirrored boot pool"),
                new("tank", "ONLINE", 1900000000000, 850000000000, 1050000000000, "8%", "VM and container data store")
            },
            Source: "iDRAC9 + Proxmox VE"
        );

        var worker02HwInventory = new HostHardwareInventoryDto(
            HostId: HostK8sWorker02Id,
            CollectedAt: DateTimeOffset.UtcNow,
            OverallHealth: HardwareHealthStatus.Warning,
            HealthAlerts: new List<string>
            {
                "SSD nvme0n1 wear-out at 88% (threshold: 80%)",
                "Elevated disk temperature on nvme0n1: 58°C"
            },
            Disks: new List<PhysicalDiskDto>
            {
                new("nvme0n1", "Kingston KC3000 1TB", "KC3000", "50026B7685ABCDEF", "NVMe", 1024209543168, HardwareHealthStatus.Warning, 88.0, 58.0, "M.2 Slot 1", "PASSED"),
                new("sda", "Samsung SSD 870 EVO 500GB", "870 EVO", "S5YBNF0R789012", "SATA SSD", 500107862016, HardwareHealthStatus.Ok, 12.0, 33.0, "SATA 1", "PASSED")
            },
            Controllers: new List<StorageControllerDto>(),
            PowerSupplies: new List<PowerSupplyDto>
            {
                new("PSU.1", "Standard ATX Power Supply (500W)", HardwareHealthStatus.Ok, 140.0, 120.0, 120.0, false)
            },
            MemoryModules: new List<MemoryModuleDto>
            {
                new("DIMM_A1", 17179869184, "3200", HardwareHealthStatus.Ok, 0, 0),
                new("DIMM_B1", 17179869184, "3200", HardwareHealthStatus.Ok, 0, 0)
            },
            ZfsPools: null,
            Source: "agent"
        );

        var nasHwInventory = new HostHardwareInventoryDto(
            HostId: HostStorageNas01Id,
            CollectedAt: DateTimeOffset.UtcNow,
            OverallHealth: HardwareHealthStatus.Ok,
            HealthAlerts: new List<string>(),
            Disks: new List<PhysicalDiskDto>
            {
                new("sda", "Seagate IronWolf Pro 8TB", "ST8000NE001", "ZA123001", "HDD", 8001563222016, HardwareHealthStatus.Ok, null, 34.0, "Drive Bay 1", "PASSED"),
                new("sdb", "Seagate IronWolf Pro 8TB", "ST8000NE001", "ZA123002", "HDD", 8001563222016, HardwareHealthStatus.Ok, null, 35.0, "Drive Bay 2", "PASSED"),
                new("sdc", "Seagate IronWolf Pro 8TB", "ST8000NE001", "ZA123003", "HDD", 8001563222016, HardwareHealthStatus.Ok, null, 33.0, "Drive Bay 3", "PASSED"),
                new("sdd", "Seagate IronWolf Pro 8TB", "ST8000NE001", "ZA123004", "HDD", 8001563222016, HardwareHealthStatus.Ok, null, 36.0, "Drive Bay 4", "PASSED"),
                new("sde", "Seagate IronWolf Pro 8TB", "ST8000NE001", "ZA123005", "HDD", 8001563222016, HardwareHealthStatus.Ok, null, 34.0, "Drive Bay 5", "PASSED"),
                new("sdf", "Seagate IronWolf Pro 8TB", "ST8000NE001", "ZA123006", "HDD", 8001563222016, HardwareHealthStatus.Ok, null, 35.0, "Drive Bay 6", "PASSED"),
                new("nvme0n1", "WD Red SN700 500GB", "WDS500G1R0C", "22144580001", "NVMe", 500107862016, HardwareHealthStatus.Ok, 4.0, 32.0, "M.2 Cache 1", "PASSED"),
                new("nvme1n1", "WD Red SN700 500GB", "WDS500G1R0C", "22144580002", "NVMe", 500107862016, HardwareHealthStatus.Ok, 3.0, 31.0, "M.2 Cache 2", "PASSED")
            },
            Controllers: new List<StorageControllerDto>
            {
                new("HBA.LSI.1", "LSI SAS 9300-8i (IT Mode)", HardwareHealthStatus.Ok, "SAS3008", "16.00.12.00", true)
            },
            PowerSupplies: new List<PowerSupplyDto>
            {
                new("PSU.1", "Seasonic Prime 650W Platinum (PSU 1)", HardwareHealthStatus.Ok, 110.0, 110.0, 120.0, true),
                new("PSU.2", "Seasonic Prime 650W Platinum (PSU 2)", HardwareHealthStatus.Ok, 105.0, 105.0, 120.0, true)
            },
            MemoryModules: new List<MemoryModuleDto>
            {
                new("DIMM_A1", 17179869184, "2666", HardwareHealthStatus.Ok, 0, 0),
                new("DIMM_A2", 17179869184, "2666", HardwareHealthStatus.Ok, 0, 0),
                new("DIMM_B1", 17179869184, "2666", HardwareHealthStatus.Ok, 0, 0),
                new("DIMM_B2", 17179869184, "2666", HardwareHealthStatus.Ok, 0, 0)
            },
            ZfsPools: new List<ZfsPoolHealthDto>
            {
                new("tank-pool", "ONLINE", 48000000000000, 21000000000000, 27000000000000, "43%", "Primary RAID-Z2 storage pool (6x 8TB)"),
                new("boot-pool", "ONLINE", 500000000000, 15000000000, 485000000000, "3%", "Mirrored boot NVMe")
            },
            Source: "agent + TrueNAS"
        );

        var hosts = new List<HostEntity>
        {
            new()
            {
                Id = HostPveNode01Id,
                Hostname = "pve-node-01",
                FriendlyName = "Dell PowerEdge R740xd (Proxmox VE 8.2)",
                IpAddress = "192.168.1.20",
                OsFamily = "linux_debian",
                TargetType = "baremetal",
                Proxmox = new ProxmoxTarget { Node = "proxmox", Vmid = 0, InstanceId = "proxmox-default" },
                Idrac = new IdracTarget { IpAddress = "192.168.1.25" },
                NetworkPort = new UnifiPortTarget { SwitchMac = "74:ac:b9:44:55:66", PortNumber = 4 },
                Agent = new AgentState
                {
                    Installed = true,
                    Version = "1.3.0",
                    LastSeenAt = DateTimeOffset.UtcNow,
                    PendingReboot = false,
                    UpgradablePackagesCount = 0
                },
                HardwareInventoryJson = JsonSerializer.Serialize(pveHwInventory, JsonOptions)
            },
            new()
            {
                Id = HostK8sCp01Id,
                Hostname = "k8s-cp-01",
                FriendlyName = "Kubernetes Control Plane 01",
                IpAddress = "192.168.1.10",
                OsFamily = "linux_ubuntu",
                TargetType = "proxmox_vm",
                Proxmox = new ProxmoxTarget { Node = "proxmox", Vmid = 100, InstanceId = "proxmox-default" },
                Kubernetes = new KubernetesTarget { ClusterId = "k8s-default", NodeName = "k8s-cp-01" },
                NetworkPort = new UnifiPortTarget { SwitchMac = "74:ac:b9:44:55:66", PortNumber = 1 },
                Agent = new AgentState
                {
                    Installed = true,
                    Version = "1.3.0",
                    LastSeenAt = DateTimeOffset.UtcNow,
                    PendingReboot = false,
                    UpgradablePackagesCount = 3
                }
            },
            new()
            {
                Id = HostK8sWorker01Id,
                Hostname = "k8s-worker-01",
                FriendlyName = "Kubernetes Worker 01 (VM)",
                IpAddress = "192.168.1.11",
                OsFamily = "linux_ubuntu",
                TargetType = "proxmox_vm",
                Proxmox = new ProxmoxTarget { Node = "proxmox", Vmid = 101, InstanceId = "proxmox-default" },
                Kubernetes = new KubernetesTarget { ClusterId = "k8s-default", NodeName = "k8s-worker-01" },
                NetworkPort = new UnifiPortTarget { SwitchMac = "74:ac:b9:44:55:66", PortNumber = 2 },
                Agent = new AgentState
                {
                    Installed = true,
                    Version = "1.3.0",
                    LastSeenAt = DateTimeOffset.UtcNow,
                    PendingReboot = false,
                    UpgradablePackagesCount = 0
                }
            },
            new()
            {
                Id = HostK8sWorker02Id,
                Hostname = "k8s-worker-02",
                FriendlyName = "Kubernetes Worker 02 (Baremetal)",
                IpAddress = "192.168.1.12",
                OsFamily = "linux_ubuntu",
                TargetType = "baremetal",
                Kubernetes = new KubernetesTarget { ClusterId = "k8s-default", NodeName = "k8s-worker-02" },
                NetworkPort = new UnifiPortTarget { SwitchMac = "74:ac:b9:44:55:66", PortNumber = 3 },
                Agent = new AgentState
                {
                    Installed = true,
                    Version = "1.3.0",
                    LastSeenAt = DateTimeOffset.UtcNow,
                    PendingReboot = true,
                    UpgradablePackagesCount = 12
                },
                HardwareInventoryJson = JsonSerializer.Serialize(worker02HwInventory, JsonOptions)
            },
            new()
            {
                Id = HostStorageNas01Id,
                Hostname = "storage-nas-01",
                FriendlyName = "TrueNAS SCALE Core Storage",
                IpAddress = "192.168.1.30",
                OsFamily = "linux_debian",
                TargetType = "baremetal",
                NetworkPort = new UnifiPortTarget { SwitchMac = "74:ac:b9:44:55:66", PortNumber = 10 },
                Agent = new AgentState
                {
                    Installed = true,
                    Version = "1.3.0",
                    LastSeenAt = DateTimeOffset.UtcNow,
                    PendingReboot = false,
                    UpgradablePackagesCount = 1
                },
                HardwareInventoryJson = JsonSerializer.Serialize(nasHwInventory, JsonOptions)
            }
        };

        await _db.Hosts.AddRangeAsync(hosts, ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedRecentJobsAsync(CancellationToken ct)
    {
        var sampleJobId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var job = new UpdateJob
        {
            Id = sampleJobId,
            TargetHostId = HostK8sCp01Id,
            PipelineId = "standard-os-upgrade",
            InitiatedBy = "DemoOperator",
            Status = UpdateJobState.Completed,
            ActiveStep = "VerifyServices",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
            CompletedAt = DateTimeOffset.UtcNow.AddHours(-2).AddMinutes(4)
        };

        var logs = new List<StepLog>
        {
            new() { JobId = sampleJobId, SequenceId = 1, StreamType = "stdout", LogLine = "[INFO] Starting standard OS upgrade pipeline for host 'k8s-cp-01'...", Timestamp = DateTimeOffset.UtcNow.AddHours(-2) },
            new() { JobId = sampleJobId, SequenceId = 2, StreamType = "stdout", LogLine = "[INFO] Preflight check: verifying network connectivity and disk space...", Timestamp = DateTimeOffset.UtcNow.AddHours(-2).AddSeconds(10) },
            new() { JobId = sampleJobId, SequenceId = 3, StreamType = "stdout", LogLine = "Hit:1 http://archive.ubuntu.com/ubuntu noble InRelease", Timestamp = DateTimeOffset.UtcNow.AddHours(-2).AddSeconds(25) },
            new() { JobId = sampleJobId, SequenceId = 4, StreamType = "stdout", LogLine = "Fetched 18.4 MB in 3s (6,133 kB/s)", Timestamp = DateTimeOffset.UtcNow.AddHours(-2).AddSeconds(35) },
            new() { JobId = sampleJobId, SequenceId = 5, StreamType = "stdout", LogLine = "[INFO] Running unattended safe package upgrades...", Timestamp = DateTimeOffset.UtcNow.AddHours(-2).AddSeconds(50) },
            new() { JobId = sampleJobId, SequenceId = 6, StreamType = "stdout", LogLine = "[SUCCESS] All packages updated. Daemon services verified healthy.", Timestamp = DateTimeOffset.UtcNow.AddHours(-2).AddMinutes(4) }
        };

        await _db.UpdateJobs.AddAsync(job, ct);
        await _db.StepLogs.AddRangeAsync(logs, ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedAdapterConfigsAsync(CancellationToken ct)
    {
        try
        {
            // Proxmox
            await _adapterConfigService.SaveProxmoxInstanceAsync(new SaveProxmoxInstanceRequest(
                Id: "proxmox-default",
                Name: "Homelab Proxmox VE",
                BaseUrl: "https://192.168.1.20:8006",
                ApiTokenId: "root@pam!controlplane",
                ApiTokenSecret: "demo-token-secret",
                AllowSelfSignedCert: true
            ), ct);

            // Kubernetes
            await _adapterConfigService.SaveKubernetesClusterAsync(new SaveKubernetesClusterRequest(
                Id: "k8s-default",
                Name: "Homelab K8s Production",
                ApiServerUrl: "https://192.168.1.10:6443",
                KubeConfigRaw: null,
                Token: "demo-service-account-token",
                ContextName: null,
                SkipTlsVerify: true
            ), ct);

            // UniFi
            await _adapterConfigService.SaveUniFiInstanceAsync(new SaveUniFiInstanceRequest(
                Id: "unifi-default",
                Name: "Homelab UniFi Network",
                ControllerUrl: "https://192.168.1.1:8443",
                Username: "admin",
                Password: "demo-password",
                Site: "default"
            ), ct);

            // OPNsense
            await _adapterConfigService.SaveOPNsenseInstanceAsync(new SaveOPNsenseInstanceRequest(
                Id: "opnsense-default",
                Name: "Homelab Core Gateway",
                BaseUrl: "https://192.168.1.1",
                ApiKey: "demo-key",
                ApiSecret: "demo-secret",
                AllowSelfSignedCert: true
            ), ct);

            // iDRAC
            await _adapterConfigService.SaveIdracInstanceAsync(new SaveIdracInstanceRequest(
                Id: "idrac-default",
                Name: "pve-node-01 iDRAC9",
                BmcUrl: "https://192.168.1.25",
                Username: "root",
                Password: "calvin",
                AllowSelfSignedCert: true
            ), ct);

            // Home Assistant
            await _adapterConfigService.SaveHomeAssistantInstanceAsync(new SaveHomeAssistantInstanceRequest(
                Id: "ha-default",
                Name: "Homelab Home Assistant",
                BaseUrl: "http://192.168.1.180:8123",
                Token: "demo-ha-token",
                AllowSelfSignedCert: true
            ), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed some adapter settings (may already exist).");
        }
    }
}
