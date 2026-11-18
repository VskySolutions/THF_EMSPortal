using System.Text.RegularExpressions;

namespace EmsPortal.Application.Abstractions.Branding;

/// <summary>
/// What a tenant's site goes by: the application name its branding sets, else the tenant's own name.
/// The same name the web app shows, so wording and request numbers follow it.
/// </summary>
public interface ISiteNameProvider
{
    Task<string> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary><paramref name="text"/> with the word "REMS" said as the tenant's site name; untouched without a tenant.</summary>
    Task<string> ApplyAsync(Guid? tenantId, string text, CancellationToken cancellationToken = default);
}

public static partial class SiteWording
{
    public const string Fallback = "COP Portal";

    // The word on its own: a request number already issued ("REMS-81") is a record's name and stays.
    [GeneratedRegex(@"\bREMS\b(?!-\d)")]
    private static partial Regex Word();

    public static bool Mentions(string? text) => !string.IsNullOrEmpty(text) && Word().IsMatch(text);

    public static string Apply(string text, string siteName) => Word().Replace(text, _ => siteName);
}
