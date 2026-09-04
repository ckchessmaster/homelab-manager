namespace ControlPlane.Api.Storage.Entities;

/// <summary>
/// Represents a managed host in the homelab inventory.
/// </summary>
public class Host
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Hostname { get; set; } = string.Empty;

    public string? FriendlyName { get; set; }

    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// Operating system family, e.g. 'linux_debian', 'linux_rhel', 'windows'.
    /// </summary>
    public string OsFamily { get; set; } = string.Empty;

    /// <summary>
    /// Target type, e.g. 'baremetal', 'proxmox_vm', 'proxmox_lxc'.
    /// </summary>
    public string TargetType { get; set; } = string.Empty;

    // Hypervisor & Out-of-Band Correlation Targets
    public ProxmoxTarget? Proxmox { get; set; }

    public IdracTarget? Idrac { get; set; }

    public UnifiPortTarget? NetworkPort { get; set; }

    // Runtime Agent State
    public AgentState Agent { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigations
    public ICollection<UpdateJob> UpdateJobs { get; set; } = new List<UpdateJob>();
}
