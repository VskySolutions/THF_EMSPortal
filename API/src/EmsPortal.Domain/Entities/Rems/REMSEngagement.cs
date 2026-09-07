using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// The engagement being set up by a <see cref="REMS"/> request — exactly one per request. Holds the
/// servicing team, fee estimate, realization and billing schedule, and routes through approval.
/// <see cref="Department"/>, <see cref="ServiceLine"/>, <see cref="Industry"/> and
/// <see cref="BillingPeriod"/> store option-set codes.
/// <para>
/// It hangs off the REQUEST, not off a <see cref="REMSEntity"/>. The initiator fills the engagement
/// setup before the client is ever contacted, so there is no entity to attach it to when it is created —
/// entities only exist once the client's intake comes back. A client who wants a second engagement gets
/// a second request, and a client group with several businesses produces one request per business (see
/// <see cref="REMSAdditionalEntity"/>), which is what keeps this one-to-one.
/// </para>
/// </summary>
public class REMSEngagement : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant (tenant-scoped).</summary>
    public Guid TenantId { get; set; }

    /// <summary>The request this engagement belongs to (one-to-one).</summary>
    public Guid REMSId { get; set; }

    /// <summary>
    /// Owning department: a foreign key to the <c>REMS.Department</c> option-set item.
    ///
    /// <para>
    /// The conditional half of the setup keys off the CODE this id resolves to -- a signed CAF on audit and
    /// assurance, a fiscal year end on tax, a purchase order on gcs, the billing pair on cas
    /// (RemsEngagementCodes). Read it through the <see cref="Department"/> navigation, whose
    /// <c>Value</c> is that code; the seeded values are locked against deletion and re-coding, so it is
    /// stable to branch on.
    /// </para>
    /// </summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>
    /// The service actually being sold — the setup form's SERVICE LINE (option-set <c>REMS.ServiceLine</c>
    /// code). Classification only — nothing branches on it.
    /// </summary>
    public Guid? ServiceLineId { get; set; }

    /// <summary>
    /// The client's trade — what the setup form calls the INDUSTRY (option-set <c>REMS.Industry</c>
    /// code, the key likewise kept). The ENTITY TYPE above it lives on the form record because it decides
    /// what the client is asked and is frozen once the intake goes out; this is internal classification, so
    /// it belongs to the engagement and stays editable for as long as the setup does.
    /// </summary>
    public Guid? IndustryId { get; set; }

    /// <summary>Department director (User).</summary>
    public Guid? DepartmentDirectorId { get; set; }

    /// <summary>Engagement executive (User).</summary>
    public Guid? EngagementExecutiveId { get; set; }

    /// <summary>Billing manager (User).</summary>
    public Guid? BillingManagerId { get; set; }

    /// <summary>Estimated first-year fee. Asked of every department except Assurance and GCS.</summary>
    public decimal? FirstYearFeeEstimate { get; set; }

    /// <summary>
    /// The Assurance department's fee. A column of its own rather than a relabelling of
    /// <see cref="FirstYearFeeEstimate"/>: an assurance engagement is priced for the engagement, not for
    /// its first year, so the two are different questions and reporting has to be able to tell them apart.
    /// Only Assurance is asked it; every other department leaves it null.
    /// </summary>
    public decimal? EngagementFee { get; set; }

    /// <summary>Expected realization percentage (0–100). Asked of every department.</summary>
    public decimal? RealizationPercentage { get; set; }

    /// <summary>How often the client is billed: a foreign key to the <c>REMS.BillingPeriod</c> item.</summary>
    public Guid? BillingPeriodId { get; set; }

    /// <summary>
    /// How this engagement is actually billed, in the firm's own words — "three progress bills against
    /// the fixed fee, the balance on delivery", "monthly in arrears against timesheets". It was a COUNT
    /// (No. of Bills), and a count could not carry any of that: a schedule is a sentence, not a number,
    /// and the number on its own said how many invoices without saying what triggered one.
    /// </summary>
    public string? BillingProcessDescription { get; set; }

    /// <summary>Engagement approval lifecycle status.</summary>
    public RemsEngagementStatus Status { get; set; }

    // Nothing records when the firm's shareholders joined this engagement's approver list, because nothing
    // writes them onto it: they route by standing, like the director and the CSE, and are not removable.

    // ---- Navigations ----
    // Note: the 0..1 detail relationships (audit/government/tax) and the engagement-per-request link are
    // modelled as one-to-many at the EF level (child holds the FK, no principal reference nav) so that
    // "one active per parent" can be enforced by a filtered unique index (WHERE [Deleted] = 0) rather
    // than EF's non-filtered convention 1:1 index, which would block soft-delete + re-create.
    public REMS? Rems { get; set; }
    // The four option-set references above. Every read goes through these — `.Value` is the code the
    // application branches on and the API puts on the wire.
    public OptionSetItem? Department { get; set; }
    public OptionSetItem? ServiceLine { get; set; }
    public OptionSetItem? Industry { get; set; }
    public OptionSetItem? BillingPeriod { get; set; }
    public ICollection<REMSEngagementMarketingMethod> MarketingMethods { get; set; } = new List<REMSEngagementMarketingMethod>();
    public ICollection<REMSEngagementCommissionSplit> CommissionSplits { get; set; } = new List<REMSEngagementCommissionSplit>();
    public ICollection<REMSEngagementApprover> Approvers { get; set; } = new List<REMSEngagementApprover>();
    public ICollection<REMSApprovalRound> ApprovalRounds { get; set; } = new List<REMSApprovalRound>();
}
