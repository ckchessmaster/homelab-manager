namespace ControlPlane.Api.Features.Hosts;

public record HostResponse(
    Guid Id,
    string Hostname,
    string? FriendlyName,
    string IpAddress,
    string OsFamily,
    string TargetType,
    ProxmoxTargetDto? Proxmox,
    KubernetesTargetDto? Kubernetes,
    IdracTargetDto? Idrac,
    UnifiPortTargetDto? NetworkPort,
    AgentStateDto Agent,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    HypervisorHostSummaryDto? Hypervisor = null,
    List<HostedVmSummaryDto>? HostedVms = null,
    HostVitalsDto? Vitals = null
);

public record HostVitalsDto(
    double? CpuUsagePct = null,
    double? MemoryUsagePct = null,
    double? DiskFreePct = null,
    double? TemperatureCelsius = null,
    double? PowerWatts = null,
    long? UptimeSeconds = null,
    string? PowerState = null,
    string? HealthStatus = null,
    string? Source = null
);

public record HypervisorHostSummaryDto(Guid HostId, string Hostname, string? FriendlyName, string? NodeName);

public record HostedVmSummaryDto(Guid HostId, string Hostname, string? FriendlyName, int Vmid, string TargetType, bool IsOnline);

public record ProxmoxTargetDto(string Node, int Vmid, string? InstanceId = null);

public record KubernetesTargetDto(string? ClusterId, string? NodeName);

public record IdracTargetDto(string IpAddress);

public record UnifiPortTargetDto(string SwitchMac, int PortNumber);

public record AgentStateDto(
    bool Installed,
    string? Version,
    DateTimeOffset? LastSeenAt,
    bool PendingReboot,
    int UpgradablePackagesCount,
    bool IsOnline = false
);

public record CreateHostRequest(
    string Hostname,
    string? FriendlyName,
    string IpAddress,
    string OsFamily,
    string TargetType,
    string? ProxmoxNode = null,
    int? ProxmoxVmid = null,
    string? ProxmoxInstanceId = null,
    string? K8sClusterId = null,
    string? K8sNodeName = null,
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
    string? ProxmoxInstanceId = null,
    string? K8sClusterId = null,
    string? K8sNodeName = null,
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
