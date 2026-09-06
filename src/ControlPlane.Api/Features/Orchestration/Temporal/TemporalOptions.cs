namespace ControlPlane.Api.Features.Orchestration.Temporal;

public class TemporalOptions
{
    public const string SectionName = "Temporal";

    /// <summary>
    /// Whether Temporal workflow orchestration is active.
    /// Defaults to true; can be disabled in Standby mode or unit tests.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Temporal server host and port (e.g. "localhost:7233").
    /// </summary>
    public string ServerUrl { get; set; } = "localhost:7233";

    /// <summary>
    /// Temporal target namespace.
    /// </summary>
    public string Namespace { get; set; } = "default";

    /// <summary>
    /// Default task queue name for host update workflows.
    /// </summary>
    public string TaskQueue { get; set; } = "controlplane-orchestration";
}
