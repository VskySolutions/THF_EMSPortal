using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

internal sealed class OptionSetConfiguration : IEntityTypeConfiguration<OptionSet>
{
    public void Configure(EntityTypeBuilder<OptionSet> builder)
    {
        builder.ToTable("OptionSets");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.EntityType).HasConversion<int>().IsRequired();

        builder.Property(s => s.Key)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(s => s.ItemSortMode)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.IsSystem).IsRequired();
        builder.Property(s => s.IsClosed).IsRequired();
        builder.Property(s => s.IsActive).IsRequired();

        // One list per (scope, entity, key); null tenant = the platform-standard list.
        builder.HasIndex(s => new { s.TenantId, s.EntityType, s.Key })
            .IsUnique()
            .HasFilter("[Deleted] = 0");

        // A dependency set references its parent set; restrict so a parent can't be deleted out from
        // under its children.
        builder.HasOne<OptionSet>()
            .WithMany()
            .HasForeignKey(s => s.ParentSetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Items)
            .WithOne(i => i.OptionSet!)
            .HasForeignKey(i => i.OptionSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OptionSetItemConfiguration : IEntityTypeConfiguration<OptionSetItem>
{
    public void Configure(EntityTypeBuilder<OptionSetItem> builder)
    {
        builder.ToTable("OptionSetItems");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Value)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(i => i.Label)
            .IsRequired()
            .HasMaxLength(200);

        // Long enough for a sentence or two of explanation — it is a tooltip, not documentation.
        builder.Property(i => i.Description).HasMaxLength(512);
        builder.Property(i => i.MaconomyNumber).HasMaxLength(64);

        builder.Property(i => i.SortOrder).IsRequired();
        builder.Property(i => i.IsDefault).IsRequired();
        builder.Property(i => i.IsActive).IsRequired();

        builder.Property(i => i.BackgroundColor).HasMaxLength(32);
        builder.Property(i => i.TextColor).HasMaxLength(32);

        // A Material icon name, e.g. "o_workspace_premium".
        builder.Property(i => i.Icon).HasMaxLength(64);
        builder.Property(i => i.IsSystem).IsRequired();

        // Value is unique within a list for a given scope (null tenant = standard value).
        builder.HasIndex(i => new { i.OptionSetId, i.TenantId, i.Value })
            .IsUnique()
            .HasFilter("[Deleted] = 0");

        builder.HasIndex(i => i.ParentItemId);

        // Self-reference to a parent-set item for dependency (cascading) lists.
        builder.HasOne<OptionSetItem>()
            .WithMany()
            .HasForeignKey(i => i.ParentItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
