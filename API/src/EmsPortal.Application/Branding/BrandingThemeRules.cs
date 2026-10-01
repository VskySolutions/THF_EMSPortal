using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EmsPortal.Application.Branding;

/// <summary>
/// What a theme is allowed to contain. The values are written into a stylesheet in every user's browser,
/// so nothing is taken on trust: colours must be hex, fonts must come from the list, numbers must sit in
/// a range a page can survive, and free text is length-capped. Anything else is refused, not cleaned up.
/// </summary>
public static partial class BrandingThemeRules
{
    /// <summary>
    /// The fonts a tenant may choose. Each is loaded from Google Fonts by name, except the system stack.
    /// Mirrored by the picker in <c>WEB/src/services/branding.js</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> Fonts = new HashSet<string>(StringComparer.Ordinal)
    {
        "System",
        "Poppins", "Inter", "Roboto", "Open Sans", "Lato", "Montserrat", "Nunito", "Nunito Sans",
        "Source Sans 3", "Work Sans", "DM Sans", "Manrope", "Plus Jakarta Sans", "Raleway", "Rubik",
        "IBM Plex Sans", "Noto Sans",
        "Merriweather", "Playfair Display", "Lora", "Source Serif 4", "Roboto Slab",
    };

    private static readonly string[] Transforms = { "none", "uppercase", "capitalize" };
    private static readonly string[] Shadows = { "none", "soft", "raised" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Email();

    /// <summary>The stored form: camelCase, nulls left out.</summary>
    public static string Serialize(BrandingTheme theme) => JsonSerializer.Serialize(theme, JsonOptions);

    /// <summary>A stored theme, or an empty one when the column holds nothing readable.</summary>
    public static BrandingTheme Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new BrandingTheme();
        }

