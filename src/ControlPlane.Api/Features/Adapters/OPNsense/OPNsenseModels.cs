namespace ControlPlane.Api.Features.Adapters.OPNsense;

public record OPNsenseGatewayStatus(
    string Name,
    string Interface,
    string Status,
    double? LatencyMs,
    double? LossPercentage,
    string? Address
);

public record OPNsenseInterfaceInfo(
    string Name,
    string Device,
    string? IpAddress,
    string Status,
    string? Media
);

public record OPNsenseServiceItem(
    string Id,
    string Name,
    string Description,
    bool Running,
    bool Enabled
);

public record OPNsenseDhcpLease(
    string Ip,
    string Mac,
    string? Hostname,
    string? Starts,
    string? Ends,
    string Status
);

public record OPNsenseFirmwareInfo(
    string Version,
    string Status,
    int UpdatesAvailable,
    List<string>? Packages,
    string? LastCheck
);

public record OPNsenseTelemetryResponse(
    string Hostname,
    string Version,
    string Status,
    List<OPNsenseGatewayStatus> Gateways,
    List<OPNsenseInterfaceInfo> Interfaces,
    List<OPNsenseServiceItem> Services,
    DateTimeOffset Timestamp
);

public record OPNsenseRestartServiceRequest(
    string ServiceName
);

public record OPNsenseServiceActionResult(
    bool Success,
    string Message
);
