using System.Text.Json;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Features.Hosts;
using ControlPlane.Api.Features.Hosts.Hardware;
using ControlPlane.Api.Features.Mcp;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using EFCore.NamingConventions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ControlPlane.Api.Tests;

public class HardwareMonitoringTests
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

    [Fact]
    public void EvaluateHealth_HealthyHardware_ReturnsOkWithNoAlerts()
    {
        var (db, conn) = CreateTestDbContext();
        using (conn)
        {
            var service = new HostHardwareService(
                db,
                new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance),
                NullLogger<HostHardwareService>.Instance);

            var thresholds = new HardwareThresholds();
            var disks = new List<PhysicalDiskDto>
            {
                new(
                    DeviceId: "/dev/sda",
                    Name: "/dev/sda",
                    Model: "Samsung 980 PRO",
                    SerialNumber: "S123",
                    MediaType: "SSD",
                    SizeBytes: 1000000000000,
                    Status: HardwareHealthStatus.Ok,
                    WearOutPercentage: 95.0,
                    TemperatureCelsius: 38.0,
                    SlotLocation: "Bay 0",
                    SmartHealthStatus: "PASSED"
                ),
                new(
                    DeviceId: "/dev/sdb",
                    Name: "/dev/sdb",
                    Model: "Seagate IronWolf",
                    SerialNumber: "H456",
                    MediaType: "HDD",
                    SizeBytes: 8000000000000,
                    Status: HardwareHealthStatus.Ok,
                    TemperatureCelsius: 42.0,
                    SlotLocation: "Bay 1",
                    SmartHealthStatus: "PASSED"
                )
            };

            var powerSupplies = new List<PowerSupplyDto>
            {
                new("psu1", "PSU 1", HardwareHealthStatus.Ok, 120, 110, 120, RedundancyHealthy: true),
                new("psu2", "PSU 2", HardwareHealthStatus.Ok, 115, 105, 120, RedundancyHealthy: true)
            };

            var memory = new List<MemoryModuleDto>
            {
                new("DIMM_A1", 34359738368, "3200", HardwareHealthStatus.Ok, CorrectableEccErrors: 0, UncorrectableEccErrors: 0)
            };

            var health = service.EvaluateHealth(disks, new(), powerSupplies, memory, null, thresholds, out var alerts);

            Assert.Equal(HardwareHealthStatus.Ok, health);
            Assert.Empty(alerts);
        }
    }

    [Fact]
    public void EvaluateHealth_DiskFailingSmart_ReturnsCritical()
    {
        var (db, conn) = CreateTestDbContext();
        using (conn)
        {
            var service = new HostHardwareService(
                db,
                new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance),
                NullLogger<HostHardwareService>.Instance);

            var thresholds = new HardwareThresholds();
            var disks = new List<PhysicalDiskDto>
            {
                new(
                    DeviceId: "/dev/sda",
                    Name: "/dev/sda",
                    Model: "Crucial MX500",
                    SerialNumber: "C789",
                    MediaType: "SSD",
                    SizeBytes: 500000000000,
                    Status: HardwareHealthStatus.Critical,
                    SmartHealthStatus: "FAILED"
                )
            };

            var health = service.EvaluateHealth(disks, new(), new(), new(), null, thresholds, out var alerts);

            Assert.Equal(HardwareHealthStatus.Critical, health);
            Assert.Contains(alerts, a => a.Contains("SMART health failure"));
        }
    }

    [Fact]
    public void EvaluateHealth_LowWearOut_ReturnsWarning()
    {
        var (db, conn) = CreateTestDbContext();
        using (conn)
        {
            var service = new HostHardwareService(
                db,
                new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance),
                NullLogger<HostHardwareService>.Instance);

            var thresholds = new HardwareThresholds { MinSsdWearOutPct = 10.0, CriticalSsdWearOutPct = 2.0 };
            var disks = new List<PhysicalDiskDto>
            {
                new(
                    DeviceId: "/dev/nvme0n1",
                    Name: "/dev/nvme0n1",
                    Model: "Samsung 970 EVO",
                    SerialNumber: "S999",
                    MediaType: "NVMe",
                    SizeBytes: 500000000000,
                    Status: HardwareHealthStatus.Ok,
                    WearOutPercentage: 5.0,
                    SmartHealthStatus: "PASSED"
                )
            };

            var health = service.EvaluateHealth(disks, new(), new(), new(), null, thresholds, out var alerts);

            Assert.Equal(HardwareHealthStatus.Warning, health);
            Assert.Contains(alerts, a => a.Contains("SSD endurance low: 5.0% remaining"));
        }
    }

    [Fact]
    public void EvaluateHealth_HighTemperature_ReturnsWarningOrCritical()
    {
        var (db, conn) = CreateTestDbContext();
        using (conn)
        {
            var service = new HostHardwareService(
                db,
                new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance),
                NullLogger<HostHardwareService>.Instance);

            var thresholds = new HardwareThresholds
            {
                MaxDiskTemperatureCelsius = 55.0,
                CriticalDiskTemperatureCelsius = 65.0
            };

            // Warning temperature (58°C)
            var warningDisks = new List<PhysicalDiskDto>
            {
                new("/dev/sda", "/dev/sda", "Disk", "1", "SSD", 1000, HardwareHealthStatus.Ok, TemperatureCelsius: 58.0)
            };
            var warningHealth = service.EvaluateHealth(warningDisks, new(), new(), new(), null, thresholds, out var warningAlerts);
            Assert.Equal(HardwareHealthStatus.Warning, warningHealth);
            Assert.Contains(warningAlerts, a => a.Contains("temperature elevated: 58°C"));

            // Critical temperature (72°C)
            var critDisks = new List<PhysicalDiskDto>
            {
                new("/dev/sda", "/dev/sda", "Disk", "1", "SSD", 1000, HardwareHealthStatus.Ok, TemperatureCelsius: 72.0)
            };
            var critHealth = service.EvaluateHealth(critDisks, new(), new(), new(), null, thresholds, out var critAlerts);
            Assert.Equal(HardwareHealthStatus.Critical, critHealth);
            Assert.Contains(critAlerts, a => a.Contains("temperature critical: 72°C"));
        }
    }

    [Fact]
    public void EvaluateHealth_NonRedundantPsu_ReturnsWarning()
    {
        var (db, conn) = CreateTestDbContext();
        using (conn)
        {
            var service = new HostHardwareService(
                db,
                new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance),
                NullLogger<HostHardwareService>.Instance);

            var thresholds = new HardwareThresholds { AlertOnPsuRedundancyLost = true };
            var powerSupplies = new List<PowerSupplyDto>
            {
                new("psu1", "PSU 1", HardwareHealthStatus.Ok, RedundancyHealthy: false)
            };

            var health = service.EvaluateHealth(new(), new(), powerSupplies, new(), null, thresholds, out var alerts);
            Assert.Equal(HardwareHealthStatus.Warning, health);
            Assert.Contains(alerts, a => a.Contains("redundancy degraded"));
        }
    }

    [Fact]
    public void EvaluateHealth_EccErrors_ReturnsWarningAndCritical()
    {
        var (db, conn) = CreateTestDbContext();
        using (conn)
        {
            var service = new HostHardwareService(
                db,
                new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance),
                NullLogger<HostHardwareService>.Instance);

            var thresholds = new HardwareThresholds { AlertOnEccErrors = true };

            // Correctable ECC > 10 returns Warning
            var warningMemory = new List<MemoryModuleDto>
            {
                new("DIMM_B1", 16000000000, "2666", HardwareHealthStatus.Ok, CorrectableEccErrors: 15, UncorrectableEccErrors: 0)
            };
            var warningHealth = service.EvaluateHealth(new(), new(), new(), warningMemory, null, thresholds, out var warningAlerts);
            Assert.Equal(HardwareHealthStatus.Warning, warningHealth);
            Assert.Contains(warningAlerts, a => a.Contains("elevated correctable ECC errors"));

            // Uncorrectable ECC > 0 returns Critical
            var critMemory = new List<MemoryModuleDto>
            {
                new("DIMM_B1", 16000000000, "2666", HardwareHealthStatus.Ok, CorrectableEccErrors: 0, UncorrectableEccErrors: 2)
            };
            var critHealth = service.EvaluateHealth(new(), new(), new(), critMemory, null, thresholds, out var critAlerts);
            Assert.Equal(HardwareHealthStatus.Critical, critHealth);
            Assert.Contains(critAlerts, a => a.Contains("uncorrectable ECC error"));
        }
    }

    [Fact]
    public void EvaluateHealth_ZfsDegraded_ReturnsWarning()
    {
        var (db, conn) = CreateTestDbContext();
        using (conn)
        {
            var service = new HostHardwareService(
                db,
                new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance),
                NullLogger<HostHardwareService>.Instance);

            var zfs = new List<ZfsPoolHealthDto>
            {
                new("tank", "DEGRADED", 2000000, 1000000, 1000000)
            };

            var health = service.EvaluateHealth(new(), new(), new(), new(), zfs, new HardwareThresholds(), out var alerts);
            Assert.Equal(HardwareHealthStatus.Warning, health);
            Assert.Contains(alerts, a => a.Contains("pool 'tank' is DEGRADED"));
        }
    }

    [Fact]
    public async Task ProcessAgentHardwareHeartbeat_UpdatesInventoryAndSavesToDb()
    {
        var (db, conn) = CreateTestDbContext();
        using (conn)
        {
            var hostId = Guid.NewGuid();
            var host = new Host
            {
                Id = hostId,
                Hostname = "compute-01",
                IpAddress = "10.0.0.10",
                OsFamily = "linux_debian",
                TargetType = "compute_node"
            };
            db.Hosts.Add(host);
            await db.SaveChangesAsync();

            var connMgr = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
            var service = new HostHardwareService(
                db,
                connMgr,
                NullLogger<HostHardwareService>.Instance);

            var heartbeat = new AgentHardwareSummary
            {
                Disks = new List<AgentPhysicalDisk>
                {
                    new()
                    {
                        DeviceId = "/dev/nvme0n1",
                        Name = "/dev/nvme0n1",
                        Model = "Samsung 980 PRO 1TB",
                        SerialNumber = "S5GXNF0R123456",
                        SizeBytes = 1000204886016,
                        MediaType = "NVMe",
                        Status = "Ok",
                        SmartHealthStatus = "PASSED",
                        TemperatureCelsius = 41,
                        WearOutPercentage = 98
                    }
                }
            };

            await service.ProcessAgentHardwareHeartbeatAsync(hostId, heartbeat);

            var inventory = await service.GetHardwareInventoryAsync(hostId);
            Assert.NotNull(inventory);
            Assert.Equal(HardwareHealthStatus.Ok, inventory.OverallHealth);
            Assert.Single(inventory.Disks);
            Assert.Equal("Samsung 980 PRO 1TB", inventory.Disks[0].Model);
            Assert.Equal("PASSED", inventory.Disks[0].SmartHealthStatus);

            // Verify database was updated
            var reloadedHost = await db.Hosts.FindAsync(hostId);
            Assert.NotNull(reloadedHost?.HardwareInventoryJson);
            Assert.Contains("Samsung 980 PRO", reloadedHost.HardwareInventoryJson);
        }
    }

    [Fact]
    public async Task McpTools_GetHostHardwareInventory_ReturnsInventory()
    {
        var (db, conn) = CreateTestDbContext();
        using (conn)
        {
            var hostId = Guid.NewGuid();
            var host = new Host
            {
                Id = hostId,
                Hostname = "storage-01",
                IpAddress = "10.0.0.20",
                OsFamily = "linux_debian",
                TargetType = "compute_node",
                HardwareInventoryJson = JsonSerializer.Serialize(new HostHardwareInventoryDto(
                    HostId: hostId,
                    CollectedAt: DateTimeOffset.UtcNow,
                    OverallHealth: HardwareHealthStatus.Ok,
                    HealthAlerts: new(),
                    Disks: new()
                    {
                        new("/dev/sda", "/dev/sda", "Seagate IronWolf 8TB", "SN123", "HDD", 8000000000000, HardwareHealthStatus.Ok)
                    },
                    Controllers: new(),
                    PowerSupplies: new(),
                    MemoryModules: new()
                ))
            };
            db.Hosts.Add(host);
            await db.SaveChangesAsync();

            var connMgr = new AgentConnectionManager(NullLogger<AgentConnectionManager>.Instance);
            var hwService = new HostHardwareService(
                db,
                connMgr,
                NullLogger<HostHardwareService>.Instance);

            var hostService = new HostService(db, NullLogger<HostService>.Instance, connMgr);

            var mcpTools = new ControlPlaneMcpTools(
                db,
                hostService,
                null!,
                null!,
                null!,
                connMgr,
                hostHardwareService: hwService);

            var result = await mcpTools.GetHostHardwareInventory(hostId);
            Assert.NotNull(result);
            var inventory = Assert.IsType<HostHardwareInventoryDto>(result);
            Assert.Single(inventory.Disks);
            Assert.Equal("Seagate IronWolf 8TB", inventory.Disks[0].Model);
        }
    }
}
