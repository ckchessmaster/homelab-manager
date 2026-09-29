namespace ControlPlane.Api.Features.Adapters.Redfish;

public record RedfishSystemInfo(
    string PowerState,
    string? Model,
    string? BiosVersion,
    string? HealthStatus,
    string? SerialNumber
);

public record RedfishSensorReading(
    string Name,
    double CurrentReadingCelsius,
    double? CriticalThresholdCelsius,
    string Status
);

public record RedfishFanReading(
    string Name,
    int ReadingRpm,
    string Status
);

public record RedfishThermalVitals(
    List<RedfishSensorReading> Temperatures,
    List<RedfishFanReading> Fans
);

public record RedfishResetRequest(
    string ResetType
);

public record RedfishResetResponse(
    bool Success,
    string Message
);

public record RedfishPowerActionRequest(
    string IdracIp,
    string ResetType,
    string Username,
    string Password,
    bool InsecureTls = true
);

public record RedfishDriveInfo(
    string Id,
    string? Name,
    string? Model,
    string? SerialNumber,
    string MediaType,
    long CapacityBytes,
    string HealthStatus,
    double? PredictedMediaLifeLeftPercent = null,
    bool? FailurePredicted = null,
    string? SlotLocation = null
);

public record RedfishStorageControllerInfo(
    string Id,
    string Name,
    string HealthStatus,
    string? Model = null,
    string? FirmwareVersion = null
);

public record RedfishPowerSupplyInfo(
    string Id,
    string? Name,
    string HealthStatus,
    string State,
    double? OutputWatts = null,
    double? LineInputVoltage = null,
    double? CapacityWatts = null
);

public record RedfishPowerVitals(
    double? TotalPowerWatts,
    bool RedundancyHealthy,
    List<RedfishPowerSupplyInfo> PowerSupplies
);

public record RedfishMemoryInfo(
    string Id,
    string DeviceLocator,
    long CapacityBytes,
    string? SpeedMhz,
    string HealthStatus,
    string State
);
