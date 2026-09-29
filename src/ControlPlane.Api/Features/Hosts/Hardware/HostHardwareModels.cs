using System.Text.Json.Serialization;

namespace ControlPlane.Api.Features.Hosts.Hardware;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HardwareHealthStatus
{
    Ok,
    Warning,
    Critical,
    Unknown
}

public record PhysicalDiskDto(
    string DeviceId,
    string? Name,
    string? Model,
    string? SerialNumber,
    string MediaType,
    long SizeBytes,
    HardwareHealthStatus Status,
    double? WearOutPercentage = null,
    double? TemperatureCelsius = null,
    string? SlotLocation = null,
    string? SmartHealthStatus = null,
    Dictionary<string, string>? Attributes = null
);

public record StorageControllerDto(
    string Id,
    string Name,
    HardwareHealthStatus Status,
    string? Model = null,
    string? FirmwareVersion = null,
    bool? BatteryBackupHealthy = null
);

public record PowerSupplyDto(
    string Id,
    string? Name,
    HardwareHealthStatus Status,
    double? InputWatts = null,
    double? OutputWatts = null,
    double? LineInputVoltage = null,
    bool RedundancyHealthy = true
);

public record MemoryModuleDto(
    string SlotLocation,
    long SizeBytes,
    string? SpeedMhz,
    HardwareHealthStatus Status,
    int? CorrectableEccErrors = null,
    int? UncorrectableEccErrors = null
);

public record ZfsPoolHealthDto(
    string PoolName,
    string State,
    long SizeBytes,
    long AllocatedBytes,
    long FreeBytes,
    string? Fragmentation = null,
    string? HealthDetails = null
);

public record HostHardwareInventoryDto(
    Guid HostId,
    DateTimeOffset CollectedAt,
    HardwareHealthStatus OverallHealth,
    List<string> HealthAlerts,
    List<PhysicalDiskDto> Disks,
    List<StorageControllerDto> Controllers,
    List<PowerSupplyDto> PowerSupplies,
    List<MemoryModuleDto> MemoryModules,
    List<ZfsPoolHealthDto>? ZfsPools = null,
    string? Source = null
);

public record HardwareThresholds(
    double MinSsdWearOutPct = 10.0,
    double CriticalSsdWearOutPct = 2.0,
    double MaxDiskTemperatureCelsius = 55.0,
    double CriticalDiskTemperatureCelsius = 65.0,
    bool AlertOnPsuRedundancyLost = true,
    bool AlertOnEccErrors = true
);
