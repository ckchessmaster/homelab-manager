namespace ControlPlane.Api.Storage.Entities;

/// <summary>
/// Correlation details for hosts managed under Proxmox VE (VMs, LXCs, or hypervisor nodes).
/// </summary>
public class ProxmoxTarget
{
    public string Node { get; set; } = string.Empty;

    public int Vmid { get; set; }
}
