using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// An independent organization served by the platform. Owns its own isolated data
/// and users (Multi-Tenancy).
/// </summary>
public class Tenant : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Human-readable tenant name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Unique, URL-safe slug. Immutable after creation.</summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>IANA time zone id used to render this tenant's UTC timestamps. Defaults to UTC.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>Lifecycle status. Defaults to Active on creation (AC-TNT-001.4).</summary>
    public TenantStatus Status { get; set; } = TenantStatus.Active;

    /// <summary>UTC timestamp when the tenant was created.</summary>
    public DateTime CreatedDate { get; set; }

    /// <summary>
    /// Who the portal emails when an integration it runs for the tenant breaks — the people who look after
    /// it, not its users. Semicolon-separated (<c>TenantDeveloperEmails</c> reads and writes it); null when
    /// nobody is named, in which case the portal only logs.
    /// </summary>
    public string? DeveloperEmails { get; set; }
}
