using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

// The request as one row (REMS) and its sub-tables (REMS_*). Every FK is Restrict, every "one per parent"
// rule is a filtered unique index. The old REMS* tables live in the legacy schema until the rewrite is done.

internal sealed class RemsRequestConfiguration : IEntityTypeConfiguration<RemsRequest>
{
    public void Configure(EntityTypeBuilder<RemsRequest> builder)
    {
        builder.ToTable("REMS", t =>
        {
            t.HasCheckConstraint(
                "CK_REMS_Realization",
                "[RealizationPercentage] IS NULL OR ([RealizationPercentage] >= 0 AND [RealizationPercentage] <= 100)");
            t.HasCheckConstraint(
                "CK_REMS_PurchaseOrderAmount",
                "[PurchaseOrderAmount] IS NULL OR [PurchaseOrderAmount] >= 0");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RemsNumber).IsRequired().HasMaxLength(64);
        builder.Property(r => r.InviteCode).HasMaxLength(64);
        builder.Property(r => r.InviteRecipientEmail).HasMaxLength(256);
        builder.Property(r => r.FirstYearFeeEstimate).HasPrecision(18, 2);
        builder.Property(r => r.EngagementFee).HasPrecision(18, 2);
        builder.Property(r => r.RealizationPercentage).HasPrecision(5, 2);
        builder.Property(r => r.BillingProcessDescription).HasMaxLength(1000);
        builder.Property(r => r.ContractNumber).HasMaxLength(64);
        builder.Property(r => r.PurchaseOrderNumber).HasMaxLength(64);
        builder.Property(r => r.PurchaseOrderAmount).HasPrecision(18, 2);
        builder.Property(r => r.MaconomyJobNumber).HasMaxLength(64);
        // Text, like the client's customer status, with the default spelt out so the rows that predate the column read as NotSynced.
        builder.Property(r => r.MaconomyJobSyncStatus).HasConversion<string>().HasMaxLength(20).IsRequired().HasDefaultValue(RemsMaconomyJobSyncStatus.NotSynced);
        builder.Property(r => r.MaconomyJobSyncError).HasMaxLength(1000);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.TenantId);
        builder.HasOne(r => r.Type).WithMany().HasForeignKey(r => r.TypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Status).WithMany().HasForeignKey(r => r.StatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Client).WithMany(c => c.Requests).HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Contract).WithMany().HasForeignKey(r => r.ContractId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.CSE).WithMany().HasForeignKey(r => r.CSEId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.AdminAssignedTo).WithMany().HasForeignKey(r => r.AdminAssignedToId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.OnBehalfOfUser).WithMany().HasForeignKey(r => r.OnBehalfOfUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.InviteSentByUser).WithMany().HasForeignKey(r => r.InviteSentByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Department).WithMany().HasForeignKey(r => r.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.ServiceLine).WithMany().HasForeignKey(r => r.ServiceLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.JobTemplate).WithMany().HasForeignKey(r => r.JobTemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.BillingPeriod).WithMany().HasForeignKey(r => r.BillingPeriodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.DepartmentDirector).WithMany().HasForeignKey(r => r.DepartmentDirectorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.EngagementExecutive).WithMany().HasForeignKey(r => r.EngagementExecutiveId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.BillingManager).WithMany().HasForeignKey(r => r.BillingManagerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.MaconomyJobSyncedById).OnDelete(DeleteBehavior.Restrict);
        // Type, status and client are on every row the API hands out; AutoInclude makes forgetting impossible.
        builder.Navigation(r => r.Type).AutoInclude();
        builder.Navigation(r => r.Status).AutoInclude();
        builder.Navigation(r => r.Client).AutoInclude();

        builder.HasIndex(r => new { r.TenantId, r.RemsNumber }).IsUnique().HasFilter("[Deleted] = 0");
        builder.HasIndex(r => new { r.TenantId, r.InviteCode }).IsUnique().HasFilter("[Deleted] = 0 AND [InviteCode] IS NOT NULL");
        builder.HasIndex(r => new { r.TenantId, r.ClientId });
        builder.HasIndex(r => new { r.TenantId, r.MaconomyJobNumber }).HasFilter("[MaconomyJobNumber] IS NOT NULL");
        builder.HasIndex(r => new { r.TenantId, r.StatusId, r.AdminAssignedToId, r.CreatedOnUtc });
        builder.HasIndex(r => new { r.TenantId, r.CreatedById, r.StatusId });
        builder.HasIndex(r => new { r.TenantId, r.OnBehalfOfUserId, r.StatusId });
        builder.HasIndex(r => new { r.TenantId, r.DepartmentId });
    }
}

