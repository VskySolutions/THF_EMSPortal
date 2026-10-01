namespace EmsPortal.Application.Branding;

/// <summary>
/// A tenant's theme. Every value is optional: null means "as the application ships", so a tenant that
/// changes only its primary colour stores only that. Mirrored by <c>WEB/src/services/branding.js</c>,
/// which holds the stock value each null stands for.
/// </summary>
public sealed class BrandingTheme
{
    public BrandingIdentity Identity { get; set; } = new();
    public BrandingColors Colors { get; set; } = new();
    public BrandingTypography Typography { get; set; } = new();
    public BrandingButtons Buttons { get; set; } = new();
    public BrandingShape Shape { get; set; } = new();
    public BrandingLogin Login { get; set; } = new();
}

/// <summary>What the application calls itself, and the words around the logo.</summary>
public sealed class BrandingIdentity
{
    /// <summary>Shown beside the logo, in the browser tab and on the sign-in screen.</summary>
    public string? ApplicationName { get; set; }

    /// <summary>A line under the name on the sign-in screen.</summary>
    public string? Tagline { get; set; }

    /// <summary>Replaces the copyright line in the page footer.</summary>
    public string? FooterText { get; set; }

    public string? SupportEmail { get; set; }

    public string? SupportUrl { get; set; }

    /// <summary>Logo height in the side menu, in pixels.</summary>
    public int? LogoHeight { get; set; }

    /// <summary>False when the logo already carries the name and the text beside it would repeat it.</summary>
    public bool? ShowNameBesideLogo { get; set; }
}

/// <summary>Every colour is <c>#rrggbb</c>.</summary>
public sealed class BrandingColors
{
    // Brand
    public string? Primary { get; set; }
    public string? Secondary { get; set; }
    public string? Accent { get; set; }

    // Status
    public string? Positive { get; set; }
    public string? Negative { get; set; }
    public string? Warning { get; set; }
    public string? Info { get; set; }

    // Surfaces and text
    public string? PageBackground { get; set; }
    public string? Surface { get; set; }
    public string? Text { get; set; }
    public string? TextMuted { get; set; }
    public string? Border { get; set; }
    public string? Link { get; set; }

    // Chrome
    public string? HeaderBackground { get; set; }
    public string? HeaderText { get; set; }
    public string? SidebarBackground { get; set; }
    public string? SidebarText { get; set; }
    public string? SidebarActiveBackground { get; set; }
    public string? SidebarActiveText { get; set; }
    public string? TableHeaderBackground { get; set; }
    public string? TableHeaderText { get; set; }
}

public sealed class BrandingTypography
{
    /// <summary>One of <see cref="BrandingThemeRules.Fonts"/>.</summary>
    public string? BodyFont { get; set; }

    /// <summary>One of <see cref="BrandingThemeRules.Fonts"/>.</summary>
    public string? HeadingFont { get; set; }

    /// <summary>Body text size in pixels.</summary>
    public int? BaseFontSize { get; set; }

    /// <summary>Colour of every heading that does not set its own.</summary>
    public string? HeadingColor { get; set; }

    public BrandingHeading H1 { get; set; } = new();
    public BrandingHeading H2 { get; set; } = new();
    public BrandingHeading H3 { get; set; } = new();
    public BrandingHeading H4 { get; set; } = new();
    public BrandingHeading H5 { get; set; } = new();
    public BrandingHeading H6 { get; set; } = new();
}

public sealed class BrandingHeading
{
    /// <summary>Pixels.</summary>
    public int? Size { get; set; }

    /// <summary>300 to 900, in hundreds.</summary>
    public int? Weight { get; set; }

    /// <summary>Unitless multiple of the size.</summary>
    public decimal? LineHeight { get; set; }

    /// <summary>In em.</summary>
    public decimal? LetterSpacing { get; set; }

    /// <summary><c>none</c>, <c>uppercase</c> or <c>capitalize</c>.</summary>
    public string? Transform { get; set; }

    public string? Color { get; set; }
}

public sealed class BrandingButtons
{
    /// <summary>Corner radius in pixels, for every button.</summary>
    public int? Radius { get; set; }

    public bool? Uppercase { get; set; }

    /// <summary>400 to 700, in hundreds.</summary>
    public int? FontWeight { get; set; }

    /// <summary>The filled, main-action button.</summary>
    public BrandingButtonStyle Primary { get; set; } = new();

    /// <summary>The outlined, alternative-action button.</summary>
    public BrandingButtonStyle Secondary { get; set; } = new();
}

public sealed class BrandingButtonStyle
{
    public string? Background { get; set; }
    public string? Text { get; set; }
    public string? Border { get; set; }
}

public sealed class BrandingShape
{
    /// <summary>Card corner radius in pixels.</summary>
    public int? CardRadius { get; set; }

    /// <summary>Input corner radius in pixels.</summary>
    public int? InputRadius { get; set; }

    /// <summary><c>none</c>, <c>soft</c> or <c>raised</c>.</summary>
    public string? CardShadow { get; set; }
}

public sealed class BrandingLogin
{
    public string? BackgroundColor { get; set; }
    public string? Headline { get; set; }
    public string? Subtext { get; set; }
}
