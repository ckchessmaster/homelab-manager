namespace ControlPlane.Api.Storage.Entities;

/// <summary>
/// Represents an update or maintenance job running against a target host in the DAG state machine.
/// </summary>
public class UpdateJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TargetHostId { get; set; }

    public string PipelineId { get; set; } = "standard-os-upgrade";

    public string InitiatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Job status: 'Pending', 'Running', 'Verifying', 'Completed', 'Failed', 'RolledBack'.
    /// </summary>
    public string Status { get; set; } = "Pending";

    public string? ActiveStep { get; set; }

    public string? SnapshotIdentifier { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? FailureReason { get; set; }

    // Navigations
    public Host TargetHost { get; set; } = null!;

    public ICollection<StepLog> StepLogs { get; set; } = new List<StepLog>();
}
