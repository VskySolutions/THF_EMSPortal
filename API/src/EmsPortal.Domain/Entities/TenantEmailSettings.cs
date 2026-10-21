namespace EmsPortal.Domain.Entities;

/// <summary>
/// A tenant's outgoing-email settings. One row per tenant, and none at all until an admin changes
/// something: no row means notifications are on. The row holds only this, so its audit pair (from
/// <see cref="AuditableEntity"/>) says who last threw the switch, and when.
/// </summary>
public class TenantEmailSettings : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant (tenant-scoped, one live row per tenant).</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// False holds back every notification email the tenant would send: invitations, password resets,
    /// REMS form links and reminders alike. A test send from the Email Accounts screen is explicit and
    /// still goes out.
    /// </summary>
    public bool NotificationsEnabled { get; set; } = true;
}
