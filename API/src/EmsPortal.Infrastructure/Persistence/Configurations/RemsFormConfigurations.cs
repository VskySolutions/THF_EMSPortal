using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

// EF Core configurations for the REMS customer-facing form and its drafts, submissions and email
// events (WO-110). REMSFormSubmission and REMSFormEmailEvent are append-only: their unique indexes
// carry no [Deleted] = 0 term and the DbContext gives them a tenant-only query filter.

internal sealed class RemsFormConfiguration : IEntityTypeConfiguration<REMSForm>
{
    public void Configure(EntityTypeBuilder<REMSForm> builder)
    {
        builder.ToTable("REMSForm");
        builder.HasKey(f => f.Id);

        builder.HasOne(f => f.EntityType).WithMany().HasForeignKey(f => f.EntityTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(f => f.EntityType).AutoInclude();
        builder.Property(f => f.InviteCode).IsRequired().HasMaxLength(64);
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(f => f.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => f.TenantId);

        builder.HasOne(f => f.Rems).WithMany(r => r.Forms).HasForeignKey(f => f.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(f => f.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        // One active form per request; invite code unique per tenant (also backs the public lookup).
        builder.HasIndex(f => new { f.TenantId, f.REMSId }).IsUnique().HasFilter("[Deleted] = 0");
        builder.HasIndex(f => new { f.TenantId, f.InviteCode }).IsUnique().HasFilter("[Deleted] = 0");

        // Request inbox by status.
        builder.HasIndex(f => new { f.TenantId, f.REMSId, f.Status });
    }
}

internal sealed class RemsFormDraftConfiguration : IEntityTypeConfiguration<REMSFormDraft>
{
    public void Configure(EntityTypeBuilder<REMSFormDraft> builder)
    {
        builder.ToTable("REMSFormDraft");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.DraftPayload).HasColumnType("nvarchar(max)").IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.TenantId);

        // Draft cascades with its form.
        builder.HasOne(d => d.Form).WithMany(f => f.Drafts).HasForeignKey(d => d.REMSFormId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => new { d.TenantId, d.REMSFormId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsFormSubmissionConfiguration : IEntityTypeConfiguration<REMSFormSubmission>
{
    public void Configure(EntityTypeBuilder<REMSFormSubmission> builder)
    {
        builder.ToTable("REMSFormSubmission");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.SubmittedPayload).HasColumnType("nvarchar(max)").IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.TenantId);

        builder.HasOne(s => s.Form).WithMany(f => f.Submissions).HasForeignKey(s => s.REMSFormId).OnDelete(DeleteBehavior.Restrict);

        // Append-only: one submission per form, no [Deleted] filter.
        builder.HasIndex(s => new { s.TenantId, s.REMSFormId }).IsUnique();
    }
}

internal sealed class RemsFormEmailEventConfiguration : IEntityTypeConfiguration<REMSFormEmailEvent>
{
    public void Configure(EntityTypeBuilder<REMSFormEmailEvent> builder)
    {
        builder.ToTable("REMSFormEmailEvent");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProviderMessageId).HasMaxLength(256);
        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.RecipientEmail).IsRequired().HasMaxLength(256);
        builder.Property(e => e.ProviderPayload).HasColumnType("nvarchar(max)");

        // What the client was actually sent, on the Sent and Reminder rows. The subject is a header and is
        // bounded; the body is rich text the sender may have rewritten wholesale, so it gets the same
        // unrationed column the send dialog's editor implies.
        builder.Property(e => e.Subject).HasMaxLength(512);
        builder.Property(e => e.Body).HasColumnType("nvarchar(max)");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.TenantId);

        builder.HasOne(e => e.Form).WithMany(f => f.EmailEvents).HasForeignKey(e => e.REMSFormId).OnDelete(DeleteBehavior.Restrict);

        // Append-only: de-duplicate provider events by (message id, event type) when a message id is present.
        builder.HasIndex(e => new { e.TenantId, e.ProviderMessageId, e.EventType })
            .IsUnique()
            .HasFilter("[ProviderMessageId] IS NOT NULL");
    }
}
