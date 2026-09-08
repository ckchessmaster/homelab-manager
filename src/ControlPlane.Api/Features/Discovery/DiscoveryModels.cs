using System.Text.Json.Serialization;

namespace ControlPlane.Api.Features.Discovery;

/// <summary>
/// Represents a compute host discovered from Proxmox VE, Kubernetes, or other infrastructure adapters.
/// </summary>
public record DiscoveredCandidateDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("source")] string Source, // "Proxmox" | "Kubernetes"
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ipAddress")] string? IpAddress,
    [property: JsonPropertyName("targetType")] string TargetType, // "proxmox_vm", "proxmox_lxc", "baremetal"
    [property: JsonPropertyName("osFamily")] string OsFamily, // "linux_debian", "linux_rhel", "windows"
    [property: JsonPropertyName("status")] string Status, // "running", "stopped", "Ready", "NotReady"
    [property: JsonPropertyName("proxmoxNode")] string? ProxmoxNode = null,
    [property: JsonPropertyName("proxmoxVmid")] int? ProxmoxVmid = null,
    [property: JsonPropertyName("proxmoxInstanceId")] string? ProxmoxInstanceId = null,
    [property: JsonPropertyName("k8sClusterId")] string? K8sClusterId = null,
    [property: JsonPropertyName("k8sNodeName")] string? K8sNodeName = null,
    [property: JsonPropertyName("unifiSwitchMac")] string? UnifiSwitchMac = null,
    [property: JsonPropertyName("unifiSwitchPort")] int? UnifiSwitchPort = null,
    [property: JsonPropertyName("roles")] List<string>? Roles = null,
    [property: JsonPropertyName("isManaged")] bool IsManaged = false,
    [property: JsonPropertyName("existingHostId")] Guid? ExistingHostId = null,
    [property: JsonPropertyName("existingHostname")] string? ExistingHostname = null
);

/// <summary>
/// Result envelope returned by service discovery scan.
/// </summary>
public record DiscoveryScanResult(
    [property: JsonPropertyName("candidates")] List<DiscoveredCandidateDto> Candidates,
    [property: JsonPropertyName("totalDiscovered")] int TotalDiscovered,
    [property: JsonPropertyName("alreadyManaged")] int AlreadyManaged,
    [property: JsonPropertyName("unmanagedCount")] int UnmanagedCount,
    [property: JsonPropertyName("scannedAt")] DateTimeOffset ScannedAt,
    [property: JsonPropertyName("errors")] List<string> Errors
);

/// <summary>
/// Payload to import a discovered candidate directly into managed host inventory.
/// </summary>
public record ImportCandidateRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ipAddress")] string IpAddress,
    [property: JsonPropertyName("targetType")] string TargetType,
    [property: JsonPropertyName("osFamily")] string OsFamily,
    [property: JsonPropertyName("friendlyName")] string? FriendlyName = null,
    [property: JsonPropertyName("proxmoxNode")] string? ProxmoxNode = null,
    [property: JsonPropertyName("proxmoxVmid")] int? ProxmoxVmid = null,
    [property: JsonPropertyName("proxmoxInstanceId")] string? ProxmoxInstanceId = null,
    [property: JsonPropertyName("k8sClusterId")] string? K8sClusterId = null,
    [property: JsonPropertyName("k8sNodeName")] string? K8sNodeName = null,
    [property: JsonPropertyName("unifiSwitchMac")] string? UnifiSwitchMac = null,
    [property: JsonPropertyName("unifiSwitchPort")] int? UnifiSwitchPort = null
);

/// <summary>
/// Response returned after importing candidate.
/// </summary>
public record ImportCandidateResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("hostId")] Guid? HostId = null,
    [property: JsonPropertyName("hostname")] string? Hostname = null,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage = null
);

/// <summary>
/// Payload to import multiple discovered candidates in batch into managed host inventory.
/// </summary>
public record BatchImportCandidatesRequest(
    [property: JsonPropertyName("candidates")] List<ImportCandidateRequest> Candidates,
    [property: JsonPropertyName("commonTargetType")] string? CommonTargetType = null,
    [property: JsonPropertyName("commonOsFamily")] string? CommonOsFamily = null
);

/// <summary>
/// Per-candidate result in a batch import operation.
/// </summary>
public record BatchImportItemResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("hostId")] Guid? HostId = null,
    [property: JsonPropertyName("hostname")] string? Hostname = null,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage = null
);

/// <summary>
/// Response envelope returned after batch importing candidates.
/// </summary>
public record BatchImportCandidatesResponse(
    [property: JsonPropertyName("totalRequested")] int TotalRequested,
    [property: JsonPropertyName("succeededCount")] int SucceededCount,
    [property: JsonPropertyName("failedCount")] int FailedCount,
    [property: JsonPropertyName("results")] List<BatchImportItemResult> Results
);