internal sealed class RemsFileConfiguration : IEntityTypeConfiguration<RemsFile>
{
    public void Configure(EntityTypeBuilder<RemsFile> builder)
    {
        builder.ToTable("REMS_File");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(f => f.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => f.TenantId);
        builder.HasOne(f => f.Request).WithMany(r => r.Files).HasForeignKey(f => f.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(f => f.Media).WithMany().HasForeignKey(f => f.MediaId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(f => f.Media).AutoInclude();
        builder.HasIndex(f => new { f.TenantId, f.REMSId, f.MediaId }).IsUnique().HasFilter("[Deleted] = 0");
        builder.HasIndex(f => new { f.TenantId, f.REMSId, f.Kind });
    }
}

internal sealed class RemsEmailEventConfiguration : IEntityTypeConfiguration<RemsEmailEvent>
{
    public void Configure(EntityTypeBuilder<RemsEmailEvent> builder)
    {
        builder.ToTable("REMS_EmailEvent");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.RecipientEmail).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Subject).HasMaxLength(512);
        builder.Property(e => e.Body).HasColumnType("nvarchar(max)");
        builder.Property(e => e.ProviderMessageId).HasMaxLength(256);
        builder.Property(e => e.FailureReason).HasMaxLength(1000);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.TenantId);
        builder.HasOne(e => e.Request).WithMany(r => r.EmailEvents).HasForeignKey(e => e.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.EmailTemplate).WithMany().HasForeignKey(e => e.EmailTemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.TenantId, e.ProviderMessageId, e.EventType })
            .IsUnique()
            .HasFilter("[ProviderMessageId] IS NOT NULL");
        builder.HasIndex(e => new { e.TenantId, e.REMSId, e.OccurredOnUtc });
    }
}

