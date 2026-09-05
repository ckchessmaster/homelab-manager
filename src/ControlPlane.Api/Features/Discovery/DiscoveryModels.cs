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
    [property: JsonPropertyName("k8sNodeName")] string? K8sNodeName = null,
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
    [property: JsonPropertyName("k8sNodeName")] string? K8sNodeName = null
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
