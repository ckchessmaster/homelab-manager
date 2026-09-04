namespace ControlPlane.Api.Features.Orchestration;

/// <summary>
/// Orchestrates sequential DAG step execution, state transitions, and automated rollback handling.
/// </summary>
public class DagExecutionPipeline
{
    private readonly IReadOnlyList<IJobStep> _steps;

    public DagExecutionPipeline(IEnumerable<IJobStep> steps)
    {
        _steps = steps.ToList();
    }

    public IReadOnlyList<IJobStep> Steps => _steps;

    public async Task<bool> ExecuteAsync(JobExecutionContext context, CancellationToken ct = default)
    {
        var executedSteps = new Stack<IJobStep>();

        await context.EmitLogAsync(
            "system",
            $"Initiating update pipeline with {_steps.Count} steps for host '{context.TargetHost.Hostname}'.",
            ct
        );

        await context.UpdateJobStatusAsync(UpdateJobState.Running, _steps.FirstOrDefault()?.StepName, ct: ct);

        foreach (var step in _steps)
        {
            if (ct.IsCancellationRequested)
            {
                await context.EmitLogAsync("system", "Pipeline execution canceled by operator.", ct);
                await context.UpdateJobStatusAsync(UpdateJobState.Failed, null, "Execution canceled", ct);
                return false;
            }

            await context.UpdateJobStatusAsync(context.Job.Status, step.StepName, ct: ct);
            await context.EmitLogAsync("system", $"\u25b6 Starting step: {step.StepName}", ct);

            JobStepResult result;
            try
            {
                result = await step.ExecuteAsync(context, ct);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Unhandled exception executing step {StepName}", step.StepName);
                result = JobStepResult.Failed($"Step encountered unhandled exception: {ex.Message}", ex);
            }

            if (result.Success)
            {
                executedSteps.Push(step);
                var targetState = result.TargetState ?? context.Job.Status;
                await context.UpdateJobStatusAsync(targetState, step.StepName, ct: ct);

                var msg = string.IsNullOrWhiteSpace(result.Message)
                    ? $"Step '{step.StepName}' completed successfully."
                    : $"Step '{step.StepName}' completed: {result.Message}";
                await context.EmitLogAsync("system", $"\u2714 {msg}", ct);
            }
            else
            {
                var failureMsg = result.Message ?? "Unknown step failure";
                await context.EmitLogAsync("system", $"\u2716 Step '{step.StepName}' failed: {failureMsg}", ct);

                // Execute rollback in reverse order for already completed steps
                if (executedSteps.Count > 0)
                {
                    await context.EmitLogAsync("system", $"\u26a0 Initiating rollback for {executedSteps.Count} executed steps...", ct);

                    while (executedSteps.Count > 0)
                    {
                        var executed = executedSteps.Pop();
                        await context.EmitLogAsync("system", $"Rolling back step: {executed.StepName}...", ct);
                        try
                        {
                            await executed.RollbackAsync(context, ct);
                            await context.EmitLogAsync("system", $"Rollback finished for: {executed.StepName}", ct);
                        }
                        catch (Exception rollbackEx)
                        {
                            context.Logger.LogError(rollbackEx, "Error rolling back step {StepName}", executed.StepName);
                            await context.EmitLogAsync("system", $"Rollback error on {executed.StepName}: {rollbackEx.Message}", ct);
                        }
                    }
                }

                await context.UpdateJobStatusAsync(UpdateJobState.Failed, null, failureReason: failureMsg, ct: ct);
                return false;
            }
        }

        await context.UpdateJobStatusAsync(UpdateJobState.Completed, null, ct: ct);
        await context.EmitLogAsync("system", $"Pipeline finished successfully for host '{context.TargetHost.Hostname}'.", ct);
        return true;
    }
}
