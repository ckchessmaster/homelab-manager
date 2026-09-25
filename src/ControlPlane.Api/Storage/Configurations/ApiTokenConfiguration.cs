using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ControlPlane.Api.Storage.Configurations;

public class ApiTokenConfiguration : IEntityTypeConfiguration<ApiToken>
{
    public void Configure(EntityTypeBuilder<ApiToken> builder)
    {
        builder.ToTable("api_tokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(t => t.TokenHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(t => t.TokenHash);

        builder.Property(t => t.TokenPrefix)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(t => t.Role)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .IsRequired();
    }
}
