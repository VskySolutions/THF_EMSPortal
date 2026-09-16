namespace EmsPortal.Domain.Entities;

/// <summary>
/// Government-specific detail for a <see cref="REMSEngagement"/> (WO-110), at most one per engagement.
/// Contract and purchase-order dates are date-only. Copied from the submitted form payload on submit.
/// </summary>
public class REMSEngagementGovernmentDetail : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant (tenant-scoped).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Owning engagement.</summary>
    public Guid REMSEngagementId { get; set; }

    /// <summary>Contract start date.</summary>
    public DateOnly? ContractStartDate { get; set; }

    /// <summary>Contract end date.</summary>
    public DateOnly? ContractEndDate { get; set; }

    /// <summary>Original contract term description.</summary>
    public string? OriginalTerm { get; set; }

    /// <summary>Renewal terms description.</summary>
    public string? RenewalTerms { get; set; }

    /// <summary>Purchase order start date.</summary>
    public DateOnly? PurchaseOrderStartDate { get; set; }

    /// <summary>Purchase order end date.</summary>
    public DateOnly? PurchaseOrderEndDate { get; set; }

    /// <summary>Contract number.</summary>
    public string? ContractNumber { get; set; }

    /// <summary>Whether the Florida 1% state fee applies.</summary>
    public bool? FloridaOnePercentStateFeeApplies { get; set; }

    // ---- GCS ----
    // The GCS department is set up against a purchase order, and it is the SAME purchase order the two
    // date columns above already carry — a government client is asked for its PO start and end on the
    // intake form, and those answers are copied here. So GCS extends this row rather than opening a table
    // of its own: a second copy of the PO dates is a second copy that can disagree with the first.
    // A government AUDIT leaves everything below null, and a GCS engagement leaves the contract block above
    // it null unless the client happens to have answered for it.

    /// <summary>GCS: the purchase order's own reference. Alphanumeric — it is a reference, not a number.</summary>
    public string? PurchaseOrderNumber { get; set; }

    /// <summary>GCS: what the purchase order is worth.</summary>
    public decimal? PurchaseOrderAmount { get; set; }

    /// <summary>GCS: the uploaded purchase order document (Media).</summary>
    public Guid? PurchaseOrderMediaId { get; set; }

    // ---- Navigations ----
    public REMSEngagement? Engagement { get; set; }
    public Media? PurchaseOrderMedia { get; set; }

    /// <summary>
    /// GCS: the rate card — the hourly rate at each personnel level the engagement is staffed at. Every
    /// level on the list is offered on the setup; only the ones given a rate have a row here.
    /// </summary>
    public ICollection<REMSEngagementPersonnelRate> PersonnelRates { get; set; } = new List<REMSEngagementPersonnelRate>();
}
