using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

// EF Core configurations for the REMS engagement and its detail, marketing and commission children
// (WO-110). The engagement-per-request link and the 0..1 detail tables are modelled as one-to-many at
// the EF level so a filtered unique index (WHERE [Deleted] = 0) enforces the "one active per parent"
// rule. Two DB CHECK constraints guard the percentage columns.

internal sealed class RemsEngagementConfiguration : IEntityTypeConfiguration<REMSEngagement>
{
    public void Configure(EntityTypeBuilder<REMSEngagement> builder)
    {
        builder.ToTable("REMSEngagement", t =>
        {
            t.HasCheckConstraint(
                "CK_REMSEngagement_Realization",
                "[RealizationPercentage] IS NULL OR ([RealizationPercentage] >= 0 AND [RealizationPercentage] <= 100)");
            // No CHECK on BillingProcessDescription: it is prose, and there is nothing about a sentence a
            // constraint could enforce.
        });
        builder.HasKey(e => e.Id);

        // The engagement's four option-set references. Restrict on each: the department in particular is
        // what the conditional half of the setup keys off, so a department with engagements on it is not a
        // value anybody may delete.
        builder.HasOne(e => e.Department).WithMany().HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ServiceLine).WithMany().HasForeignKey(e => e.ServiceLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Industry).WithMany().HasForeignKey(e => e.IndustryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.BillingPeriod).WithMany().HasForeignKey(e => e.BillingPeriodId).OnDelete(DeleteBehavior.Restrict);

        // Always loaded -- see the note on the request's Type/Status. The department in particular decides
        // what the rest of the setup asks, so every read of an engagement wants it.
        builder.Navigation(e => e.Department).AutoInclude();
        builder.Navigation(e => e.ServiceLine).AutoInclude();
        builder.Navigation(e => e.Industry).AutoInclude();
        builder.Navigation(e => e.BillingPeriod).AutoInclude();
        // A description of how the client is billed, not a treatise: long enough for the two or three
        // sentences a schedule takes, short enough that nobody pastes an engagement letter into it.
        builder.Property(e => e.BillingProcessDescription).HasMaxLength(1000);
        builder.Property(e => e.FirstYearFeeEstimate).HasPrecision(18, 2);
        builder.Property(e => e.EngagementFee).HasPrecision(18, 2);
        builder.Property(e => e.RealizationPercentage).HasPrecision(5, 2);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.TenantId);

        builder.HasOne(e => e.Rems).WithMany().HasForeignKey(e => e.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.DepartmentDirectorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.EngagementExecutiveId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.BillingManagerId).OnDelete(DeleteBehavior.Restrict);

        // One active engagement per request.
        builder.HasIndex(e => new { e.TenantId, e.REMSId }).IsUnique().HasFilter("[Deleted] = 0");

        // Approval work queue by status.
        builder.HasIndex(e => new { e.TenantId, e.Status });
    }
}

internal sealed class RemsEngagementAuditDetailConfiguration : IEntityTypeConfiguration<REMSEngagementAuditDetail>
{
    public void Configure(EntityTypeBuilder<REMSEngagementAuditDetail> builder)
    {
        builder.ToTable("REMSEngagementAuditDetail", t =>
        {
            // Assurance's administrative fees. A negative charge is not a fee, and the browser and the API
            // both refuse one — this is the floor under both.
            t.HasCheckConstraint(
                "CK_REMSEngagementAuditDetail_AdminFeesAmount",
                "[AdminFeesAmount] IS NULL OR [AdminFeesAmount] >= 0");
        });
        builder.HasKey(d => d.Id);

        builder.Property(d => d.AdminFeesAmount).HasPrecision(18, 2);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.TenantId);

