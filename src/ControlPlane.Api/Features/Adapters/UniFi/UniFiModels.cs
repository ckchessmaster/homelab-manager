namespace ControlPlane.Api.Features.Adapters.UniFi;

public record UniFiLoginRequest(
    string Username,
    string Password
);

public record UniFiPortBounceRequest(
    string ControllerUrl,
    string Username,
    string Password,
    string SwitchMac,
    int PortNumber,
    string Site = "default",
    int DelaySeconds = 5
);

public record UniFiBounceResult(
    bool Success,
    string Message,
    string SwitchMac,
    int PortNumber
);

public record UniFiMacLease(
    string Mac,
    string? Ip,
    string? Hostname,
    DateTimeOffset? LastSeen
);

public record UniFiPortOverride(
    int PortIdx,
    string PoeMode,
    string? Name = null
);

public record UniFiPortDto(
    int PortIdx,
    string? Name,
    bool Up,
    int? SpeedMbps,
    string PoeMode,
    double? PoePowerWatts,
    double? PoeVoltage = null,
    double? PoeCurrent = null
);

public record UniFiDeviceDto(
    string Mac,
    string? Name,
    string Model,
    string Type,
    string? Ip,
    string State,
    string? Version,
    bool UpgradeAvailable,
    long? UptimeSeconds,
    double? Temperature,
    List<UniFiPortDto> Ports
);

public record UniFiDeviceRestartRequest(
    string? Reason = null
);

public record UniFiDeviceUpgradeRequest();

public record UniFiPortCycleRequest(
    int DelaySeconds = 5
);

