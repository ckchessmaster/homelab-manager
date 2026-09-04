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
