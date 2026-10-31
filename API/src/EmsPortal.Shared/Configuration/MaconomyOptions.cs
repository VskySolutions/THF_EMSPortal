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

    /// <summary>The customer card's container as the customer sync writes it. The field names are code: MaconomyCustomerDataMapping.</summary>
    public MaconomyCustomerCardOptions CustomerCard { get; set; } = new();

    /// <summary>Minutes after which a sync row still Syncing is taken to be an attempt the portal lost track of.</summary>
    public int CustomerSyncStaleMinutes { get; set; } = 10;

    /// <summary>The job card's container as the job sync writes it. The field names are code: MaconomyJobDataMapping.</summary>
    public MaconomyJobCardOptions JobCard { get; set; } = new();

    /// <summary>Minutes after which a request still Syncing its job is taken to be an attempt the portal lost track of.</summary>
    public int JobSyncStaleMinutes { get; set; } = 10;
}

/// <summary>
/// The job card's container, the one thing about the write that differs per instance. The field names and
/// what goes in them are code: <c>MaconomyJobDataMapping</c>.
/// </summary>
public sealed class MaconomyJobCardOptions
{
    public string Container { get; set; } = "jobs";

    /// <summary>The Maconomy employee number every job names as project manager (THF: 82). Null refuses every sync, naming this key.</summary>
    public string? ProjectManagerNumber { get; set; }

    /// <summary>The template job used when a request's Job Template carries no Maconomy number, or the request names none. Null refuses such a request.</summary>
    public string? DefaultTemplateJobNumber { get; set; }
}

/// <summary>
/// The customer card's container as the customer sync WRITES it: the one THF's Maconomy team named for
/// creating customers (<c>dpl:customercard</c>, 22 Oct 2026, on d104p). The test instance thmasd01 has only
/// the plain <c>customercard</c>, so the dev config sets that (26 Oct 2026); reads keep using
/// <c>customercard</c>. The field names and what goes in them are code: <c>MaconomyCustomerDataMapping</c>.
/// </summary>
public sealed class MaconomyCustomerCardOptions
{
    public string Container { get; set; } = "dpl:customercard";
}
