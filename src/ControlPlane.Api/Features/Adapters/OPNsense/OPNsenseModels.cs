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
    string? Media,
    string? Description = null,
    string? MacAddress = null,
    int? Mtu = null,
    bool Enabled = true
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
    string? LastCheck,
    bool NeedsReboot = false,
    string? StatusMsg = null,
    string? UpgradeAction = null
);

public record OPNsenseVitalsInfo(
    double[] CpuLoadAverage,
    long MemoryTotalBytes,
    long MemoryUsedBytes,
    double MemoryUsagePercent,
    long DiskTotalBytes,
    long DiskUsedBytes,
    double DiskUsagePercent,
    long UptimeSeconds,
    string UptimeFormatted,
    Dictionary<string, double>? Temperatures,
    string? LastConfigChange
);

public record OPNsenseHAProxyBackendServer(
    string Name,
    string Address,
    int? Port,
    string Status,
    int? ActiveSessions,
    int? CheckDurationMs
);

public record OPNsenseHAProxyStatus(
    bool IsInstalled,
    bool Running,
    List<string> Frontends,
    List<OPNsenseHAProxyBackendServer> Backends,
    string? Message = null
);

public record OPNsenseAcmeCertificate(
    string Id,
    string Name,
    string Description,
    List<string> AltNames,
    string Status,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    int? DaysRemaining,
    DateTimeOffset? LastUpdate
);

public record OPNsenseAcmeStatus(
    bool IsInstalled,
    List<OPNsenseAcmeCertificate> Certificates,
    string? Message = null
);

public record OPNsenseSecurityAlert(
    string Timestamp,
    string Threat,
    string Category,
    string Severity,
    string SourceIp,
    int? SourcePort,
    string DestinationIp,
    int? DestinationPort,
    string Protocol,
    string Action
);

public record OPNsenseSecurityStatus(
    bool IsInstalled,
    bool Running,
    string? Model,
    int ThreatCount24h,
    List<OPNsenseSecurityAlert> Alerts
);

public record OPNsenseFirewallStats(
    int TotalFilterRules,
    int TotalAliases,
    int PfStatesCurrent,
    int PfStatesMax,
    double PfStatesPercent,
    int RecentBlockedPacketsCount
);

public record OPNsenseArpEntry(
    string Ip,
    string Mac,
    string Interface,
    string? Hostname,
    string? Manufacturer,
    bool Expired
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
