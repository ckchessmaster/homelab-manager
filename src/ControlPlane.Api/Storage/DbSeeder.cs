using HostEntity = ControlPlane.Api.Storage.Entities.Host;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Storage;

public static class DbSeeder
{
    public static async Task SeedStandbyAsync(ControlPlaneDbContext context, CancellationToken cancellationToken = default)
    {
        if (await context.Hosts.AnyAsync(cancellationToken))
        {
            return;
        }

        var sampleHost1 = new HostEntity
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Hostname = "k8s-control-01",
            FriendlyName = "K8s Control Plane 01",
            IpAddress = "192.168.1.10",
            OsFamily = "linux_debian",
            TargetType = "proxmox_vm",
            Proxmox = new ProxmoxTarget
            {
                Node = "pve-node-01",
                Vmid = 101
            },
            Agent = new AgentState
            {
                Installed = true,
                Version = "1.0.0",
                LastSeenAt = DateTimeOffset.UtcNow,
                PendingReboot = false,
                UpgradablePackagesCount = 2
            },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var sampleHost2 = new HostEntity
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Hostname = "pve-node-01",
            FriendlyName = "Proxmox Hypervisor Primary",
            IpAddress = "192.168.1.20",
            OsFamily = "linux_debian",
            TargetType = "baremetal",
            Idrac = new IdracTarget
            {
                IpAddress = "192.168.1.120"
            },
            NetworkPort = new UnifiPortTarget
            {
                SwitchMac = "00:11:22:33:44:55",
                PortNumber = 5
            },
            Agent = new AgentState
            {
                Installed = true,
                Version = "1.0.0",
                LastSeenAt = DateTimeOffset.UtcNow,
                PendingReboot = true,
                UpgradablePackagesCount = 5
            },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var sampleLease = new ClusterLease
        {
            LeaseKey = "GLOBAL_MAINTENANCE_LOCK",
            HolderIdentifier = "controlplane-standby",
            AcquiredAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };

        await context.Hosts.AddRangeAsync(new[] { sampleHost1, sampleHost2 }, cancellationToken);
        await context.ClusterLeases.AddAsync(sampleLease, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}
