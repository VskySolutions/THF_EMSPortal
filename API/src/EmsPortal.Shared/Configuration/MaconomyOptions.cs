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

    /// <summary>
    /// Seconds the details of a tenant's Maconomy clients are held in memory before they are read again.
    /// Every list, request page and email reads from that copy, so this is the longest a change made in
    /// Maconomy takes to show up without a refresh.
    /// </summary>
    public int CustomerCacheSeconds { get; set; } = 600;

    /// <summary>Customer numbers asked for in one call when details are read by number.</summary>
    public int NumbersPerCall { get; set; } = 100;

    /// <summary>
    /// Seconds Maconomy is left alone after it refused the login or could not be reached. Without it
    /// every search is one more failed login against the service account. Zero disables it.
    /// </summary>
    public int FailureCooldownSeconds { get; set; } = 60;

    /// <summary>
    /// Reads EVERY customer into memory once per <see cref="CustomerCacheSeconds"/>, so the client search
    /// makes no call at all. Off: only the customers the portal's requests name are held, and a search
    /// asks Maconomy.
    /// </summary>
    public bool PreloadAllCustomers { get; set; }

    /// <summary>Row cap for that one read. A Maconomy holding more is searched live instead.</summary>
    public int PreloadMaxRows { get; set; } = 50000;
}
