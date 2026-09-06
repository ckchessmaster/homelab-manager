using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using Temporalio.Activities;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities;

public interface IProxmoxActivities
{
    [Activity]
    Task<ProxmoxSnapshotResult> CreateSnapshotAsync(ProxmoxSnapshotInput input);

    [Activity]
    Task<ProxmoxRollbackResult> RollbackSnapshotAsync(ProxmoxRollbackInput input);
}
