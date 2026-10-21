namespace EmsPortal.Domain.Entities;

/// <summary>
/// Attest-side detail for a <see cref="REMSEngagement"/> (WO-110), at most one per engagement. Holds the
/// signed client-acceptance form required before an audit engagement can be approved.
/// <para>
/// Shared by the AUDIT and ASSURANCE departments rather than split in two. The client-acceptance form is
/// the same compliance artifact under both, filed and read the same way, and a second table holding one
/// media id would have needed a second upload endpoint and a second read on every screen that shows it.
/// The column below it is Assurance's alone; audit engagements leave it null.
/// </para>
/// </summary>
public class REMSEngagementAuditDetail : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant (tenant-scoped).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Owning engagement.</summary>
    public Guid REMSEngagementId { get; set; }

    /// <summary>The uploaded client-acceptance form PDF (Media).</summary>
    public Guid? ClientAcceptanceFormMediaId { get; set; }

    /// <summary>
    /// ASSURANCE only: the CLIENT's fiscal year end. Distinct from the Tax department's
    /// <c>REMSEngagementTaxDetail.FiscalYearEnd</c>, which drives a filing schedule — this one dates the
    /// period being examined and computes nothing.
    /// </summary>
    public DateOnly? ClientFiscalYearEnd { get; set; }


    // ---- Navigations ----
    public REMSEngagement? Engagement { get; set; }
    public Media? ClientAcceptanceFormMedia { get; set; }
}
