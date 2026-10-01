using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

internal sealed class TenantBrandingConfiguration : IEntityTypeConfiguration<TenantBranding>
{
    public void Configure(EntityTypeBuilder<TenantBranding> builder)
    {
        builder.ToTable("TenantBrandings");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.ThemeJson).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(b => b.TenantId).OnDelete(DeleteBehavior.Restrict);

        // The images are media rows; removing one must not take the branding with it.
        builder.HasOne<Media>().WithMany().HasForeignKey(b => b.LogoMediaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Media>().WithMany().HasForeignKey(b => b.LogoDarkMediaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Media>().WithMany().HasForeignKey(b => b.LogoMarkMediaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Media>().WithMany().HasForeignKey(b => b.FaviconMediaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Media>().WithMany().HasForeignKey(b => b.LoginBackgroundMediaId).OnDelete(DeleteBehavior.Restrict);

        // One live branding per tenant.
        builder.HasIndex(b => b.TenantId).IsUnique().HasFilter("[Deleted] = 0");
    }
}
