using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using Temporalio.Activities;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities;

public interface IPreflightActivities
{
    [Activity]
    Task<PreflightCheckResult> CheckHeartbeatAsync(PreflightHeartbeatInput input);

    [Activity]
    Task<PreflightCheckResult> CheckDiskHeadroomAsync(PreflightDiskHeadroomInput input);

    [Activity]
    Task<PreflightCheckResult> CheckPackageLockAsync(PreflightPackageLockInput input);
}
