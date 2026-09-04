namespace ControlPlane.Api.Features.Adapters.Proxmox;

public record ProxmoxProbeRequest(
    string BaseUrl,
    string ApiTokenId,
    string ApiTokenSecret,
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
