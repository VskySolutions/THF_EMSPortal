using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

// EF Core configurations for the REMS approval round/task/checklist chain (WO-110). A round is
// immutable history (a new round per resubmission); its (engagement, round-number) unique index
// carries no [Deleted] = 0 term so a superseded round never frees the number.

internal sealed class RemsApprovalRoundConfiguration : IEntityTypeConfiguration<REMSApprovalRound>
{
    public void Configure(EntityTypeBuilder<REMSApprovalRound> builder)
    {
        builder.ToTable("REMSApprovalRound");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.RejectionReason).HasMaxLength(500);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.TenantId);

        builder.HasOne(r => r.Engagement).WithMany(e => e.ApprovalRounds).HasForeignKey(r => r.REMSEngagementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.SentByUserId).OnDelete(DeleteBehavior.Restrict);

        // Immutable history: round number unique per engagement, no [Deleted] filter.
        builder.HasIndex(r => new { r.TenantId, r.REMSEngagementId, r.RoundNumber }).IsUnique();
    }
}

internal sealed class RemsApprovalTaskConfiguration : IEntityTypeConfiguration<REMSApprovalTask>
{
    public void Configure(EntityTypeBuilder<REMSApprovalTask> builder)
    {
        builder.ToTable("REMSApprovalTask");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.ApproverRole).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.RejectionReason).HasMaxLength(500);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.TenantId);

        builder.HasOne(t => t.Round).WithMany(r => r.Tasks).HasForeignKey(t => t.REMSApprovalRoundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.ApproverId).OnDelete(DeleteBehavior.Restrict);

        // One task per (round, approver, role).
        builder.HasIndex(t => new { t.TenantId, t.REMSApprovalRoundId, t.ApproverId, t.ApproverRole }).IsUnique().HasFilter("[Deleted] = 0");

        // An approver's pending work queue.
        builder.HasIndex(t => new { t.TenantId, t.ApproverId, t.Status });
    }
}

internal sealed class RemsApprovalChecklistItemConfiguration : IEntityTypeConfiguration<REMSApprovalChecklistItem>
{
    public void Configure(EntityTypeBuilder<REMSApprovalChecklistItem> builder)
    {
        builder.ToTable("REMSApprovalChecklistItem");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Label).IsRequired().HasMaxLength(200);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.TenantId);

        builder.HasOne(i => i.Task).WithMany(t => t.ChecklistItems).HasForeignKey(i => i.REMSApprovalTaskId).OnDelete(DeleteBehavior.Restrict);

        // One item per (task, display order).
        builder.HasIndex(i => new { i.TenantId, i.REMSApprovalTaskId, i.DisplayOrder }).IsUnique().HasFilter("[Deleted] = 0");
    }
}
