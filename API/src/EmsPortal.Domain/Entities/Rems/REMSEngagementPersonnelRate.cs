namespace EmsPortal.Domain.Entities;

/// <summary>
/// One line of a GCS engagement's rate card: the hourly rate the engagement bills at one personnel
/// level. <see cref="PersonnelLevelId"/> is a foreign key to the <c>REMS.PersonnelLevel</c> item, and
/// a level the engagement is not staffed at simply has no row.
/// </summary>
public class REMSEngagementPersonnelRate : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant (tenant-scoped).</summary>
    public Guid TenantId { get; set; }

    /// <summary>The government detail the rate card belongs to — the row that carries the purchase order it bills against.</summary>
    public Guid REMSEngagementGovernmentDetailId { get; set; }

    /// <summary>The personnel level (option-set item).</summary>
    public Guid PersonnelLevelId { get; set; }

    /// <summary>The hourly rate billed at that level.</summary>
    public decimal BillRatePerHour { get; set; }

    // ---- Navigations ----
    public REMSEngagementGovernmentDetail? GovernmentDetail { get; set; }
    public OptionSetItem? PersonnelLevel { get; set; }
}
