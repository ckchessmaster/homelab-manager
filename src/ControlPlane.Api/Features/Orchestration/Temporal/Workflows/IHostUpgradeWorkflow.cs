using ControlPlane.Api.Features.Orchestration.Temporal.Workflows.Models;
using Temporalio.Workflows;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Workflows;

[Workflow]
public interface IHostUpgradeWorkflow
{
    [WorkflowRun]
    Task<HostUpgradeWorkflowResult> RunAsync(HostUpgradeWorkflowInput input);

    [WorkflowSignal]
    Task ApproveRebootAsync();

    [WorkflowSignal]
    Task CancelAsync(string? reason = null);

    [WorkflowQuery]
    HostUpgradeWorkflowState GetWorkflowState();
}
