using ControlPlane.Api.Features.Orchestration.Temporal.Activities.Models;
using Temporalio.Activities;

namespace ControlPlane.Api.Features.Orchestration.Temporal.Activities;

public class UpdateJobActivities : IUpdateJobActivities
{
    private readonly IWorkflowLogEmitter _logEmitter;
    private readonly ILogger<UpdateJobActivities> _logger;

    public UpdateJobActivities(
        IWorkflowLogEmitter logEmitter,
        ILogger<UpdateJobActivities> logger)
    {
        _logEmitter = logEmitter;
        _logger = logger;
    }

    [Activity]
    public async Task UpdateJobStatusAsync(UpdateJobStatusInput input)
    {
        _logger.LogInformation("Updating job {JobId} status to {Status}, step: {Step}", input.JobId, input.Status, input.ActiveStep);
        await _logEmitter.UpdateJobStatusAsync(input.JobId, input.Status, input.ActiveStep, input.FailureReason);
        if (!string.IsNullOrWhiteSpace(input.ActiveStep))
        {
            await _logEmitter.EmitLogAsync(input.JobId, "system", $"[PIPELINE] Entering step: {input.ActiveStep} (State: {input.Status})");
        }
    }

    [Activity]
    public async Task RecordJobCompletionAsync(RecordJobCompletionInput input)
    {
        _logger.LogInformation("Recording job {JobId} completion with status {Status}", input.JobId, input.Status);
        await _logEmitter.UpdateJobStatusAsync(input.JobId, input.Status, null, input.FailureReason);
        await _logEmitter.EmitLogAsync(
            input.JobId,
            "system",
            $"[PIPELINE] Workflow reached terminal status: {input.Status}. {(string.IsNullOrWhiteSpace(input.FailureReason) ? "" : "Reason: " + input.FailureReason)}"
        );
    }
}
