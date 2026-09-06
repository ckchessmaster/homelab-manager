using ControlPlane.Api.Features.Orchestration.Temporal.Workflows.Models;
using Temporalio.Workflows;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Workflows;

[Workflow]
public interface IRollingUpgradeWorkflow
{
    [WorkflowRun]
    Task<RollingUpgradeWorkflowResult> RunAsync(RollingUpgradeWorkflowInput input);

    [WorkflowSignal]
    Task PauseAsync();

    [WorkflowSignal]
    Task ResumeAsync();

    [WorkflowSignal]
    Task CancelAsync(string? reason = null);

    [WorkflowQuery]
    RollingUpgradeWorkflowState GetWorkflowState();
}
