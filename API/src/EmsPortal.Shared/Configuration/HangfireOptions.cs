namespace EmsPortal.Shared.Configuration;

/// <summary>
/// Configuration for the Hangfire background queue: the storage every host shares, and the server the
/// API runs by default (or a separate Workers host, when <see cref="ServerEnabled"/> is off on the API).
/// </summary>
public sealed class HangfireOptions
{
    /// <summary>Number of concurrent worker threads on the host that runs the server.</summary>
    public int WorkerCount { get; set; } = 10;

    /// <summary>
    /// Whether the API runs the Hangfire server itself, so one publish carries the queued and recurring
    /// jobs. Off for a deployment that runs the separate Workers host instead; that host ignores it.
    /// </summary>
    public bool ServerEnabled { get; set; } = true;

    /// <summary>Whether the Hangfire dashboard is exposed by the host.</summary>
    public bool DashboardEnabled { get; set; } = true;

    /// <summary>Schema name used for the Hangfire SQL Server tables.</summary>
    public string SchemaName { get; set; } = "HangFire";

    /// <summary>Logical name of this Hangfire server instance. Empty uses the machine default.</summary>
    public string ServerName { get; set; } = string.Empty;
}
