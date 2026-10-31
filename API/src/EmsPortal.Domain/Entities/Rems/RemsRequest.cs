using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// A REMS request as one row: who raised it, for which client, where it is, the invite that went out,
/// the engagement setup and the government contract fields. <see cref="StatusId"/> is the only workflow
/// status; everything else about the request hangs off it as child rows.
/// </summary>
public class RemsRequest : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>"REMS-n" from the request sequence; never reused.</summary>
    public string RemsNumber { get; set; } = string.Empty;
    /// <summary>REMS.Type option.</summary>
    public Guid TypeId { get; set; }
    /// <summary>REMS.Status option: the one workflow status.</summary>
    public Guid StatusId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? CSEId { get; set; }
    public Guid? AdminAssignedToId { get; set; }
    public Guid? OnBehalfOfUserId { get; set; }

    // ---- Invite: the intake link sent to the client ----
    public string? InviteCode { get; set; }
    public string? InviteRecipientEmail { get; set; }
    public Guid? InviteSentByUserId { get; set; }
    public DateTime? InviteSentOnUtc { get; set; }
    public DateTime? FormLastSavedOnUtc { get; set; }
    public DateTime? FormSubmittedOnUtc { get; set; }

    // ---- Engagement setup ----
    public Guid? DepartmentId { get; set; }
    public Guid? ServiceLineId { get; set; }
    public Guid? JobTemplateId { get; set; }
    public Guid? BillingPeriodId { get; set; }
    public Guid? DepartmentDirectorId { get; set; }
    public Guid? EngagementExecutiveId { get; set; }
    public Guid? BillingManagerId { get; set; }
    public decimal? FirstYearFeeEstimate { get; set; }
    public decimal? EngagementFee { get; set; }
    public decimal? RealizationPercentage { get; set; }
    public string? BillingProcessDescription { get; set; }

    // ---- Government only: the firm's side of the contract the client declared ----
    public Guid? ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public decimal? PurchaseOrderAmount { get; set; }
    public bool? IsFloridaOnePercentFeeApplicable { get; set; }

    // ---- Maconomy job: the engagement posted to Maconomy as a job, by hand, from a list's Sync job now ----
    public string? MaconomyJobNumber { get; set; }
    public RemsMaconomyJobSyncStatus MaconomyJobSyncStatus { get; set; } = RemsMaconomyJobSyncStatus.NotSynced;
    /// <summary>Who pressed the sync that created the job, and when.</summary>
    public Guid? MaconomyJobSyncedById { get; set; }
    public DateTime? MaconomyJobSyncedOnUtc { get; set; }
    public string? MaconomyJobSyncError { get; set; }

    // ---- Navigations ----
    public OptionSetItem? Type { get; set; }
    public OptionSetItem? Status { get; set; }
    public Client? Client { get; set; }
    public ClientContract? Contract { get; set; }
    public User? CSE { get; set; }
    public User? AdminAssignedTo { get; set; }
    public User? OnBehalfOfUser { get; set; }
    public User? InviteSentByUser { get; set; }
    public OptionSetItem? Department { get; set; }
    public OptionSetItem? ServiceLine { get; set; }
    public OptionSetItem? JobTemplate { get; set; }
    public OptionSetItem? BillingPeriod { get; set; }
    public User? DepartmentDirector { get; set; }
    public User? EngagementExecutive { get; set; }
    public User? BillingManager { get; set; }
    public ICollection<RemsFile> Files { get; set; } = new List<RemsFile>();
    public ICollection<RemsEmailEvent> EmailEvents { get; set; } = new List<RemsEmailEvent>();
    public ICollection<RemsStatusHistory> StatusHistory { get; set; } = new List<RemsStatusHistory>();
    public ICollection<RemsApprover> Approvers { get; set; } = new List<RemsApprover>();
    public ICollection<RemsCommissionSplit> CommissionSplits { get; set; } = new List<RemsCommissionSplit>();
    public ICollection<RemsApprovalTask> ApprovalTasks { get; set; } = new List<RemsApprovalTask>();
    public ICollection<RemsPersonnelRate> PersonnelRates { get; set; } = new List<RemsPersonnelRate>();
    public ICollection<RemsMarketingMethod> MarketingMethods { get; set; } = new List<RemsMarketingMethod>();
    public ICollection<RemsTaxForm> TaxForms { get; set; } = new List<RemsTaxForm>();
    public ICollection<RemsTaxDueDate> TaxDueDates { get; set; } = new List<RemsTaxDueDate>();
}
