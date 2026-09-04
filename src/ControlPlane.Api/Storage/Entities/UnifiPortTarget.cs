namespace ControlPlane.Api.Storage.Entities;

/// <summary>
/// Network switch port mapping on Ubiquiti UniFi switches for PoE power cycling and port control.
/// </summary>
public class UnifiPortTarget
{
    public string SwitchMac { get; set; } = string.Empty;

    public int PortNumber { get; set; }
}
