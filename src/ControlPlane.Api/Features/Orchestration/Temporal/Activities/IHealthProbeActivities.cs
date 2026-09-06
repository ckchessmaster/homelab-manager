using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using Temporalio.Activities;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities;

public interface IHealthProbeActivities
{
    [Activity]
    Task<HealthProbeResult> RunHealthProbesAsync(HealthProbeInput input);
}
