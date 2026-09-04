using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ControlPlane.Api.Storage.Configurations;

public class UpdateJobConfiguration : IEntityTypeConfiguration<UpdateJob>
{
    public void Configure(EntityTypeBuilder<UpdateJob> builder)
    {
        builder.ToTable("update_jobs");

        builder.HasKey(j => j.Id);

        builder.Property(j => j.InitiatedBy)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(j => j.Status)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(j => j.ActiveStep)
            .HasMaxLength(100);

        builder.Property(j => j.SnapshotIdentifier)
            .HasMaxLength(255);

        builder.HasMany(j => j.StepLogs)
            .WithOne(l => l.Job)
            .HasForeignKey(l => l.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
