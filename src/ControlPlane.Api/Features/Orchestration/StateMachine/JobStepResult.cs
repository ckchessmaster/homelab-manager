namespace ControlPlane.Api.Features.Orchestration;

/// <summary>
/// Encapsulates the execution outcome of an individual DAG step.
/// </summary>
public record JobStepResult(
    bool Success,
    string? Message = null,
    string? TargetState = null,
    Exception? Exception = null)
{
    public static JobStepResult Succeeded(string? message = null, string? targetState = null) =>
        new(true, message, targetState);

    public static JobStepResult Failed(string message, Exception? exception = null) =>
        new(false, message, null, exception);
}