internal sealed class RemsStatusHistoryConfiguration : IEntityTypeConfiguration<RemsStatusHistory>
{
    public void Configure(EntityTypeBuilder<RemsStatusHistory> builder)
    {
        builder.ToTable("REMS_StatusHistory");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Reason).HasMaxLength(1000);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(h => h.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(h => h.TenantId);
        builder.HasOne(h => h.Request).WithMany(r => r.StatusHistory).HasForeignKey(h => h.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(h => h.FromStatus).WithMany().HasForeignKey(h => h.FromStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(h => h.ToStatus).WithMany().HasForeignKey(h => h.ToStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(h => h.ChangedByUser).WithMany().HasForeignKey(h => h.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(h => h.ReturnedToUser).WithMany().HasForeignKey(h => h.ReturnedToUserId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(h => h.ToStatus).AutoInclude();
        builder.HasIndex(h => new { h.TenantId, h.REMSId, h.ChangedOnUtc });
    }
}

internal sealed class RemsApproverConfiguration : IEntityTypeConfiguration<RemsApprover>
{
    public void Configure(EntityTypeBuilder<RemsApprover> builder)
    {
        builder.ToTable("REMS_Approver");
        builder.HasKey(a => a.Id);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.TenantId);
        builder.HasOne(a => a.Request).WithMany(r => r.Approvers).HasForeignKey(a => a.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.TenantId, a.REMSId, a.UserId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsCommissionSplitConfiguration : IEntityTypeConfiguration<RemsCommissionSplit>
{
    public void Configure(EntityTypeBuilder<RemsCommissionSplit> builder)
    {
        builder.ToTable("REMS_CommissionSplit", t => t.HasCheckConstraint(
            "CK_REMS_CommissionSplit_Pct",
            "[CommissionPercentage] > 0 AND [CommissionPercentage] <= 100"));
        builder.HasKey(s => s.Id);
        builder.Property(s => s.CommissionPercentage).HasPrecision(5, 2).IsRequired();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.TenantId);
        builder.HasOne(s => s.Request).WithMany(r => r.CommissionSplits).HasForeignKey(s => s.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Employee).WithMany().HasForeignKey(s => s.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.TenantId, s.REMSId, s.EmployeeId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsApprovalTaskConfiguration : IEntityTypeConfiguration<RemsApprovalTask>
{
    public void Configure(EntityTypeBuilder<RemsApprovalTask> builder)
    {
        builder.ToTable("REMS_ApprovalTask", t => t.HasCheckConstraint(
            "CK_REMS_ApprovalTask_Round",
            "[RoundNumber] >= 1"));
        builder.HasKey(t => t.Id);
        builder.Property(t => t.ApproverRole).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.RejectionReason).HasMaxLength(500);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.TenantId);
        builder.HasOne(t => t.Request).WithMany(r => r.ApprovalTasks).HasForeignKey(t => t.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Approver).WithMany().HasForeignKey(t => t.ApproverId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => new { t.TenantId, t.REMSId, t.RoundNumber, t.ApproverId, t.ApproverRole }).IsUnique().HasFilter("[Deleted] = 0");
        builder.HasIndex(t => new { t.TenantId, t.REMSId, t.RoundNumber });
        builder.HasIndex(t => new { t.TenantId, t.ApproverId, t.Status });
    }
}

internal sealed class RemsApprovalChecklistItemConfiguration : IEntityTypeConfiguration<RemsApprovalChecklistItem>
{
    public void Configure(EntityTypeBuilder<RemsApprovalChecklistItem> builder)
    {
        builder.ToTable("REMS_ApprovalChecklistItem");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Label).IsRequired().HasMaxLength(200);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.TenantId);
        builder.HasOne(i => i.Task).WithMany(t => t.ChecklistItems).HasForeignKey(i => i.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => new { i.TenantId, i.TaskId, i.DisplayOrder }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsPersonnelRateConfiguration : IEntityTypeConfiguration<RemsPersonnelRate>
{
    public void Configure(EntityTypeBuilder<RemsPersonnelRate> builder)
    {
        builder.ToTable("REMS_PersonnelRate", t => t.HasCheckConstraint(
            "CK_REMS_PersonnelRate_BillRatePerHour",
            "[BillRatePerHour] >= 0"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.BillRatePerHour).HasPrecision(18, 2).IsRequired();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.TenantId);
        builder.HasOne(r => r.Request).WithMany(x => x.PersonnelRates).HasForeignKey(r => r.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.PersonnelLevel).WithMany().HasForeignKey(r => r.PersonnelLevelId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(r => r.PersonnelLevel).AutoInclude();
        builder.HasIndex(r => new { r.TenantId, r.REMSId, r.PersonnelLevelId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsMarketingMethodConfiguration : IEntityTypeConfiguration<RemsMarketingMethod>
{
    public void Configure(EntityTypeBuilder<RemsMarketingMethod> builder)
    {
        builder.ToTable("REMS_MarketingMethod");
        builder.HasKey(m => m.Id);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.TenantId);
        builder.HasOne(m => m.Request).WithMany(r => r.MarketingMethods).HasForeignKey(m => m.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.MarketingMethod).WithMany().HasForeignKey(m => m.MarketingMethodId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(m => m.MarketingMethod).AutoInclude();
        builder.HasIndex(m => new { m.TenantId, m.REMSId, m.MarketingMethodId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsTaxFormConfiguration : IEntityTypeConfiguration<RemsTaxForm>
{
    public void Configure(EntityTypeBuilder<RemsTaxForm> builder)
    {
        builder.ToTable("REMS_TaxForm");
        builder.HasKey(f => f.Id);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(f => f.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => f.TenantId);
        builder.HasOne(f => f.Request).WithMany(r => r.TaxForms).HasForeignKey(f => f.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(f => f.TaxForm).WithMany().HasForeignKey(f => f.TaxFormId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(f => f.TaxForm).AutoInclude();
        builder.HasIndex(f => new { f.TenantId, f.REMSId, f.TaxFormId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsTaxDueDateConfiguration : IEntityTypeConfiguration<RemsTaxDueDate>
{
    public void Configure(EntityTypeBuilder<RemsTaxDueDate> builder)
    {
        builder.ToTable("REMS_TaxDueDate");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.TenantId);
        builder.HasOne(d => d.Request).WithMany(r => r.TaxDueDates).HasForeignKey(d => d.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.OverriddenByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => new { d.TenantId, d.REMSId, d.Kind }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsChecklistTemplateConfiguration : IEntityTypeConfiguration<RemsChecklistTemplate>
{
    public void Configure(EntityTypeBuilder<RemsChecklistTemplate> builder)
    {
        builder.ToTable("REMS_ChecklistTemplate");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.ApproverRole).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Label).IsRequired().HasMaxLength(200);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.TenantId);
        builder.HasOne(t => t.Settings).WithMany(s => s.ChecklistTemplates).HasForeignKey(t => t.SettingsId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => new { t.TenantId, t.SettingsId, t.ApproverRole, t.DisplayOrder }).IsUnique().HasFilter("[Deleted] = 0");
    }
}
