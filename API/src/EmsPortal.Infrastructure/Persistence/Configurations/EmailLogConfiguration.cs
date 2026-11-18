using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

internal sealed class EmailLogConfiguration : IEntityTypeConfiguration<EmailLog>
{
    public void Configure(EntityTypeBuilder<EmailLog> builder)
    {
        builder.ToTable("EmailLogs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.TemplateKey).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.RecipientEmail).HasMaxLength(320).IsRequired();
        builder.Property(l => l.Subject).HasMaxLength(500).IsRequired();
        builder.Property(l => l.FailureReason).HasMaxLength(1000);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);

        // The list is one tenant's log, newest first.
        builder.HasIndex(l => new { l.TenantId, l.CreatedOnUtc });
    }
}
