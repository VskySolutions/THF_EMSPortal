namespace EmsPortal.Shared.Configuration;

/// <summary>
/// Platform-wide limits for the Maconomy integration, bound from <c>Maconomy</c>. The connection itself
/// (URL, instance, credentials, the tenant's own row cap) is per tenant and lives in the database; these
/// are the ceilings every tenant runs under.
/// </summary>
public sealed class MaconomyOptions
{
    /// <summary>Upper bound on the rows one customer search may ask Maconomy for, whatever the tenant's default.</summary>
    public int MaxLimit { get; set; } = 100;

    /// <summary>Seconds to wait for Maconomy before a call is given up.</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// How long Maconomy keeps a reconnect token alive. A token older than this, less
    /// <see cref="TokenRefreshMarginSeconds"/>, is replaced before a call rather than after the 401.
    /// </summary>
    public int TokenLifetimeMinutes { get; set; } = 15;

    /// <summary>Safety margin taken off the lifetime, so a token is never used in its last seconds.</summary>
    public int TokenRefreshMarginSeconds { get; set; } = 60;

    /// <summary>Seconds a search result is kept per tenant and search text, absorbing keystroke bursts. Zero disables it.</summary>
    public int SearchCacheSeconds { get; set; } = 120;
}
