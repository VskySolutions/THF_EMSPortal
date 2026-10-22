using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// Data access for <see cref="REMSMaconomyCustomerSync"/>. The reads that take a tenant id bypass the
/// ambient filter and pin to that tenant, as the connection repository does: the job runs with whatever
/// tenant Hangfire restored, and a Super Admin works another tenant's rows through <c>?tenantId=</c>.
/// </summary>
public interface IRemsMaconomyCustomerSyncRepository
{
    Task<REMSMaconomyCustomerSync?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);

    Task<REMSMaconomyCustomerSync?> GetByRemsIdAsync(Guid remsId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>The rows for these requests in the active tenant, keyed by request id. For list rows.</summary>
    Task<IReadOnlyDictionary<Guid, REMSMaconomyCustomerSync>> ListByRemsIdsAsync(
        IReadOnlyCollection<Guid> remsIds, CancellationToken cancellationToken = default);

    /// <summary>Failed, NotSynced, and Syncing since before <paramref name="staleBeforeUtc"/>; newest first, each with its request.</summary>
    Task<IReadOnlyList<REMSMaconomyCustomerSync>> ListUnsyncedAsync(
        Guid tenantId, DateTime staleBeforeUtc, CancellationToken cancellationToken = default);

    Task<RemsMaconomyCustomerSyncCounts> CountAsync(Guid tenantId, DateTime staleBeforeUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approved Brand New Client requests whose client is not a Maconomy customer and that have no sync
    /// row at all — approved before the sync existed. Newest first, each with its type, status and client.
    /// </summary>
    Task<IReadOnlyList<REMS>> ListApprovedUnrecordedAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<int> CountApprovedUnrecordedAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What an attempt posts: the request with its client, the entity type the client was asked under
    /// (the sent intake form's, else the latest form's) and the engagement's industry, each as the
    /// option code. Null when the request is not in the tenant.
    /// </summary>
    Task<RemsMaconomyCustomerSyncSubject?> GetSubjectAsync(Guid remsId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Whether a Person other than this one already holds the customer number in the tenant.</summary>
    Task<bool> NumberHeldByAnotherAsync(
        Guid tenantId, string customerNumber, Guid exceptPersonId, CancellationToken cancellationToken = default);

    Task AddAsync(REMSMaconomyCustomerSync sync, CancellationToken cancellationToken = default);

    void Update(REMSMaconomyCustomerSync sync);
}

public sealed record RemsMaconomyCustomerSyncSubject(REMS Rems, string? EntityTypeCode, string? IndustryCode);

/// <summary>Rows per status. <c>Stale</c> is the Syncing rows older than the cutoff, counted apart from the live ones.</summary>
public sealed record RemsMaconomyCustomerSyncCounts(
    int Synced, int Linked, int Failed, int NotSynced, int Pending, int Syncing, int Stale)
{
    public static readonly RemsMaconomyCustomerSyncCounts Empty = new(0, 0, 0, 0, 0, 0, 0);
}
