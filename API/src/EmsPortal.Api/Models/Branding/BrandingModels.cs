using EmsPortal.Application.Branding;

namespace EmsPortal.Api.Models.Branding;

/// <summary>The image slots a tenant can fill. The route segment is the camelCase name.</summary>
public static class BrandingAssetSlots
{
    public const string Logo = "logo";
    public const string LogoDark = "logoDark";
    public const string LogoMark = "logoMark";
    public const string Favicon = "favicon";
    public const string LoginBackground = "loginBackground";

    public static readonly IReadOnlyList<string> All = new[] { Logo, LogoDark, LogoMark, Favicon, LoginBackground };
}

/// <summary>Where each uploaded image is served from; null for a slot left empty.</summary>
public sealed record BrandingAssetsResponse(
    string? Logo,
    string? LogoDark,
    string? LogoMark,
    string? Favicon,
    string? LoginBackground);

/// <summary>
/// A tenant's branding. <c>theme</c> carries only what was changed — null means "as the application
/// ships". <c>isCustomised</c> is false while the tenant has saved nothing at all.
/// </summary>
public sealed record BrandingResponse(
    Guid TenantId,
    string TenantName,
    string TenantIdentifier,
    bool IsCustomised,
    BrandingTheme Theme,
    BrandingAssetsResponse Assets,
    string? UpdatedByName,
    DateTime? UpdatedOnUtc);

/// <summary>What an anonymous screen may know: enough to draw itself, and nothing about who set it.</summary>
public sealed record PublicBrandingResponse(BrandingTheme Theme, BrandingAssetsResponse Assets);
