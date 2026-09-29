namespace ControlPlane.Api.Features.Demo;

/// <summary>
/// Configuration options for ControlPlane Demo Mode.
/// </summary>
public class DemoOptions
{
    public const string SectionName = "Demo";

    /// <summary>
    /// Indicates whether demo mode simulation is active.
    /// Can also be enabled via DEMO_MODE=true environment variable.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Interval in seconds between background simulation ticks (vitals jitter, heartbeat sync).
    /// </summary>
    public int SimulationTickSeconds { get; set; } = 3;

    /// <summary>
    /// Indicates whether to automatically seed demo data on startup.
    /// </summary>
    public bool AutoSeed { get; set; } = true;
}
