namespace ControlPlane.Api.Features.Hosts;

public record HostResponse(
    Guid Id,
    string Hostname,
    string? FriendlyName,
    string IpAddress,
    string OsFamily,
    string TargetType,
    ProxmoxTargetDto? Proxmox,
    IdracTargetDto? Idrac,
    UnifiPortTargetDto? NetworkPort,
    AgentStateDto Agent,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record ProxmoxTargetDto(string Node, int Vmid);

public record IdracTargetDto(string IpAddress);

public record UnifiPortTargetDto(string SwitchMac, int PortNumber);

public record AgentStateDto(
    bool Installed,
    string? Version,
    DateTimeOffset? LastSeenAt,
    bool PendingReboot,
    int UpgradablePackagesCount
);

public record CreateHostRequest(
    string Hostname,
    string? FriendlyName,
    string IpAddress,
    string OsFamily,
    string TargetType,
    string? ProxmoxNode = null,
    int? ProxmoxVmid = null,
    string? IdracIp = null,
    string? UnifiSwitchMac = null,
    int? UnifiSwitchPort = null
);

public record UpdateHostRequest(
    string? Hostname = null,
    string? FriendlyName = null,
    string? IpAddress = null,
    string? OsFamily = null,
    string? TargetType = null,
    string? ProxmoxNode = null,
    int? ProxmoxVmid = null,
    string? IdracIp = null,
    string? UnifiSwitchMac = null,
    int? UnifiSwitchPort = null,
    bool? PendingReboot = null
);

public record HostFilterQuery(
    string? OsFamily = null,
    string? TargetType = null,
    bool? PendingReboot = null,
    bool? HasUpdates = null,
    string? Search = null
);
