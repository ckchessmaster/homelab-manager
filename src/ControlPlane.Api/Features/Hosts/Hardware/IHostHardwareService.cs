using ControlPlane.Api.Features.Agents.Models;

namespace ControlPlane.Api.Features.Hosts.Hardware;

public interface IHostHardwareService
{
    Task<HostHardwareInventoryDto?> GetHardwareInventoryAsync(Guid hostId, bool forceRefresh = false, CancellationToken ct = default);
    Task<HostHardwareInventoryDto?> TriggerHardwareScanAsync(Guid hostId, CancellationToken ct = default);
    Task ProcessAgentHardwareHeartbeatAsync(Guid hostId, AgentHardwareSummary hardware, CancellationToken ct = default);
    Task<HardwareThresholds> GetThresholdsAsync(CancellationToken ct = default);
    Task UpdateThresholdsAsync(HardwareThresholds thresholds, CancellationToken ct = default);
    HardwareHealthStatus EvaluateHealth(
        List<PhysicalDiskDto> disks,
        List<StorageControllerDto> controllers,
        List<PowerSupplyDto> powerSupplies,
        List<MemoryModuleDto> memoryModules,
        List<ZfsPoolHealthDto>? zfsPools,
        HardwareThresholds thresholds,
        out List<string> alerts);
}
