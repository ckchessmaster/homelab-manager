using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Idrac;
using ControlPlane.Api.Features.Adapters.Proxmox;
using ControlPlane.Api.Features.Agents;
using ControlPlane.Api.Features.Agents.Models;
using ControlPlane.Api.Storage;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Features.Hosts.Hardware;

public class HostHardwareService : IHostHardwareService
{
    private readonly ControlPlaneDbContext _db;
    private readonly AgentConnectionManager _connectionManager;
    private readonly IIdracClientFactory? _idracFactory;
    private readonly IProxmoxClientFactory? _proxmoxFactory;
    private readonly ILogger<HostHardwareService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string SettingKeyHardwareThresholds = "hardware_thresholds";

    public HostHardwareService(
        ControlPlaneDbContext db,
        AgentConnectionManager connectionManager,
        ILogger<HostHardwareService> logger,
        IIdracClientFactory? idracFactory = null,
        IProxmoxClientFactory? proxmoxFactory = null)
    {
        _db = db;
        _connectionManager = connectionManager;
        _logger = logger;
        _idracFactory = idracFactory;
        _proxmoxFactory = proxmoxFactory;
    }

    public async Task<HardwareThresholds> GetThresholdsAsync(CancellationToken ct = default)
    {
        var setting = await _db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == SettingKeyHardwareThresholds, ct);

        if (setting != null && !string.IsNullOrWhiteSpace(setting.ValueJson))
        {
            try
            {
                var thresholds = JsonSerializer.Deserialize<HardwareThresholds>(setting.ValueJson, JsonOptions);
                if (thresholds != null) return thresholds;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize hardware thresholds; using defaults.");
            }
        }

