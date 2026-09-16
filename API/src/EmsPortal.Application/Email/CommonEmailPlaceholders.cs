namespace EmsPortal.Application.Email;

/// <summary>
/// The placeholders every email may use whatever it is about — where the portal lives and whose it is.
/// One definition, so a pre-send preview fills the same tokens the send itself does.
/// </summary>
public static class CommonEmailPlaceholders
{
    public const string TenantName = "TenantName";
    public const string LoginUrl = "LoginUrl";
    public const string AppBaseUrl = "AppBaseUrl";

    /// <summary>The common values underneath <paramref name="model"/>; anything the caller set wins.</summary>
    public static Dictionary<string, string?> Merge(
        IReadOnlyDictionary<string, string?> model, string baseUrl, string? tenantName)
    {
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [LoginUrl] = baseUrl,
            [AppBaseUrl] = baseUrl,
        };
        if (!string.IsNullOrWhiteSpace(tenantName))
        {
            merged[TenantName] = tenantName;
        }
        foreach (var kv in model)
        {
            merged[kv.Key] = kv.Value;
        }
        return merged;
    }
}
