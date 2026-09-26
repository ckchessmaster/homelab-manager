namespace ControlPlane.Api.Features.Agents.Models;

public record AgentBinaryPlatformDto(
    string Architecture,
    string DisplayName,
    string FileName,
    bool IsAvailable,
    long? SizeBytes,
    DateTimeOffset? LastModifiedAtUtc,
    string DownloadPath
);

public record AgentBinaryStatusDto(
    string CurrentInstalledVersion,
    string? LatestAvailableVersion,
    DateTimeOffset? LastCheckedAtUtc,
    DateTimeOffset? LastDownloadedAtUtc,
    string Status,
    string? LastError,
    bool IsSyncing,
    bool AutoSyncEnabled,
    int SyncIntervalHours,
    string Repository,
    IReadOnlyList<AgentBinaryPlatformDto> Platforms
);

public record AgentBinarySyncRequest(
    bool Force = false
);

public record AgentBinarySyncResultDto(
    bool Success,
    string? Version,
    string Message,
    IReadOnlyList<string> UpdatedBinaries,
    AgentBinaryStatusDto Status
);

public class AgentBinarySyncOptions
{
    public const string SectionName = "ControlPlane:AgentBinarySync";

    public string Repository { get; set; } = "ckchessmaster/homelab-manager";
    public bool Enabled { get; set; } = true;
    public bool CheckOnStartup { get; set; } = true;
    public int SyncIntervalHours { get; set; } = 6;
    public string? AgentDistDir { get; set; }
    public string? GitHubToken { get; set; }
}