        try
        {
            return Fill(JsonSerializer.Deserialize<BrandingTheme>(json, JsonOptions));
        }
        catch (JsonException)
        {
            return new BrandingTheme();
        }
    }

    /// <summary>
    /// Tidies <paramref name="theme"/> in place (trimmed text, blank to null, colours to lower-case
    /// <c>#rrggbb</c>) and returns what is wrong with it, one message per field. Empty means it may be stored.
    /// </summary>
    public static IReadOnlyList<string> Normalize(BrandingTheme theme)
    {
        Fill(theme);
        var errors = new List<string>();

        var identity = theme.Identity;
        identity.ApplicationName = Text(identity.ApplicationName, 60, "identity.applicationName", errors);
        identity.Tagline = Text(identity.Tagline, 120, "identity.tagline", errors);
        identity.FooterText = Text(identity.FooterText, 200, "identity.footerText", errors);
        identity.SupportEmail = Text(identity.SupportEmail, 200, "identity.supportEmail", errors);
        if (identity.SupportEmail is not null && !Email().IsMatch(identity.SupportEmail))
        {
            errors.Add("identity.supportEmail must be an email address.");
        }
        identity.SupportUrl = Text(identity.SupportUrl, 300, "identity.supportUrl", errors);
        if (identity.SupportUrl is not null
            && !(Uri.TryCreate(identity.SupportUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps))
        {
            errors.Add("identity.supportUrl must be an absolute https:// URL.");
        }
        Range(identity.LogoHeight, 20, 64, "identity.logoHeight", errors);

        // Every property of the colour block is a colour, so the block is walked rather than listed twice.
        foreach (var property in typeof(BrandingColors).GetProperties())
        {
            var name = $"colors.{JsonNamingPolicy.CamelCase.ConvertName(property.Name)}";
            property.SetValue(theme.Colors, Colour((string?)property.GetValue(theme.Colors), name, errors));
        }

        var type = theme.Typography;
        type.BodyFont = Font(type.BodyFont, "typography.bodyFont", errors);
        type.HeadingFont = Font(type.HeadingFont, "typography.headingFont", errors);
        Range(type.BaseFontSize, 12, 18, "typography.baseFontSize", errors);
        type.HeadingColor = Colour(type.HeadingColor, "typography.headingColor", errors);
        Heading(type.H1, "typography.h1", errors);
        Heading(type.H2, "typography.h2", errors);
        Heading(type.H3, "typography.h3", errors);
        Heading(type.H4, "typography.h4", errors);
        Heading(type.H5, "typography.h5", errors);
        Heading(type.H6, "typography.h6", errors);

        var buttons = theme.Buttons;
        Range(buttons.Radius, 0, 28, "buttons.radius", errors);
        Weight(buttons.FontWeight, 400, 700, "buttons.fontWeight", errors);
        Button(buttons.Primary, "buttons.primary", errors);
        Button(buttons.Secondary, "buttons.secondary", errors);

        Range(theme.Shape.CardRadius, 0, 24, "shape.cardRadius", errors);
        Range(theme.Shape.InputRadius, 0, 16, "shape.inputRadius", errors);
        theme.Shape.CardShadow = OneOf(theme.Shape.CardShadow, Shadows, "shape.cardShadow", errors);

        theme.Login.BackgroundColor = Colour(theme.Login.BackgroundColor, "login.backgroundColor", errors);
        theme.Login.Headline = Text(theme.Login.Headline, 80, "login.headline", errors);
        theme.Login.Subtext = Text(theme.Login.Subtext, 160, "login.subtext", errors);

        return errors;
    }

    /// <summary>A body that sent <c>"colors": null</c> still gets a block to read from.</summary>
    private static BrandingTheme Fill(BrandingTheme? theme)
    {
        theme ??= new BrandingTheme();
        theme.Identity ??= new BrandingIdentity();
        theme.Colors ??= new BrandingColors();
        theme.Typography ??= new BrandingTypography();
        theme.Typography.H1 ??= new BrandingHeading();
        theme.Typography.H2 ??= new BrandingHeading();
        theme.Typography.H3 ??= new BrandingHeading();
        theme.Typography.H4 ??= new BrandingHeading();
        theme.Typography.H5 ??= new BrandingHeading();
        theme.Typography.H6 ??= new BrandingHeading();
        theme.Buttons ??= new BrandingButtons();
        theme.Buttons.Primary ??= new BrandingButtonStyle();
        theme.Buttons.Secondary ??= new BrandingButtonStyle();
        theme.Shape ??= new BrandingShape();
        theme.Login ??= new BrandingLogin();
        return theme;
    }

    private static void Heading(BrandingHeading heading, string name, List<string> errors)
    {
        Range(heading.Size, 10, 72, $"{name}.size", errors);
        Weight(heading.Weight, 300, 900, $"{name}.weight", errors);
        Range(heading.LineHeight, 1m, 2m, $"{name}.lineHeight", errors);
        Range(heading.LetterSpacing, -0.05m, 0.3m, $"{name}.letterSpacing", errors);
        heading.Transform = OneOf(heading.Transform, Transforms, $"{name}.transform", errors);
        heading.Color = Colour(heading.Color, $"{name}.color", errors);
    }

    private static void Button(BrandingButtonStyle button, string name, List<string> errors)
    {
        button.Background = Colour(button.Background, $"{name}.background", errors);
        button.Text = Colour(button.Text, $"{name}.text", errors);
        button.Border = Colour(button.Border, $"{name}.border", errors);
    }

    private static string? Text(string? value, int maxLength, string name, List<string> errors)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (text is { Length: var length } && length > maxLength)
        {
            errors.Add($"{name} must be {maxLength} characters or fewer.");
        }
        return text;
    }

    private static string? Colour(string? value, string name, List<string> errors)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (text is null)
        {
            return null;
        }
        if (!HexColour().IsMatch(text))
        {
            errors.Add($"{name} must be a hex colour such as #1f6478.");
            return text;
        }

        // #abc is #aabbcc; stored long so the browser side has one form to read.
        var hex = text.Length == 4 ? string.Concat(text[1..].Select(c => $"{c}{c}")) : text[1..];
        return $"#{hex.ToLowerInvariant()}";
    }

    private static string? Font(string? value, string name, List<string> errors)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (text is not null && !Fonts.Contains(text))
        {
            errors.Add($"{name} is not a font the application offers.");
        }
        return text;
    }

    private static string? OneOf(string? value, string[] allowed, string name, List<string> errors)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        if (text is not null && !allowed.Contains(text))
        {
            errors.Add($"{name} must be one of: {string.Join(", ", allowed)}.");
        }
        return text;
    }

    private static void Range(int? value, int min, int max, string name, List<string> errors)
    {
        if (value is { } number && (number < min || number > max))
        {
            errors.Add($"{name} must be between {min} and {max}.");
        }
    }

    private static void Range(decimal? value, decimal min, decimal max, string name, List<string> errors)
    {
        if (value is { } number && (number < min || number > max))
        {
            errors.Add($"{name} must be between {min} and {max}.");
        }
    }

    private static void Weight(int? value, int min, int max, string name, List<string> errors)
    {
        if (value is { } number && (number < min || number > max || number % 100 != 0))
        {
            errors.Add($"{name} must be a multiple of 100 between {min} and {max}.");
        }
    }
}
