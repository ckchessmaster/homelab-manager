namespace ControlPlane.Api.Features.Orchestration;

/// <summary>
/// Defines an atomic, verifiable step in the update DAG pipeline.
/// </summary>
public interface IJobStep
{
    /// <summary>
    /// Descriptive name of the step (e.g. 'Preflight: Heartbeat Freshness').
    /// </summary>
    string StepName { get; }

    /// <summary>
    /// Executes the primary step logic against the target host.
    /// </summary>
    Task<JobStepResult> ExecuteAsync(JobExecutionContext context, CancellationToken ct);

    /// <summary>
    /// Reverses mutations performed by this step if a subsequent step in the DAG fails.
    /// </summary>
    Task RollbackAsync(JobExecutionContext context, CancellationToken ct);
}
