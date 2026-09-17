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
    List<IdracFanReading> Fans,
    string? BmcFirmwareVersion = null
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
    string? IdracIp = null,
    string ResetType = "",
    string? Username = null,
    string? Password = null,
    bool InsecureTls = true,
    Guid? HostId = null
);

public record BmcFanControlRequest(
    string Mode = "Manual",
    int? Percentage = 37
);

public record BmcFanControlResponse(
    bool Success,
    string Message,
    string Mode,
    int? Percentage = null
);

public record BmcFanControlByIpRequest(
    string? IdracIp = null,
    string Mode = "Manual",
    int? Percentage = 37,
    string? Username = null,
    string? Password = null,
    bool InsecureTls = true,
    Guid? HostId = null
);

public record BmcChassisIdentifyRequest(
    string State = "Blink",
    int DurationSeconds = 15
);

public record BmcChassisIdentifyResponse(
    bool Success,
    string Message,
    string State
);

public record BmcChassisIdentifyByIpRequest(
    string? IdracIp = null,
    string State = "Blink",
    int DurationSeconds = 15,
    string? Username = null,
    string? Password = null,
    bool InsecureTls = true,
    Guid? HostId = null
);

public record BmcBootOverrideRequest(
    string Target = "BiosSetup"
);

public record BmcBootOverrideResponse(
    bool Success,
    string Message,
    string Target
);

public record BmcBootOverrideByIpRequest(
    string? IdracIp = null,
    string Target = "BiosSetup",
    string? Username = null,
    string? Password = null,
    bool InsecureTls = true,
    Guid? HostId = null
);
