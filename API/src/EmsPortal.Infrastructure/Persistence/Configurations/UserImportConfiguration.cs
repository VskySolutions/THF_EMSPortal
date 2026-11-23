using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

internal sealed class UserImportConfiguration : IEntityTypeConfiguration<UserImport>
{
    public void Configure(EntityTypeBuilder<UserImport> builder)
    {
        builder.ToTable("UserImports");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.FileName).HasMaxLength(260).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(i => i.Rows).WithOne().HasForeignKey(r => r.UserImportId).OnDelete(DeleteBehavior.Cascade);

        // The history is one tenant's imports, newest first.
        builder.HasIndex(i => new { i.TenantId, i.CreatedOnUtc });
    }
}

internal sealed class UserImportRowConfiguration : IEntityTypeConfiguration<UserImportRow>
{
    public void Configure(EntityTypeBuilder<UserImportRow> builder)
    {
        builder.ToTable("UserImportRows");

        builder.HasKey(r => r.Id);

        // The typed values are kept as they came, so they are wider than the columns they were bound for.
        builder.Property(r => r.MaconomyNumber).HasMaxLength(100);
        builder.Property(r => r.FirstName).HasMaxLength(200);
        builder.Property(r => r.LastName).HasMaxLength(200);
        builder.Property(r => r.Email).HasMaxLength(320);
        builder.Property(r => r.Phone).HasMaxLength(100);
        builder.Property(r => r.Department).HasMaxLength(100);
        builder.Property(r => r.Roles).HasMaxLength(1000);
        builder.Property(r => r.AccountStatus).HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.Error).HasMaxLength(1000);

        builder.HasOne<User>().WithMany().HasForeignKey(r => r.CreatedUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.UserImportId, r.RowNumber });
    }
}
