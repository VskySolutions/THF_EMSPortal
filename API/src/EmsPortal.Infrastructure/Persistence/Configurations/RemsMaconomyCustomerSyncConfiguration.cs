using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

// One row per request: whether its brand-new client was posted to Maconomy. Tenant-owned and
// soft-deletable like every REMS table; the per-request uniqueness is filtered on [Deleted] = 0.
internal sealed class RemsMaconomyCustomerSyncConfiguration : IEntityTypeConfiguration<REMSMaconomyCustomerSync>
{
    public void Configure(EntityTypeBuilder<REMSMaconomyCustomerSync> builder)
    {
        builder.ToTable("REMSMaconomyCustomerSync");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Status).HasConversion<string>().IsRequired().HasMaxLength(32);
        builder.Property(s => s.CustomerNumber).HasMaxLength(64);
        builder.Property(s => s.FailureReason).HasMaxLength(2000);
        builder.Property(s => s.PayloadJson).HasColumnType("nvarchar(max)");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.TenantId);

        builder.HasOne(s => s.Rems).WithMany().HasForeignKey(s => s.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.ClientPerson).WithMany().HasForeignKey(s => s.ClientPersonId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.TenantId, s.REMSId }).IsUnique().HasFilter("[Deleted] = 0");
        // The settings card's counts and its unsynced list.
        builder.HasIndex(s => new { s.TenantId, s.Status });
    }
}
