namespace ControlPlane.Api.Storage.Entities;

/// <summary>
/// Represents a distributed maintenance lease for coordinating cluster takeover and delta synchronization.
/// </summary>
public class ClusterLease
{
    public string LeaseKey { get; set; } = string.Empty;

    public string HolderIdentifier { get; set; } = string.Empty;

    public DateTimeOffset AcquiredAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
