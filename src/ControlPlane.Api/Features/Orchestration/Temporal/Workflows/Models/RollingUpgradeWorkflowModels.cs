namespace ControlPlane.Api.Features.Orchestration.Temporal.Workflows.Models;

public record RollingHostTarget(
    Guid HostId,
    string Hostname,
    string? TargetType = null,
    string? OsFamily = null,
    string? K8sNodeName = null);

public record RollingUpgradeWorkflowInput(
    Guid BatchId,
    List<RollingHostTarget> TargetHosts,
    int MaxParallelism = 1,
    string FailureStrategy = "StopOnFirstFailure",
    bool RequireApprovalBeforeReboot = false,
    bool RequireApprovalBetweenHosts = false,
    bool AlwaysReboot = false,
    List<string>? ProbeUrls = null,
    string? SnapshotPrefix = null,
    string InitiatedBy = "Operator");

public class RollingHostProgress
{
    public Guid HostId { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Running, Completed, Failed, Skipped
    public string? ChildWorkflowId { get; set; }
    public string? CurrentStep { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class RollingUpgradeWorkflowState
{
    public Guid BatchId { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Running, Paused, Completed, Failed, Cancelled
    public int TotalHosts { get; set; }
    public int CompletedHosts { get; set; }
    public int FailedHosts { get; set; }
    public Guid? ActiveHostId { get; set; }
    public string? ActiveHostname { get; set; }
    public bool IsPaused { get; set; }
    public bool Cancelled { get; set; }
    public string? CancelReason { get; set; }
    public string? FailureReason { get; set; }
    public Dictionary<string, RollingHostProgress> HostProgresses { get; set; } = new();
}

public record RollingUpgradeWorkflowResult(
    bool Success,
    string Status,
    int TotalHosts,
    int CompletedHosts,
    int FailedHosts,
    string? ErrorMessage = null);
