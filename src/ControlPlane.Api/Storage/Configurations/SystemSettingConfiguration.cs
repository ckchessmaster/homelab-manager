using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ControlPlane.Api.Storage.Configurations;

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("system_settings");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(128);
        builder.Property(s => s.ValueJson).IsRequired();
    }
}
