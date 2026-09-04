namespace ControlPlane.Api.Features.Cluster;

/// <summary>
/// Tracks in-memory runtime cluster state, including whether the cluster scheduler is suspended
/// due to an active takeover lease held by a standby runner.
/// </summary>
public class ClusterState
{
    private volatile bool _isSuspended;

    public bool IsSuspended
    {
        get => _isSuspended;
        set => _isSuspended = value;
    }

    public string? CurrentLeaseHolder { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }
}
