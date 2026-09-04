namespace ControlPlane.Api.Storage.Entities;

/// <summary>
/// Out-of-band management coordinates for Dell PowerEdge servers via iDRAC/Redfish.
/// </summary>
public class IdracTarget
{
    public string IpAddress { get; set; } = string.Empty;
}
