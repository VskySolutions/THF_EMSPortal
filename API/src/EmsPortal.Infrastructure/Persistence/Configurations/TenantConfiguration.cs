using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Identifier)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(t => t.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(t => t.CreatedDate)
            .IsRequired();

        // Room for the twenty addresses the API accepts, semicolon-separated.
        builder.Property(t => t.DeveloperEmails)
            .HasMaxLength(2000);

        // Tenant identifiers must be unique, URL-safe slugs.
        builder.HasIndex(t => t.Identifier).IsUnique().HasFilter("[Deleted] = 0");
    }
}
