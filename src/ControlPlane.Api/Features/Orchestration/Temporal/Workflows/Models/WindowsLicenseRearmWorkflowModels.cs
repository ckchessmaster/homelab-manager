namespace ControlPlane.Api.Features.Orchestration.Temporal.Workflows.Models;

public record WindowsLicenseRearmWorkflowInput(
    Guid JobId,
    Guid HostId,
    string Hostname,
    bool RebootAfter = false,
    string InitiatedBy = "Operator"
);

public record WindowsLicenseRearmWorkflowResult(
    bool Success,
    string Status,
    List<string> CompletedSteps,
    string Message
);

public class WindowsLicenseRearmWorkflowState
{
    public string Status { get; set; } = "Pending";
    public string? ActiveStep { get; set; }
    public List<string> CompletedSteps { get; set; } = new();
    public int RearmsRemaining { get; set; }
    public int DaysRemaining { get; set; }
    public bool RebootRequested { get; set; }
    public string? FailureReason { get; set; }
}
