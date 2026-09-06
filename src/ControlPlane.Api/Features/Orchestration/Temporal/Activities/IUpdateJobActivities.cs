using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using Temporalio.Activities;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities;

public interface IUpdateJobActivities
{
    [Activity]
    Task UpdateJobStatusAsync(UpdateJobStatusInput input);

    [Activity]
    Task RecordJobCompletionAsync(RecordJobCompletionInput input);
}
