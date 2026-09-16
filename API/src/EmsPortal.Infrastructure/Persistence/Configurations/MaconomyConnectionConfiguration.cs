using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

internal sealed class MaconomyConnectionConfiguration : IEntityTypeConfiguration<MaconomyConnection>
{
    public void Configure(EntityTypeBuilder<MaconomyConnection> builder)
    {
        // A search that returns no rows is not a search.
        builder.ToTable("MaconomyConnections", t => t.HasCheckConstraint(
            "CK_MaconomyConnections_DefaultLimit",
            "[DefaultLimit] >= 1"));

        builder.HasKey(c => c.Id);

        builder.Property(c => c.BaseUrl).IsRequired().HasMaxLength(500);
        builder.Property(c => c.InstanceCode).IsRequired().HasMaxLength(100);
        builder.Property(c => c.UserName).IsRequired().HasMaxLength(200);
        // Encrypted blobs; never stored in plaintext, never returned in API responses.
        builder.Property(c => c.EncryptedPassword).IsRequired();
        builder.Property(c => c.EncryptedReconnectToken);
        builder.Property(c => c.LastLoginError).HasMaxLength(500);
        builder.Property(c => c.ContainerId).HasMaxLength(100);
        builder.Property(c => c.DefaultLimit).IsRequired();
        builder.Property(c => c.IsEnabled).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);

        // One live connection per tenant.
        builder.HasIndex(c => c.TenantId).IsUnique().HasFilter("[Deleted] = 0");
    }
}
