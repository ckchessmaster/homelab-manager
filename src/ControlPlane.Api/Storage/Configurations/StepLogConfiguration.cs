using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ControlPlane.Api.Storage.Configurations;

public class StepLogConfiguration : IEntityTypeConfiguration<StepLog>
{
    public void Configure(EntityTypeBuilder<StepLog> builder)
    {
        builder.ToTable("step_logs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.StreamType)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(l => l.LogLine)
            .IsRequired();

        builder.HasIndex(l => new { l.JobId, l.SequenceId })
            .HasDatabaseName("idx_step_logs_job_seq");
    }
}
