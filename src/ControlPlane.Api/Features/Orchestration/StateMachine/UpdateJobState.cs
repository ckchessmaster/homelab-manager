namespace ControlPlane.Api.Features.Orchestration;

/// <summary>
/// Defines the lifecycle states for an update job orchestrated by the DAG state machine.
/// </summary>
public static class UpdateJobState
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Verifying = "Verifying";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string RolledBack = "RolledBack";
}
