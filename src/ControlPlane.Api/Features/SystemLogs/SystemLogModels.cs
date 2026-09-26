namespace ControlPlane.Api.Features.SystemLogs;

public record SystemLogEntry(
    long Id,
    DateTimeOffset Timestamp,
    string LogLevel,
    string Category,
    string Message,
    string? Exception = null,
    int? EventId = null,
    string? EventName = null
);

public record SystemLogStats(
    int TotalCount,
    int ErrorCount,
    int WarningCount,
    int InfoCount,
    int DebugCount,
    int Capacity
);

public record SystemLogResponse(
    IReadOnlyList<SystemLogEntry> Logs,
    SystemLogStats Stats,
    int TotalAvailable
);

public record SystemInfoDto(
    string OsDescription,
    string FrameworkDescription,
    string MachineName,
    int ProcessorCount,
    TimeSpan Uptime,
    long WorkingSetBytes,
    DateTimeOffset ServerTimeUtc,
    string EnvironmentName
);
