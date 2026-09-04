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
