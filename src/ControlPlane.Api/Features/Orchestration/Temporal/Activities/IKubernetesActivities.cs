using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using Temporalio.Activities;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities;

public interface IKubernetesActivities
{
    [Activity]
    Task<KubernetesCordonResult> CordonNodeAsync(KubernetesNodeInput input);

    [Activity]
    Task<KubernetesDrainResult> DrainNodeAsync(KubernetesDrainInput input);

    [Activity]
    Task<KubernetesUncordonResult> UncordonNodeAsync(KubernetesNodeInput input);
}
