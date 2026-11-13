using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(512);
        builder.Property(u => u.Salt).IsRequired().HasMaxLength(512);
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.MustChangePassword).IsRequired();
        builder.Property(u => u.TokenVersion).IsRequired();
        builder.Property(u => u.MaconomyNumber).HasMaxLength(64);
        builder.Property(u => u.CreatedDate).IsRequired();

        builder.HasIndex(u => u.Email).IsUnique().HasFilter("[Deleted] = 0");

        // Every user is backed by one Person master record (WO-61). The FK lives on User.
        builder.HasOne(u => u.Person)
            .WithOne()
            .HasForeignKey<User>(u => u.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(u => u.TenantRoles)
            .WithOne(r => r.User!)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserTenantRoleConfiguration : IEntityTypeConfiguration<UserTenantRole>
{
    public void Configure(EntityTypeBuilder<UserTenantRole> builder)
    {
        builder.ToTable("UserTenantRoles");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        // Required link to the RBAC role — every assignment row references exactly one concrete role.
        builder.HasOne(r => r.RoleEntity)
            .WithMany()
            .HasForeignKey(r => r.RoleId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // One row per (user, tenant, role): a user may hold multiple roles in the same tenant.
        builder.HasIndex(r => new { r.UserId, r.TenantId, r.RoleId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}
