using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>
/// Posts an approved engagement to Maconomy as a job. It is queued by itself once the request is approved
/// and its client is in Maconomy (straight after the final approval, or after the customer sync has made
/// the client a customer), and pressed by hand (Sync job now) for a request that failed or was approved
/// while the sync was off. The state is five columns on the request (<see cref="RemsRequest.MaconomyJobNumber"/>,
/// <see cref="RemsRequest.MaconomyJobSyncStatus"/>, <see cref="RemsRequest.MaconomyJobSyncedById"/>,
/// <see cref="RemsRequest.MaconomyJobSyncedOnUtc"/>, <see cref="RemsRequest.MaconomyJobSyncError"/>).
/// The project manager and the default template job are platform configuration; the template job
/// numbers are the REMS Job Template option items' (<see cref="IMaconomyNumberMaps"/>).
/// </summary>
public interface IMaconomyJobSyncService
{
    /// <summary>What every row needs to answer "may this be posted now": the switch, the configured numbers, the template map.</summary>
    Task<MaconomyJobSyncContext> GetContextAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an approved request whose client is in Maconomy as waiting to be posted, while the job sync is
    /// on. Returns the request when a background attempt should be queued, null when nothing is to be done.
    /// <paramref name="requireApproved"/> is false on the approval itself, whose status change is not yet saved.
    /// </summary>
    Task<RemsRequest?> RecordPendingAsync(RemsRequest request, bool requireApproved, CancellationToken cancellationToken = default);

    void Enqueue(RemsRequest request);

    /// <summary>The background attempt for one request. A failed post, or a request that lacks something, is recorded on the row.</summary>
    Task<RemsRequest?> RunAsync(Guid remsId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>One attempt, inline, by the person named. Throws <see cref="MaconomyJobSyncException"/> for a refusal; a failed post is recorded on the row, not thrown.</summary>
    Task<RemsRequest> SyncNowAsync(Guid remsId, Guid tenantId, Guid actorId, CancellationToken cancellationToken = default);

    /// <summary>The request with everything the job needs loaded, or null. For the confirmation shown before a press.</summary>
    Task<RemsRequest?> GetAsync(Guid remsId, CancellationToken cancellationToken = default);

    /// <summary>Whether a request has sat queued or in Syncing longer than an attempt can take.</summary>
    bool IsStale(RemsRequest request);

    Task<MaconomyConnection> SetEnabledAsync(Guid tenantId, bool enabled, Guid actorId, CancellationToken cancellationToken = default);
    Task<MaconomyJobSyncOverview> GetOverviewAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>The caller's tenant's approved requests, newest change first, with the client, service line and template loaded.</summary>
    Task<IReadOnlyList<RemsRequest>> ListApprovedAsync(CancellationToken cancellationToken = default);
}

/// <summary>The background attempt's queue: what the approval, or the customer sync, enqueues.</summary>
public interface IMaconomyJobSyncQueue
{
    void Enqueue(Guid remsId, Guid tenantId);
}

/// <summary>The tenant's job sync as the rules read it. Built once per API call and handed to every row.</summary>
public sealed record MaconomyJobSyncContext(
    bool Connected,
    bool Enabled,
    string? ProjectManagerNumber,
    string? DefaultTemplateNumber,
    IReadOnlyDictionary<Guid, string> TemplateMap)
{
    public static readonly MaconomyJobSyncContext None = new(false, false, null, null, new Dictionary<Guid, string>());
}

public sealed record MaconomyJobSyncOverview(
    bool Connected,
    bool Enabled,
    DateTime? ChangedOnUtc,
    Guid? ChangedById,
    /// <summary>The configured project manager and default template job, read-only on the Job tab.</summary>
    string? ProjectManagerNumber,
    string? DefaultTemplateNumber,
    int Synced,
    int Failed,
    int Syncing,
    int Waiting);

/// <summary>A rule of the job sync broken by the caller. The refusal kinds are the customer sync's: the API words them the same way.</summary>
public sealed class MaconomyJobSyncException : Exception
{
    public MaconomyJobSyncException(MaconomyCustomerSyncRefusal refusal, string message) : base(message)
        => Refusal = refusal;

    public MaconomyCustomerSyncRefusal Refusal { get; }
}
