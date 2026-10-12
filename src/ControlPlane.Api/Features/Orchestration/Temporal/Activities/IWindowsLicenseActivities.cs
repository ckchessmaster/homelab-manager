using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using Temporalio.Activities;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities;

public interface IWindowsLicenseActivities
{
    [Activity]
    Task<WindowsLicensePreflightResult> PreflightCheckAsync(WindowsLicensePreflightInput input);

    [Activity]
    Task<WindowsLicenseRearmExecutionResult> ExecuteRearmAsync(WindowsLicenseRearmExecutionInput input);

    [Activity]
    Task<WindowsLicenseRefreshResult> RefreshLicenseStatusAsync(WindowsLicenseRefreshInput input);
}
