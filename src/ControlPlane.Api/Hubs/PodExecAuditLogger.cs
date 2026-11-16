namespace ControlPlane.Api.Hubs;

public static class PodExecAuditActions
{
    public const string SessionStarted = "SessionStarted";
    public const string SessionEnded = "SessionEnded";
    public const string AttachFailed = "AttachFailed";
}

/// <summary>
/// Structured audit record for interactive pod terminal session lifecycle events.
/// Raw stdin keystrokes and stdout/stderr terminal streams are intentionally never recorded
/// to prevent accidental capture of credentials or sensitive output.
/// </summary>
public sealed record PodExecAuditEntry(
    Guid SessionId,
    string Action,
    string ConnectionId,
    string PrincipalName,
    IReadOnlyList<string> Roles,
    string? AuthMethod,
    string? RemoteIp,
    string ClusterId,
    string Namespace,
    string PodName,
    string? Container,
    string? Shell,
    DateTimeOffset TimestampUtc,
    TimeSpan? Duration = null,
    int? ExitCode = null,
    string? Reason = null);

public interface IPodExecAuditLogger
{
    void LogSessionStarted(PodExecAuditEntry entry);
    void LogSessionEnded(PodExecAuditEntry entry);
    void LogAttachFailed(PodExecAuditEntry entry);
    IReadOnlyList<PodExecAuditEntry> GetRecentEntries(int limit = 200);
}

public sealed class PodExecAuditLogger : IPodExecAuditLogger
{
    public static readonly EventId SessionStartedEventId = new(4100, "PodExecSessionStarted");
    public static readonly EventId SessionEndedEventId = new(4101, "PodExecSessionEnded");
    public static readonly EventId AttachFailedEventId = new(4102, "PodExecAttachFailed");

    private readonly ILogger<PodExecAuditLogger> _logger;
    private readonly int _capacity;
    private readonly Lock _lock = new();
    private readonly LinkedList<PodExecAuditEntry> _entries = new();

    public PodExecAuditLogger(ILogger<PodExecAuditLogger> logger, int capacity = 1000)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _capacity = capacity > 0 ? capacity : 1000;
    }

    public void LogSessionStarted(PodExecAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Enqueue(entry);

        _logger.LogInformation(
            SessionStartedEventId,
            "AUDIT PodExec {Action}: session={SessionId} user={User} roles=[{Roles}] auth={AuthMethod} ip={RemoteIp} target={ClusterId}/{Namespace}/{PodName} container={Container} shell={Shell} connection={ConnectionId}",
            entry.Action,
            entry.SessionId,
            entry.PrincipalName,
            string.Join(",", entry.Roles),
            entry.AuthMethod ?? "unknown",
            entry.RemoteIp ?? "unknown",
            entry.ClusterId,
            entry.Namespace,
            entry.PodName,
            entry.Container ?? "<default>",
            entry.Shell ?? "<auto>",
            entry.ConnectionId);
    }

    public void LogSessionEnded(PodExecAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Enqueue(entry);

        _logger.LogInformation(
            SessionEndedEventId,
            "AUDIT PodExec {Action}: session={SessionId} user={User} roles=[{Roles}] target={ClusterId}/{Namespace}/{PodName} container={Container} shell={Shell} durationMs={DurationMs} exitCode={ExitCode} reason={Reason} connection={ConnectionId}",
            entry.Action,
            entry.SessionId,
            entry.PrincipalName,
            string.Join(",", entry.Roles),
            entry.ClusterId,
            entry.Namespace,
            entry.PodName,
            entry.Container ?? "<default>",
            entry.Shell ?? "<auto>",
            entry.Duration.HasValue ? Math.Round(entry.Duration.Value.TotalMilliseconds, 1) : 0,
            entry.ExitCode?.ToString() ?? "n/a",
            entry.Reason ?? "Closed",
            entry.ConnectionId);
    }

    public void LogAttachFailed(PodExecAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Enqueue(entry);

        _logger.LogWarning(
            AttachFailedEventId,
            "AUDIT PodExec {Action}: session={SessionId} user={User} roles=[{Roles}] auth={AuthMethod} ip={RemoteIp} target={ClusterId}/{Namespace}/{PodName} container={Container} shell={Shell} reason={Reason} connection={ConnectionId}",
            entry.Action,
            entry.SessionId,
            entry.PrincipalName,
            string.Join(",", entry.Roles),
            entry.AuthMethod ?? "unknown",
            entry.RemoteIp ?? "unknown",
            entry.ClusterId,
            entry.Namespace,
            entry.PodName,
            entry.Container ?? "<default>",
            entry.Shell ?? "<auto>",
            entry.Reason ?? "Unknown error",
            entry.ConnectionId);
    }

    public IReadOnlyList<PodExecAuditEntry> GetRecentEntries(int limit = 200)
    {
        var effectiveLimit = Math.Clamp(limit, 1, _capacity);
        lock (_lock)
        {
            return _entries.TakeLast(effectiveLimit).ToList();
        }
    }

    private void Enqueue(PodExecAuditEntry entry)
    {
        lock (_lock)
        {
            if (_entries.Count >= _capacity)
            {
                _entries.RemoveFirst();
            }

            _entries.AddLast(entry);
        }
    }
}
