using System.Text.Json.Serialization;

namespace ControlPlane.Api.Features.Adapters.HomeAssistant;

/// <summary>
/// Standard Home Assistant API response envelope.
/// </summary>
public record HassEnvelope<T>(
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("message")] string? Message
);

public record HomeAssistantHostInfoDto(
    [property: JsonPropertyName("chassis")] string? Chassis,
    [property: JsonPropertyName("hostname")] string? Hostname,
    [property: JsonPropertyName("kernel")] string? Kernel,
    [property: JsonPropertyName("operatingSystem")] string? OperatingSystem,
    [property: JsonPropertyName("rebootRequired")] bool RebootRequired,
    [property: JsonPropertyName("diskFreeGb")] double? DiskFreeGb,
    [property: JsonPropertyName("diskTotalGb")] double? DiskTotalGb,
    [property: JsonPropertyName("diskUsedGb")] double? DiskUsedGb
);

public record HomeAssistantOsInfoDto(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("versionLatest")] string? VersionLatest,
    [property: JsonPropertyName("updateAvailable")] bool UpdateAvailable,
    [property: JsonPropertyName("board")] string? Board,
    [property: JsonPropertyName("bootSlot")] string? BootSlot
);

public record HomeAssistantCoreInfoDto(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("versionLatest")] string? VersionLatest,
    [property: JsonPropertyName("updateAvailable")] bool UpdateAvailable,
    [property: JsonPropertyName("arch")] string? Arch,
    [property: JsonPropertyName("state")] string? State
);

public record HomeAssistantSupervisorInfoDto(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("versionLatest")] string? VersionLatest,
    [property: JsonPropertyName("updateAvailable")] bool UpdateAvailable,
    [property: JsonPropertyName("channel")] string? Channel,
    [property: JsonPropertyName("healthy")] bool Healthy,
    [property: JsonPropertyName("supported")] bool Supported
);

public record HomeAssistantBackupDto(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("date")] DateTimeOffset Date,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("sizeMb")] double SizeMb,
    [property: JsonPropertyName("protected")] bool Protected
);

public record HomeAssistantOverviewDto(
    [property: JsonPropertyName("instanceId")] string InstanceId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("baseUrl")] string BaseUrl,
    [property: JsonPropertyName("host")] HomeAssistantHostInfoDto? Host,
    [property: JsonPropertyName("os")] HomeAssistantOsInfoDto? Os,
    [property: JsonPropertyName("core")] HomeAssistantCoreInfoDto? Core,
    [property: JsonPropertyName("supervisor")] HomeAssistantSupervisorInfoDto? Supervisor,
    [property: JsonPropertyName("recentBackups")] List<HomeAssistantBackupDto> RecentBackups,
    [property: JsonPropertyName("correlatedHostId")] Guid? CorrelatedHostId = null,
    [property: JsonPropertyName("correlatedHostName")] string? CorrelatedHostName = null,
    [property: JsonPropertyName("latencyMs")] long LatencyMs = 0,
    [property: JsonPropertyName("fetchedAt")] DateTimeOffset? FetchedAt = null
);

public record HomeAssistantConfigCheckResultDto(
    [property: JsonPropertyName("isValid")] bool IsValid,
    [property: JsonPropertyName("errors")] string? Errors,
    [property: JsonPropertyName("checkedAt")] DateTimeOffset CheckedAt
);

public record CreateHomeAssistantBackupRequest(
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("password")] string? Password = null
);

public record CreateHomeAssistantBackupResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("jobId")] string? JobId,
    [property: JsonPropertyName("slug")] string? Slug,
    [property: JsonPropertyName("message")] string? Message
);