        builder.HasOne(d => d.Engagement).WithMany().HasForeignKey(d => d.REMSEngagementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.ClientAcceptanceFormMedia).WithMany().HasForeignKey(d => d.ClientAcceptanceFormMediaId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => new { d.TenantId, d.REMSEngagementId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsEngagementGovernmentDetailConfiguration : IEntityTypeConfiguration<REMSEngagementGovernmentDetail>
{
    public void Configure(EntityTypeBuilder<REMSEngagementGovernmentDetail> builder)
    {
        builder.ToTable("REMSEngagementGovernmentDetail", t =>
        {
            // GCS's two money columns. Neither can be negative — a purchase order is worth what it is
            // worth, and an hour is billed at a rate, not a credit.
            t.HasCheckConstraint(
                "CK_REMSEngagementGovernmentDetail_PoAmounts",
                "([PurchaseOrderAmount] IS NULL OR [PurchaseOrderAmount] >= 0) AND ([BillRatePerHour] IS NULL OR [BillRatePerHour] >= 0)");
        });
        builder.HasKey(d => d.Id);

        builder.Property(d => d.OriginalTerm).HasMaxLength(500);
        builder.Property(d => d.RenewalTerms).HasMaxLength(500);
        builder.Property(d => d.ContractNumber).HasMaxLength(64);
        builder.Property(d => d.PurchaseOrderNumber).HasMaxLength(64);
        builder.HasOne(d => d.PersonnelLevel).WithMany().HasForeignKey(d => d.PersonnelLevelId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(d => d.PersonnelLevel).AutoInclude();
        builder.Property(d => d.PurchaseOrderAmount).HasPrecision(18, 2);
        builder.Property(d => d.BillRatePerHour).HasPrecision(18, 2);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.TenantId);

        builder.HasOne(d => d.Engagement).WithMany().HasForeignKey(d => d.REMSEngagementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.PurchaseOrderMedia).WithMany().HasForeignKey(d => d.PurchaseOrderMediaId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => new { d.TenantId, d.REMSEngagementId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsEngagementTaxDetailConfiguration : IEntityTypeConfiguration<REMSEngagementTaxDetail>
{
    public void Configure(EntityTypeBuilder<REMSEngagementTaxDetail> builder)
    {
        builder.ToTable("REMSEngagementTaxDetail");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.CalculatedDueDates).HasColumnType("nvarchar(max)");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.TenantId);

        builder.HasOne(d => d.Engagement).WithMany().HasForeignKey(d => d.REMSEngagementId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => new { d.TenantId, d.REMSEngagementId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsEngagementTaxFormConfiguration : IEntityTypeConfiguration<REMSEngagementTaxForm>
{
    public void Configure(EntityTypeBuilder<REMSEngagementTaxForm> builder)
    {
        builder.ToTable("REMSEngagementTaxForm");
        builder.HasKey(f => f.Id);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(f => f.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => f.TenantId);

        builder.HasOne(f => f.TaxDetail).WithMany(d => d.TaxForms).HasForeignKey(f => f.REMSEngagementTaxDetailId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(f => f.TaxForm).WithMany().HasForeignKey(f => f.TaxFormId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(f => new { f.TenantId, f.REMSEngagementTaxDetailId, f.TaxFormId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsEngagementMarketingMethodConfiguration : IEntityTypeConfiguration<REMSEngagementMarketingMethod>
{
    public void Configure(EntityTypeBuilder<REMSEngagementMarketingMethod> builder)
    {
        builder.ToTable("REMSEngagementMarketingMethod");
        builder.HasKey(m => m.Id);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.TenantId);

        builder.HasOne(m => m.Engagement).WithMany(e => e.MarketingMethods).HasForeignKey(m => m.REMSEngagementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.MarketingMethod).WithMany().HasForeignKey(m => m.MarketingMethodId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.TenantId, m.REMSEngagementId, m.MarketingMethodId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsEngagementCommissionSplitConfiguration : IEntityTypeConfiguration<REMSEngagementCommissionSplit>
{
    public void Configure(EntityTypeBuilder<REMSEngagementCommissionSplit> builder)
    {
        builder.ToTable("REMSEngagementCommissionSplit", t => t.HasCheckConstraint(
            "CK_REMSEngagementCommissionSplit_Pct",
            "[CommissionPercentage] > 0 AND [CommissionPercentage] <= 100"));
        builder.HasKey(s => s.Id);

        builder.Property(s => s.CommissionPercentage).HasPrecision(5, 2).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.TenantId);

        builder.HasOne(s => s.Engagement).WithMany(e => e.CommissionSplits).HasForeignKey(s => s.REMSEngagementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.EmployeeId).OnDelete(DeleteBehavior.Restrict);

        // One split per (engagement, employee).
        builder.HasIndex(s => new { s.TenantId, s.REMSEngagementId, s.EmployeeId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsEngagementApproverConfiguration : IEntityTypeConfiguration<REMSEngagementApprover>
{
    public void Configure(EntityTypeBuilder<REMSEngagementApprover> builder)
    {
        builder.ToTable("REMSEngagementApprover");
        builder.HasKey(a => a.Id);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.TenantId);

        builder.HasOne(a => a.Engagement).WithMany(e => e.Approvers).HasForeignKey(a => a.REMSEngagementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);

        // One row per (engagement, user) — the same person is picked at most once.
        builder.HasIndex(a => new { a.TenantId, a.REMSEngagementId, a.UserId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}
