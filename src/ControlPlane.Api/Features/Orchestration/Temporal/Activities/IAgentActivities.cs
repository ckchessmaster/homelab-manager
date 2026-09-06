using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using Temporalio.Activities;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities;

public interface IAgentActivities
{
    [Activity]
    Task<AgentUpgradeResult> UpgradePackagesAsync(AgentUpgradeInput input);

    [Activity]
    Task<AgentRebootResult> InitiateRebootAsync(AgentRebootInput input);

    [Activity]
    Task<AgentReconnectResult> AwaitReconnectionAsync(AgentReconnectInput input);
}
