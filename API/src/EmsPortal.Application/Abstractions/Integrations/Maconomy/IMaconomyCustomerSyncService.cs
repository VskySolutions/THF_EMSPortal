using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>
/// Posts an approved brand-new client to Maconomy as a customer. The sync state is three columns on the
/// client (<see cref="Client.MaconomySyncStatus"/>, <see cref="Client.MaconomySyncedOnUtc"/>,
/// <see cref="Client.MaconomySyncError"/>); the request that made the client postable is only the
/// subject of the activity log.
/// </summary>
public interface IMaconomyCustomerSyncService
{
    /// <summary>
    /// Marks the client of an approved brand-new-client request as waiting to be posted. Returns the client
    /// when a background attempt should be queued, null when nothing is to be done.
    /// </summary>
    Task<Client?> RecordOnApprovalAsync(RemsRequest request, CancellationToken cancellationToken = default);

    void Enqueue(Client client);

    /// <summary>The background attempt for one client.</summary>
    Task<Client?> RunAsync(Guid clientId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>An attempt a person asked for from a request's page.</summary>
    Task<Client> SyncNowAsync(Guid remsId, Guid tenantId, Guid actorId, CancellationToken cancellationToken = default);

    Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<MaconomyConnection> SetEnabledAsync(Guid tenantId, bool enabled, Guid actorId, CancellationToken cancellationToken = default);
    Task<MaconomyCustomerSyncOverview> GetOverviewAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Clients still to post, each with the request that made it postable where one is known.</summary>
    Task<IReadOnlyList<MaconomyUnsyncedClient>> ListUnsyncedAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Whether a client has sat in Syncing longer than an attempt can take.</summary>
    bool IsStale(Client client);
}

public sealed record MaconomyUnsyncedClient(Client Client, RemsRequest? Request);

public interface IMaconomyCustomerSyncQueue
{
    void Enqueue(Guid clientId, Guid tenantId);
}

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

public enum MaconomyCustomerSyncRefusal
{
    NotConnected,
    SyncOff,
    NotEligible,
    Running,
    NotFound,
}

public sealed class MaconomyCustomerSyncException : Exception
{
    public MaconomyCustomerSyncException(MaconomyCustomerSyncRefusal refusal, string message) : base(message)
        => Refusal = refusal;

    public MaconomyCustomerSyncRefusal Refusal { get; }
}
