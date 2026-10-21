using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

internal sealed class TenantEmailSettingsConfiguration : IEntityTypeConfiguration<TenantEmailSettings>
{
    public void Configure(EntityTypeBuilder<TenantEmailSettings> builder)
    {
        builder.ToTable("TenantEmailSettings");

        builder.HasKey(s => s.Id);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);

        // One live settings row per tenant.
        builder.HasIndex(s => s.TenantId).IsUnique().HasFilter("[Deleted] = 0");
    }
}
