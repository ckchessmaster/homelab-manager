using HostEntity = ControlPlane.Api.Storage.Entities.Host;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ControlPlane.Api.Storage.Configurations;

public class HostConfiguration : IEntityTypeConfiguration<HostEntity>
{
    public void Configure(EntityTypeBuilder<HostEntity> builder)
    {
        builder.ToTable("hosts");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Hostname)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(h => h.FriendlyName)
            .HasMaxLength(255);

        builder.Property(h => h.IpAddress)
            .HasMaxLength(45)
            .IsRequired();

        builder.Property(h => h.OsFamily)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(h => h.TargetType)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(h => h.Hostname)
            .IsUnique();

        builder.HasIndex(h => h.IpAddress)
            .IsUnique();

        builder.OwnsOne(h => h.Proxmox, p =>
        {
            p.Property(x => x.Node)
                .HasColumnName("proxmox_node")
                .HasMaxLength(100);

            p.Property(x => x.Vmid)
                .HasColumnName("proxmox_vmid");
        });

        builder.OwnsOne(h => h.Idrac, i =>
        {
            i.Property(x => x.IpAddress)
                .HasColumnName("idrac_ip")
                .HasMaxLength(45);
        });

        builder.OwnsOne(h => h.NetworkPort, n =>
        {
            n.Property(x => x.SwitchMac)
                .HasColumnName("unifi_switch_mac")
                .HasMaxLength(17);

            n.Property(x => x.PortNumber)
                .HasColumnName("unifi_switch_port");
        });

        builder.OwnsOne(h => h.Agent, a =>
        {
            a.Property(x => x.Installed)
                .HasColumnName("agent_installed");

            a.Property(x => x.Version)
                .HasColumnName("agent_version")
                .HasMaxLength(30);

            a.Property(x => x.LastSeenAt)
                .HasColumnName("agent_last_seen_at");

            a.Property(x => x.PendingReboot)
                .HasColumnName("pending_reboot");

            a.Property(x => x.UpgradablePackagesCount)
                .HasColumnName("upgradable_packages_count");
        });

        builder.HasMany(h => h.UpdateJobs)
            .WithOne(j => j.TargetHost)
            .HasForeignKey(j => j.TargetHostId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