        return new HardwareThresholds();
    }

    public async Task UpdateThresholdsAsync(HardwareThresholds thresholds, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(thresholds, JsonOptions);
        var setting = await _db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == SettingKeyHardwareThresholds, ct);

        if (setting == null)
        {
            setting = new SystemSetting
            {
                Key = SettingKeyHardwareThresholds,
                ValueJson = json,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _db.SystemSettings.Add(setting);
        }
        else
        {
            setting.ValueJson = json;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Updated hardware degradation thresholds.");
    }

    public HardwareHealthStatus EvaluateHealth(
        List<PhysicalDiskDto> disks,
        List<StorageControllerDto> controllers,
        List<PowerSupplyDto> powerSupplies,
        List<MemoryModuleDto> memoryModules,
        List<ZfsPoolHealthDto>? zfsPools,
        HardwareThresholds thresholds,
        out List<string> alerts)
    {
        var localAlerts = new List<string>();
        var worst = HardwareHealthStatus.Ok;

        void Elevate(HardwareHealthStatus level, string alert)
        {
            localAlerts.Add(alert);
            if (level == HardwareHealthStatus.Critical)
            {
                worst = HardwareHealthStatus.Critical;
            }
            else if (level == HardwareHealthStatus.Warning && worst != HardwareHealthStatus.Critical)
            {
                worst = HardwareHealthStatus.Warning;
            }
        }

        // 1. Physical Disks
        foreach (var disk in disks)
        {
            var diskLabel = !string.IsNullOrWhiteSpace(disk.Name) ? disk.Name : disk.DeviceId;

            if (disk.Status == HardwareHealthStatus.Critical ||
                string.Equals(disk.SmartHealthStatus, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(disk.SmartHealthStatus, "FAILURE_PREDICTED", StringComparison.OrdinalIgnoreCase))
            {
                Elevate(HardwareHealthStatus.Critical, $"Disk '{diskLabel}' SMART health failure predicted or failed.");
            }

            if (disk.WearOutPercentage.HasValue)
            {
                if (disk.WearOutPercentage.Value <= thresholds.CriticalSsdWearOutPct)
                {
                    Elevate(HardwareHealthStatus.Critical, $"Disk '{diskLabel}' SSD endurance critical: {disk.WearOutPercentage.Value:F1}% remaining.");
                }
                else if (disk.WearOutPercentage.Value <= thresholds.MinSsdWearOutPct)
                {
                    Elevate(HardwareHealthStatus.Warning, $"Disk '{diskLabel}' SSD endurance low: {disk.WearOutPercentage.Value:F1}% remaining.");
                }
            }

            if (disk.TemperatureCelsius.HasValue)
            {
                if (disk.TemperatureCelsius.Value >= thresholds.CriticalDiskTemperatureCelsius)
                {
                    Elevate(HardwareHealthStatus.Critical, $"Disk '{diskLabel}' temperature critical: {disk.TemperatureCelsius.Value:F0}°C.");
                }
                else if (disk.TemperatureCelsius.Value >= thresholds.MaxDiskTemperatureCelsius)
                {
                    Elevate(HardwareHealthStatus.Warning, $"Disk '{diskLabel}' temperature elevated: {disk.TemperatureCelsius.Value:F0}°C.");
                }
            }
        }

        // 2. Power Supplies
        if (thresholds.AlertOnPsuRedundancyLost)
        {
            if (powerSupplies.Any(p => !p.RedundancyHealthy))
            {
                Elevate(HardwareHealthStatus.Warning, "Power supply redundancy degraded or lost.");
            }
        }

        foreach (var psu in powerSupplies)
        {
            var psuName = psu.Name ?? psu.Id;
            if (psu.Status == HardwareHealthStatus.Critical)
            {
                Elevate(HardwareHealthStatus.Critical, $"Power supply '{psuName}' is faulted.");
            }
            else if (psu.Status == HardwareHealthStatus.Warning)
            {
                Elevate(HardwareHealthStatus.Warning, $"Power supply '{psuName}' is degraded or offline.");
            }
        }

        // 3. Storage Controllers
        foreach (var c in controllers)
        {
            if (c.Status == HardwareHealthStatus.Critical)
            {
                Elevate(HardwareHealthStatus.Critical, $"Storage controller '{c.Name}' is faulted.");
            }
            else if (c.Status == HardwareHealthStatus.Warning)
            {
                Elevate(HardwareHealthStatus.Warning, $"Storage controller '{c.Name}' is degraded.");
            }

            if (c.BatteryBackupHealthy == false)
            {
                Elevate(HardwareHealthStatus.Warning, $"Storage controller '{c.Name}' RAID battery backup unit (BBU) degraded.");
            }
        }

        // 4. Memory Modules
        if (thresholds.AlertOnEccErrors)
        {
            foreach (var m in memoryModules)
            {
                if (m.UncorrectableEccErrors.HasValue && m.UncorrectableEccErrors.Value > 0)
                {
                    Elevate(HardwareHealthStatus.Critical, $"DIMM '{m.SlotLocation}' recorded {m.UncorrectableEccErrors.Value} uncorrectable ECC error(s).");
                }
                else if (m.CorrectableEccErrors.HasValue && m.CorrectableEccErrors.Value > 10)
                {
                    Elevate(HardwareHealthStatus.Warning, $"DIMM '{m.SlotLocation}' recorded elevated correctable ECC errors ({m.CorrectableEccErrors.Value}).");
                }
            }
        }

        // 5. Proxmox ZFS Pools
        if (zfsPools != null)
        {
            foreach (var pool in zfsPools)
            {
                var state = pool.State.ToUpperInvariant();
                if (state == "FAULTED")
                {
                    Elevate(HardwareHealthStatus.Critical, $"ZFS pool '{pool.PoolName}' is FAULTED.");
                }
                else if (state == "DEGRADED" || state != "ONLINE")
                {
                    Elevate(HardwareHealthStatus.Warning, $"ZFS pool '{pool.PoolName}' is {pool.State}.");
                }
            }
        }

        alerts = localAlerts;
        return worst;
    }

    public async Task<HostHardwareInventoryDto?> GetHardwareInventoryAsync(Guid hostId, bool forceRefresh = false, CancellationToken ct = default)
    {
        var host = await _db.Hosts.FirstOrDefaultAsync(h => h.Id == hostId, ct);
        if (host == null) return null;

        var thresholds = await GetThresholdsAsync(ct);

        var disks = new List<PhysicalDiskDto>();
        var controllers = new List<StorageControllerDto>();
        var powerSupplies = new List<PowerSupplyDto>();
        var memoryModules = new List<MemoryModuleDto>();
        List<ZfsPoolHealthDto>? zfsPools = null;
        var sourceList = new List<string>();

        // 1. Ingest Agent telemetry if present
        var agentHw = _connectionManager.GetLatestHardware(hostId);
        if (agentHw != null && agentHw.Disks.Count > 0)
        {
            sourceList.Add("agent");
            foreach (var d in agentHw.Disks)
            {
                var status = ParseStatus(d.Status);
                disks.Add(new PhysicalDiskDto(
                    DeviceId: d.DeviceId,
                    Name: d.Name,
                    Model: d.Model,
                    SerialNumber: d.SerialNumber,
                    MediaType: d.MediaType,
                    SizeBytes: d.SizeBytes,
                    Status: status,
                    WearOutPercentage: d.WearOutPercentage,
                    TemperatureCelsius: d.TemperatureCelsius,
                    SmartHealthStatus: d.SmartHealthStatus,
                    Attributes: d.Attributes
                ));
            }
        }

        // 2. Query Out-of-band BMC (iDRAC / Redfish)
        if (_idracFactory != null && host.Idrac != null)
        {
            try
            {
                var resolved = await _idracFactory.ResolveByHostIdAsync(hostId, ct);
                if (resolved != null)
                {
                    var bmcHw = await resolved.Value.Client.GetInstanceHardwareInventoryAsync(resolved.Value.Config, resolved.Value.Password, ct);
                    if (bmcHw != null)
                    {
                        sourceList.Add("bmc");
                        controllers.AddRange(bmcHw.Controllers);
                        powerSupplies.AddRange(bmcHw.PowerSupplies);
                        memoryModules.AddRange(bmcHw.MemoryModules);

                        // If no disks were retrieved via agent, use BMC disks
                        if (disks.Count == 0 && bmcHw.Disks.Count > 0)
                        {
                            disks.AddRange(bmcHw.Disks);
                        }
                        else if (bmcHw.Disks.Count > 0)
                        {
                            // Enrich agent disks with slot/bay location from BMC if matched
                            foreach (var bmcDisk in bmcHw.Disks)
                            {
                                if (!string.IsNullOrWhiteSpace(bmcDisk.SlotLocation))
                                {
                                    var match = disks.FirstOrDefault(ad =>
                                        (!string.IsNullOrWhiteSpace(ad.SerialNumber) && string.Equals(ad.SerialNumber, bmcDisk.SerialNumber, StringComparison.OrdinalIgnoreCase)) ||
                                        (!string.IsNullOrWhiteSpace(ad.Model) && string.Equals(ad.Model, bmcDisk.Model, StringComparison.OrdinalIgnoreCase)));
                                    if (match != null && match.SlotLocation == null)
                                    {
                                        var idx = disks.IndexOf(match);
                                        disks[idx] = match with { SlotLocation = bmcDisk.SlotLocation };
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not fetch BMC hardware inventory for host {HostId}", hostId);
            }
        }

        // 3. Query Proxmox VE hypervisor disks and ZFS pools
        if (_proxmoxFactory != null && host.Proxmox != null && !string.IsNullOrWhiteSpace(host.Proxmox.Node))
        {
            try
            {
                var pveClient = await _proxmoxFactory.GetClientAsync(host.Proxmox.InstanceId, ct);
                if (pveClient != null)
                {
                    // If host is hypervisor or VM and no agent disks were discovered, query Proxmox disks
                    if (disks.Count == 0)
                    {
                        var pDisks = await pveClient.GetNodeDisksAsync(host.Proxmox.Node, ct);
                        if (pDisks.Count > 0)
                        {
                            sourceList.Add("proxmox");
                            foreach (var pd in pDisks)
                            {
                                var dStatus = string.Equals(pd.Health, "PASSED", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(pd.Health, "OK", StringComparison.OrdinalIgnoreCase)
                                    ? HardwareHealthStatus.Ok
                                    : HardwareHealthStatus.Critical;

                                double? wear = null;
                                if (pd.Wearout != null)
                                {
                                    if (double.TryParse(pd.Wearout.ToString(), out var parsedWear))
                                    {
                                        wear = parsedWear;
                                    }
                                }

                                disks.Add(new PhysicalDiskDto(
                                    DeviceId: pd.DevPath,
                                    Name: pd.Model,
                                    Model: pd.Model,
                                    SerialNumber: pd.Serial,
                                    MediaType: !string.IsNullOrWhiteSpace(pd.Type) ? pd.Type.ToUpperInvariant() : "SSD",
                                    SizeBytes: pd.Size,
                                    Status: dStatus,
                                    WearOutPercentage: wear,
                                    SmartHealthStatus: pd.Health
                                ));
                            }
                        }
                    }

                    // Query ZFS pools
                    var pZfs = await pveClient.GetNodeZfsPoolsAsync(host.Proxmox.Node, ct);
                    if (pZfs.Count > 0)
                    {
                        zfsPools = pZfs.Select(z => new ZfsPoolHealthDto(
                            PoolName: z.Name,
                            State: z.Health,
                            SizeBytes: z.Size,
                            AllocatedBytes: z.Alloc,
                            FreeBytes: z.Free,
                            Fragmentation: z.Frag?.ToString()
                        )).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not fetch Proxmox disk/ZFS telemetry for host {HostId}", hostId);
            }
        }

        // 4. Fallback to persisted snapshot if live queries yielded nothing
        if (disks.Count == 0 && controllers.Count == 0 && powerSupplies.Count == 0 && memoryModules.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(host.HardwareInventoryJson))
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<HostHardwareInventoryDto>(host.HardwareInventoryJson, JsonOptions);
                    if (cached != null) return cached;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to deserialize stored hardware inventory for host {HostId}", hostId);
                }
            }
        }

        // 5. Evaluate Health & Thresholds
        var overallHealth = EvaluateHealth(disks, controllers, powerSupplies, memoryModules, zfsPools, thresholds, out var alerts);

        var inventory = new HostHardwareInventoryDto(
            HostId: hostId,
            CollectedAt: DateTimeOffset.UtcNow,
            OverallHealth: overallHealth,
            HealthAlerts: alerts,
            Disks: disks,
            Controllers: controllers,
            PowerSupplies: powerSupplies,
            MemoryModules: memoryModules,
            ZfsPools: zfsPools,
            Source: sourceList.Count > 0 ? string.Join("+", sourceList) : "system"
        );

        // Persist to database asynchronously
        try
        {
            host.HardwareInventoryJson = JsonSerializer.Serialize(inventory, JsonOptions);
            host.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist hardware inventory snapshot for host {HostId}", hostId);
        }

        return inventory;
    }

    public async Task<HostHardwareInventoryDto?> TriggerHardwareScanAsync(Guid hostId, CancellationToken ct = default)
    {
        _logger.LogInformation("Triggering on-demand hardware scan for host {HostId}...", hostId);

        // If agent is online, dispatch CMD_HARDWARE_SCAN
        if (_connectionManager.IsOnline(hostId))
        {
            await _connectionManager.SendCommandAsync(hostId, new AgentCommandEnvelope
            {
                Type = "CMD_HARDWARE_SCAN"
            }, ct);

            // Give agent a brief moment to scan
            await Task.Delay(500, ct);
        }

        return await GetHardwareInventoryAsync(hostId, forceRefresh: true, ct);
    }

    public async Task ProcessAgentHardwareHeartbeatAsync(Guid hostId, AgentHardwareSummary hardware, CancellationToken ct = default)
    {
        var host = await _db.Hosts.FirstOrDefaultAsync(h => h.Id == hostId, ct);
        if (host == null) return;

        var thresholds = await GetThresholdsAsync(ct);

        var disks = hardware.Disks.Select(d => new PhysicalDiskDto(
            DeviceId: d.DeviceId,
            Name: d.Name,
            Model: d.Model,
            SerialNumber: d.SerialNumber,
            MediaType: d.MediaType,
            SizeBytes: d.SizeBytes,
            Status: ParseStatus(d.Status),
            WearOutPercentage: d.WearOutPercentage,
            TemperatureCelsius: d.TemperatureCelsius,
            SmartHealthStatus: d.SmartHealthStatus,
            Attributes: d.Attributes
        )).ToList();

        // Preserve existing controllers, PSUs, and memory from previous snapshot if present
        var controllers = new List<StorageControllerDto>();
        var powerSupplies = new List<PowerSupplyDto>();
        var memoryModules = new List<MemoryModuleDto>();
        List<ZfsPoolHealthDto>? zfsPools = null;

        if (!string.IsNullOrWhiteSpace(host.HardwareInventoryJson))
        {
            try
            {
                var prev = JsonSerializer.Deserialize<HostHardwareInventoryDto>(host.HardwareInventoryJson, JsonOptions);
                if (prev != null)
                {
                    controllers = prev.Controllers;
                    powerSupplies = prev.PowerSupplies;
                    memoryModules = prev.MemoryModules;
                    zfsPools = prev.ZfsPools;
                }
            }
            catch
            {
                // ignore
            }
        }

        var health = EvaluateHealth(disks, controllers, powerSupplies, memoryModules, zfsPools, thresholds, out var alerts);

        var inventory = new HostHardwareInventoryDto(
            HostId: hostId,
            CollectedAt: DateTimeOffset.UtcNow,
            OverallHealth: health,
            HealthAlerts: alerts,
            Disks: disks,
            Controllers: controllers,
            PowerSupplies: powerSupplies,
            MemoryModules: memoryModules,
            ZfsPools: zfsPools,
            Source: "agent"
        );

        host.HardwareInventoryJson = JsonSerializer.Serialize(inventory, JsonOptions);
        await _db.SaveChangesAsync(ct);
    }

    private static HardwareHealthStatus ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return HardwareHealthStatus.Ok;
        if (string.Equals(status, "Critical", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Unhealthy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return HardwareHealthStatus.Critical;
        }
        if (string.Equals(status, "Warning", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Degraded", StringComparison.OrdinalIgnoreCase))
        {
            return HardwareHealthStatus.Warning;
        }
        return HardwareHealthStatus.Ok;
    }
}
