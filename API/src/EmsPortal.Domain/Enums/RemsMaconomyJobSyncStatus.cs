namespace EmsPortal.Domain.Enums;

/// <summary>
/// Where posting a request's engagement to Maconomy as a job has got to. Stored as a string, like
/// <see cref="RemsMaconomyCustomerSyncStatus"/>. No Pending: the job sync is pressed by hand and runs
/// inline, so nothing ever waits in a queue.
/// </summary>
public enum RemsMaconomyJobSyncStatus
{
    /// <summary>No attempt yet.</summary>
    NotSynced,

    /// <summary>
    /// An attempt is under way. Set BEFORE the create is sent, so a crash between the create and the
    /// record leaves a trace rather than a second job on the next press.
    /// </summary>
    Syncing,

    /// <summary>Maconomy created the job; the number it assigned is on the row.</summary>
    Synced,

    /// <summary>The one attempt failed, with the reason on the row. It stays Failed until somebody presses Sync job now again.</summary>
    Failed,
}
