namespace ControlPlane.Api.Features.Orchestration.Temporal.Workflows.Models;

public record HostUpgradeWorkflowInput(
    Guid JobId,
    Guid HostId,
    string Hostname,
    string? TargetType = null,
    string? OsFamily = null,
    bool RequireApprovalBeforeReboot = false,
    bool AlwaysReboot = false,
    TimeSpan? ApprovalTimeout = null,
    List<string>? ProbeUrls = null,
    string? SnapshotName = null,
    string? K8sNodeName = null);

public record HostUpgradeWorkflowResult(
    bool Success,
    string Status,
    List<string> CompletedSteps,
    string? SnapshotIdentifier,
    string? ErrorMessage);

public class HostUpgradeWorkflowState
{
    public string Status { get; set; } = "Pending";
    public string? ActiveStep { get; set; }
    public List<string> CompletedSteps { get; set; } = new();
    public List<string> SkippedSteps { get; set; } = new();
    public bool AwaitingApproval { get; set; }
    public bool RebootApproved { get; set; }
    public bool Cancelled { get; set; }
    public string? CancelReason { get; set; }
    public string? SnapshotIdentifier { get; set; }
    public string? K8sNodeName { get; set; }
    public string? FailureReason { get; set; }
}
