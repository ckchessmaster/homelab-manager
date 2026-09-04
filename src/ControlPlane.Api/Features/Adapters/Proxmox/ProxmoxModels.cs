using System.Text.Json.Serialization;

namespace ControlPlane.Api.Features.Adapters.Proxmox;

/// <summary>
/// Proxmox VE connection and adapter configuration options.
/// </summary>
public class ProxmoxOptions
{
    public const string SectionName = "Proxmox";

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiTokenId { get; set; } = string.Empty;

    public string ApiTokenSecret { get; set; } = string.Empty;

    public bool AllowSelfSignedCert { get; set; } = true;

    public int TaskPollTimeoutSeconds { get; set; } = 300;

    public int TaskPollIntervalMilliseconds { get; set; } = 1000;
}

/// <summary>
/// Response returned by Proxmox asynchronous operations containing a task UPID.
/// </summary>
public record ProxmoxTaskResponse(
    [property: JsonPropertyName("data")] string Data
);

/// <summary>
/// Status descriptor for an asynchronous Proxmox task.
/// </summary>
public record ProxmoxTaskStatus(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("exitstatus")] string? ExitStatus = null,
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("node")] string? Node = null,
    [property: JsonPropertyName("type")] string? Type = null,
    [property: JsonPropertyName("user")] string? User = null,
    [property: JsonPropertyName("starttime")] long? StartTime = null
)
{
    [JsonIgnore]
    public bool IsStopped => string.Equals(Status, "stopped", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsSuccess => IsStopped && (string.IsNullOrEmpty(ExitStatus) || string.Equals(ExitStatus, "OK", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Response envelope for task status queries.
/// </summary>
public record ProxmoxTaskStatusResponse(
    [property: JsonPropertyName("data")] ProxmoxTaskStatus Data
);

/// <summary>
/// Request payload for creating VM/LXC snapshots.
/// </summary>
public record ProxmoxSnapshotRequest(
    [property: JsonPropertyName("snapname")] string SnapName,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("vmstate")] bool? VmState = null
);
