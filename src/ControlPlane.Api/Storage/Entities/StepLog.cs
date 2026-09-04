namespace ControlPlane.Api.Storage.Entities;

/// <summary>
/// Represents a framed, sequence-ordered console log line emitted during job execution.
/// </summary>
public class StepLog
{
    public long Id { get; set; }

    public Guid JobId { get; set; }

    public long SequenceId { get; set; }

    /// <summary>
    /// Stream type: 'stdout', 'stderr', or 'system'.
    /// </summary>
    public string StreamType { get; set; } = "stdout";

    public string LogLine { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    // Navigations
    public UpdateJob Job { get; set; } = null!;
}
