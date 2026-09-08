namespace ControlPlane.Api.Features.Adapters.Idrac;

public record IdracSensorReading(
    string Name,
    double CurrentReadingCelsius,
    double? CriticalThresholdCelsius,
    string Status
);

public record IdracFanReading(
    string Name,
    int ReadingRpm,
    string Status
);

public record IdracVitalsDto(
    string PowerState,
    string? HealthStatus,
    string? Model,
    string? BiosVersion,
    string? SerialNumber,
    double? PowerConsumptionWatts,
    List<IdracSensorReading> Temperatures,
    List<IdracFanReading> Fans
);

public record IdracPowerControlRequest(
    string ResetType
);

public record IdracPowerControlResponse(
    bool Success,
    string Message,
    string? PowerState = null
);

public record IdracPowerActionByIpRequest(
    string IdracIp,
    string ResetType,
    string? Username = null,
    string? Password = null,
    bool InsecureTls = true
);
