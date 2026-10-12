using ControlPlane.Api.Features.Orchestration.Temporal.Workflows.Models;
using Temporalio.Workflows;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Workflows;

[Workflow]
public interface IWindowsLicenseRearmWorkflow
{
    [WorkflowRun]
    Task<WindowsLicenseRearmWorkflowResult> RunAsync(WindowsLicenseRearmWorkflowInput input);

    [WorkflowQuery]
    WindowsLicenseRearmWorkflowState GetWorkflowState();
}
