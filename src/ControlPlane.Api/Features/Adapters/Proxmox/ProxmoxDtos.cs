namespace ControlPlane.Api.Features.Adapters.Proxmox;

public record ProxmoxProbeRequest(
    string? BaseUrl = null,
    string? ApiTokenId = null,
    string? ApiTokenSecret = null,
    bool AllowSelfSignedCert = true
);

public record ProxmoxNodeDto(
    string Node,
    string Status,
    double? Cpu = null,
    long? MaxCpu = null,
    long? Memory = null,
    long? MaxMemory = null,
    long? Uptime = null
);

public record ProxmoxProbeResponse(
    bool Success,
    string? Version = null,
    string? Release = null,
    string? Repoid = null,
    List<ProxmoxNodeDto>? Nodes = null,
    string? ErrorMessage = null
);

public record ProxmoxNodeVitalsDto(
    string Node,
    string Status,
    double? CpuUsagePct = null,
    long? MaxCpu = null,
    long? MemoryUsedBytes = null,
    long? MemoryMaxBytes = null,
    double? MemoryUsagePct = null,
    long? UptimeSeconds = null
);

public record ProxmoxVitalsDto(
    string InstanceId,
    string InstanceName,
    string? Version,
    int TotalNodes,
    int OnlineNodes,
    List<ProxmoxNodeVitalsDto> Nodes,
    DateTimeOffset FetchedAt
);
