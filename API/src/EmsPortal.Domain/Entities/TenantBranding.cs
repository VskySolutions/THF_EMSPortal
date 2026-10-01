namespace EmsPortal.Domain.Entities;

/// <summary>
/// How a tenant's copy of the application looks: its name, logos, colours, type and button styling.
/// One row per tenant, and no row at all until an admin changes something — the absence of a row IS the
/// stock look. Inherits the standard audit/soft-delete fields from <see cref="AuditableEntity"/>.
/// </summary>
public class TenantBranding : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant (tenant-scoped, one live row per tenant).</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The theme as JSON (<c>BrandingTheme</c>): identity text, colours, typography, buttons, shape and
    /// the sign-in screen. Only what the tenant changed is stored; anything absent falls back to the
    /// application's own styling. Validated on the way in, because the values end up inside CSS.
    /// </summary>
    public string ThemeJson { get; set; } = "{}";

    /// <summary>The main logo, shown on light surfaces (side menu, sign-in screen).</summary>
    public Guid? LogoMediaId { get; set; }

    /// <summary>The logo for dark or coloured surfaces (the public form's header).</summary>
    public Guid? LogoDarkMediaId { get; set; }

    /// <summary>The square mark shown when the side menu is collapsed to its icon rail.</summary>
    public Guid? LogoMarkMediaId { get; set; }

    /// <summary>The browser-tab icon.</summary>
    public Guid? FaviconMediaId { get; set; }

    /// <summary>The image behind the sign-in card.</summary>
    public Guid? LoginBackgroundMediaId { get; set; }
}
