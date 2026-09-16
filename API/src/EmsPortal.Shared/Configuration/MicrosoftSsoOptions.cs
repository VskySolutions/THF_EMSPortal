namespace EmsPortal.Shared.Configuration;

/// <summary>
/// "Login with Microsoft" (Microsoft 365 / Entra ID), bound from <c>Authentication:Microsoft</c>. The three keys
/// come from the Entra app registration and are meant to be supplied through the API's <c>.env</c> file rather
/// than appsettings. Leaving any of them empty keeps the feature switched off.
/// </summary>
public sealed class MicrosoftSsoOptions
{
    /// <summary>
    /// Directory (tenant) ID of the organisation whose accounts may sign in. One tenant only: the multi-tenant
    /// aliases (<c>common</c>, <c>organizations</c>, <c>consumers</c>) are refused, because they would let any
    /// Microsoft account through as long as its email matched a user.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Application (client) ID of the app registration.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>A client secret from the app registration's "Certificates &amp; secrets" page.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The exact redirect URI registered in Entra. Empty means it is derived from the incoming request
    /// (<c>{scheme}://{host}/api/auth/microsoft/callback</c>); set it when a proxy hides the public host.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Optional OpenID Connect <c>prompt</c> (e.g. <c>select_account</c>). Empty lets Microsoft sign a single
    /// signed-in work account straight in.
    /// </summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Entra's sign-in host. Only changes for national clouds.</summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com";

    private static readonly string[] MultiTenantAliases = { "common", "organizations", "consumers" };

    /// <summary>Why the feature is unavailable, or null when every required setting is present.</summary>
    public string? ConfigurationProblem
    {
        get
        {
            var missing = new List<string>(3);
            if (string.IsNullOrWhiteSpace(TenantId))
            {
                missing.Add(nameof(TenantId));
            }

            if (string.IsNullOrWhiteSpace(ClientId))
            {
                missing.Add(nameof(ClientId));
            }

            if (string.IsNullOrWhiteSpace(ClientSecret))
            {
                missing.Add(nameof(ClientSecret));
            }

            if (missing.Count > 0)
            {
                return $"Authentication:Microsoft:{string.Join(", ", missing)} not set.";
            }

            if (MultiTenantAliases.Contains(TenantId.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return $"Authentication:Microsoft:TenantId must be your directory's tenant id, not '{TenantId.Trim()}'.";
            }

            return null;
        }
    }

    /// <summary>The tenant-specific authority every Entra endpoint hangs off.</summary>
    public string Authority => $"{Instance.TrimEnd('/')}/{TenantId.Trim()}";
}
