namespace EmsPortal.Application.Common;

/// <summary>
/// The developer addresses a tenant names, as the one column holds them: semicolon-separated, each once,
/// in the order given. The portal writes to them when an integration it runs for the tenant breaks — the
/// Maconomy connection switching itself off — rather than to the tenant's users.
/// </summary>
public static class TenantDeveloperEmails
{
    private const char Separator = ';';

    /// <summary>The addresses in the stored column; none for a blank.</summary>
    public static IReadOnlyList<string> Parse(string? stored)
        => string.IsNullOrWhiteSpace(stored) ? Array.Empty<string>() : Normalize(stored.Split(Separator));

    /// <summary>The column for a list: trimmed, blanks dropped, each address once; null when none are left.</summary>
    public static string? Join(IEnumerable<string>? emails)
    {
        var list = Normalize(emails);
        return list.Count == 0 ? null : string.Join(Separator, list);
    }

    /// <summary>Trimmed, blanks dropped, and each address once whatever its case, the first spelling kept.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? emails)
    {
        if (emails is null)
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var raw in emails)
        {
            var email = raw?.Trim();
            if (!string.IsNullOrEmpty(email) && seen.Add(email))
            {
                list.Add(email);
            }
        }
        return list;
    }
}
