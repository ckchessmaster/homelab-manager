namespace ControlPlane.Api.Storage.Entities;

/// <summary>
/// Runtime agent state and metrics reported by the node daemon.
/// </summary>
public class AgentState
{
    public bool Installed { get; set; }

    public string? Version { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public bool PendingReboot { get; set; }

    public int UpgradablePackagesCount { get; set; }
}
