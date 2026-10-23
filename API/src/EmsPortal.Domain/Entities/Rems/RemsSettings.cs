namespace EmsPortal.Domain.Entities;

/// <summary>
/// Per-tenant REMS engagement/approval configuration (WO-114). Exactly one row per tenant holds the
/// department-to-director mapping used to prefill an engagement's
/// <see cref="REMSEngagement.DepartmentDirectorId"/>. Optional throughout ("unassigned placeholder"
/// allowed). Inherits the standard audit/soft-delete fields.
/// <para>
/// It also held the firm-wide managing shareholder, who was added to every approval round. That seat is
/// gone: an engagement is signed off by the people it names, and anyone else whose signature it needs is
/// added on its own Approval tab.
/// </para>
/// </summary>
public class RemsSettings : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant (tenant-scoped, one row per tenant).</summary>
    public Guid TenantId { get; set; }

    /// <summary>How many declines reject a round. One by default.</summary>
    public int DeclineThreshold { get; set; } = 1;
    /// <summary>Tax engagements at or under this fee skip the shareholder seat; null means never skip.</summary>
    public decimal? TaxFeeCeilingWithoutShareholder { get; set; } = 10000m;
    /// <summary>Whether the firm's fixed approver routing (STATIC-APPROVAL-POLICY) is in force.</summary>
    public bool IsStaticRouting { get; set; } = true;

    // ---- Navigations ----
    public ICollection<RemsDepartmentDirector> DepartmentDirectors { get; set; } = new List<RemsDepartmentDirector>();
    public ICollection<RemsChecklistTemplate> ChecklistTemplates { get; set; } = new List<RemsChecklistTemplate>();
}
