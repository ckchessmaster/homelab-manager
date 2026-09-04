using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ControlPlane.Api.Storage.Configurations;

public class ClusterLeaseConfiguration : IEntityTypeConfiguration<ClusterLease>
{
    public void Configure(EntityTypeBuilder<ClusterLease> builder)
    {
        builder.ToTable("cluster_leases");

        builder.HasKey(c => c.LeaseKey);

        builder.Property(c => c.LeaseKey)
            .HasMaxLength(100);

        builder.Property(c => c.HolderIdentifier)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(c => c.AcquiredAt)
            .IsRequired();

        builder.Property(c => c.ExpiresAt)
            .IsRequired();
    }
}
