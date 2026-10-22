using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>
/// Posting a fully approved request's brand-new client to Maconomy as a customer. Approval and the sync
/// are standalone: approval stages a row and queues the job, and nothing about approval waits on, or
/// fails because of, what happens here. A row that is Failed, or NotSynced because the sync was off,
/// stays so until an admin presses Sync now.
/// </summary>
public interface IMaconomyCustomerSyncService
{
    /// <summary>
    /// Stages a sync row for a request that has just been fully approved: Pending when the tenant's sync
    /// is on, NotSynced when it is off. Nothing is saved here — the caller's unit of work commits it with
    /// the approval. Returns the row to <see cref="Enqueue"/> once committed, or null when nothing is to
    /// be posted (an existing-client request, a client picked from Maconomy, a client already synced).
    /// </summary>
    Task<REMSMaconomyCustomerSync?> RecordOnApprovalAsync(REMS rems, CancellationToken cancellationToken = default);

    /// <summary>Queues the job for a Pending row that has been committed.</summary>
    void Enqueue(REMSMaconomyCustomerSync sync);

    /// <summary>The job body: one attempt, recorded on the row. Null when the row no longer exists.</summary>
    Task<REMSMaconomyCustomerSync?> RunAsync(Guid syncId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync now: one attempt, inline, for a Failed or NotSynced row — or for an approved brand-new-client
    /// request with no row yet, whose row is created on demand. Throws
    /// <see cref="MaconomyCustomerSyncException"/> when the sync is off, the request is not one that is
    /// posted, or an attempt is already under way.
    /// </summary>
    Task<REMSMaconomyCustomerSync> SyncNowAsync(Guid remsId, Guid tenantId, Guid actorId, CancellationToken cancellationToken = default);

    /// <summary>Whether the tenant's connection is on and its customer sync switched on.</summary>
    Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Start or stop the sync. Start posts nothing by itself; Stop does not interrupt a job already running.</summary>
    Task<MaconomyConnection> SetEnabledAsync(Guid tenantId, bool enabled, Guid actorId, CancellationToken cancellationToken = default);

    Task<MaconomyCustomerSyncOverview> GetOverviewAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>The rows an admin may want to Sync now: Failed, NotSynced, and Syncing for longer than a job can run.</summary>
    Task<IReadOnlyList<REMSMaconomyCustomerSync>> ListUnsyncedAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>A row still Syncing longer than a job can run: the portal lost track of that attempt.</summary>
    bool IsStale(REMSMaconomyCustomerSync sync);
}

/// <summary>Hands a committed Pending row to the Workers process.</summary>
public interface IMaconomyCustomerSyncQueue
{
    void Enqueue(Guid syncId, Guid tenantId);
}

/// <summary>The settings page's Customer sync card: the switch, who last moved it, and the counts.</summary>
public sealed record MaconomyCustomerSyncOverview(
    bool Connected,
    bool Enabled,
    DateTime? ChangedOnUtc,
    Guid? ChangedById,
    int Synced,
    int Linked,
    int Failed,
    int NotSynced,
    int Pending);

/// <summary>Why Sync now, Start or Stop was refused.</summary>
public enum MaconomyCustomerSyncRefusal
{
    /// <summary>The tenant has no Maconomy connection, or it is switched off.</summary>
    NotConnected,

    /// <summary>The customer sync is switched off.</summary>
    SyncOff,

    /// <summary>The request is not one whose client is posted.</summary>
    NotEligible,

    /// <summary>An attempt is already under way.</summary>
    Running,

    /// <summary>No such request in the tenant.</summary>
    NotFound,
}

public sealed class MaconomyCustomerSyncException : Exception
{
    public MaconomyCustomerSyncException(MaconomyCustomerSyncRefusal refusal, string message) : base(message)
        => Refusal = refusal;

    public MaconomyCustomerSyncRefusal Refusal { get; }
}
