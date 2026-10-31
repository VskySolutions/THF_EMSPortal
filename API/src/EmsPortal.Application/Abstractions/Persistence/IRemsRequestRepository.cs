using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// The request as one row (REMS) with its REMS_* children. Replaces the request, form and engagement
/// repositories of the old model; the list, inbox and related-entity queries keep their option records.
/// </summary>
public interface IRemsRequestRepository
{
    // ---- Loads ----

    /// <summary>Type, status, client (with person and entity type) and attachments.</summary>
    Task<RemsRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything the setup, submission and approval screens read: the request's option items and seats,
    /// its contract, personnel rates, marketing methods, commission splits, tax forms, due dates, picked
    /// approvers and files, and the client's full graph.
    /// </summary>
    Task<RemsRequest?> GetWorkspaceAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The request with what the Maconomy job sync reads: type, status, client with person, service line, job template.</summary>
    Task<RemsRequest?> GetForJobSyncAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The caller's tenant's approved requests, newest change first, with the same graph loaded. The Job tab's table.</summary>
    Task<IReadOnlyList<RemsRequest>> ListApprovedForJobSyncAsync(CancellationToken cancellationToken = default);

    /// <summary>How the caller's tenant's approved requests stand with the Maconomy job sync; a Syncing older than the cut-off counts as failed.</summary>
    Task<RemsMaconomyJobSyncCounts> CountJobSyncAsync(DateTime staleBeforeUtc, CancellationToken cancellationToken = default);

    /// <summary>The request an invite code opens, outside the tenant filter, with type, status and client.</summary>
    Task<RemsRequest?> GetByInviteCodeUnscopedAsync(string inviteCode, CancellationToken cancellationToken = default);

    Task<bool> InviteCodeExistsAsync(Guid tenantId, string inviteCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, string>> GetNumbersAsync(IReadOnlyCollection<Guid> remsIds, CancellationToken cancellationToken = default);

    /// <summary>The form state of each request, derived from its invite columns.</summary>
    Task<IReadOnlyList<RemsFormStateInfo>> GetFormStatesAsync(IReadOnlyCollection<Guid> remsIds, CancellationToken cancellationToken = default);

    /// <summary>Whether another live request names the same client.</summary>
    Task<bool> IsClientSharedAsync(Guid clientId, Guid excludingRemsId, CancellationToken cancellationToken = default);

    /// <summary>A client's requests, newest first, with type and status.</summary>
    Task<IReadOnlyList<RemsRequest>> ListByClientAsync(Guid clientId, CancellationToken cancellationToken = default);

    // ---- Request lists (My REMS Requests, Partner dashboard, pool) ----

    Task<(IReadOnlyList<RemsRequest> Items, int Total)> ListRequestsAsync(RemsRequestListOptions options, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemsClientChoice>> ListRequestClientsAsync(RemsRequestListOptions options, CancellationToken cancellationToken = default);
    Task<RemsPoolCounts> CountPoolScopesAsync(RemsRequestListOptions options, CancellationToken cancellationToken = default);
    Task<RemsRequestQuickCounts> CountQuickFiltersAsync(RemsRequestListOptions options, CancellationToken cancellationToken = default);

    // ---- EMS Review (client forms) ----

    Task<(IReadOnlyList<RemsClientFormItem> Items, int Total)> ListClientFormsAsync(RemsClientFormQuery query, CancellationToken cancellationToken = default);
    Task<RemsClientFormQuickCounts> CountClientFormQuickFiltersAsync(RemsClientFormQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemsClientChoice>> ListClientFormClientsAsync(CancellationToken cancellationToken = default);

    // ---- Form inbox ----

    Task<(IReadOnlyList<RemsInboxItem> Items, int Total)> ListInboxAsync(RemsInboxQuery query, CancellationToken cancellationToken = default);

    // ---- Related entities ----

    Task<(IReadOnlyList<RemsRelatedEntityItem> Items, int Total)> ListRelatedEntitiesAsync(RemsRelatedEntityQuery query, CancellationToken cancellationToken = default);
    Task<RemsRelatedEntityQuickCounts> CountRelatedEntityQuickFiltersAsync(RemsRelatedEntityQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemsClientChoice>> ListRelatedEntityClientsAsync(CancellationToken cancellationToken = default);

    /// <summary>The related parties declared on each request's client. <see cref="RemsRelatedClientItem.Id"/> is the relationship id.</summary>
    Task<IReadOnlyList<RemsRelatedClientItem>> ListRelatedClientsAsync(IReadOnlyCollection<Guid> remsIds, CancellationToken cancellationToken = default);

    // ---- Mutations ----

    Task AddAsync(RemsRequest request, CancellationToken cancellationToken = default);
    void Update(RemsRequest request);
    void Remove(RemsRequest request);

    Task AddFileAsync(RemsFile file, CancellationToken cancellationToken = default);
    void RemoveFile(RemsFile file);

    Task AddEmailEventAsync(RemsEmailEvent emailEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemsEmailEvent>> ListEmailEventsAsync(Guid remsId, CancellationToken cancellationToken = default);
    Task<RemsEmailEvent?> GetSentEventByProviderMessageIdUnscopedAsync(string providerMessageId, CancellationToken cancellationToken = default);
    Task<bool> EmailEventExistsAsync(Guid tenantId, string providerMessageId, RemsFormEmailEventType eventType, CancellationToken cancellationToken = default);
    /// <summary>Appends a provider-reported event; false when the same message already has one of that type.</summary>
    Task<bool> TryAppendProviderEmailEventAsync(RemsEmailEvent emailEvent, CancellationToken cancellationToken = default);

    Task AddStatusHistoryAsync(RemsStatusHistory history, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemsStatusHistory>> ListStatusHistoryAsync(Guid remsId, CancellationToken cancellationToken = default);
    /// <summary>The latest history row when it is a send-back the request is still on; null otherwise.</summary>
    Task<RemsStatusHistory?> GetOpenSendBackAsync(Guid remsId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemsStatusHistory>> ListSendBacksAsync(Guid remsId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemsApprover>> ListApproversAsync(Guid remsId, CancellationToken cancellationToken = default);
    Task AddApproverAsync(RemsApprover approver, CancellationToken cancellationToken = default);
    void RemoveApprover(RemsApprover approver);

    Task AddCommissionSplitAsync(RemsCommissionSplit split, CancellationToken cancellationToken = default);
    void RemoveCommissionSplit(RemsCommissionSplit split);
    Task<int> CountActiveCommissionSplitsAsync(Guid remsId, CancellationToken cancellationToken = default);

    Task AddPersonnelRateAsync(RemsPersonnelRate rate, CancellationToken cancellationToken = default);
    void RemovePersonnelRate(RemsPersonnelRate rate);

    Task AddMarketingMethodAsync(RemsMarketingMethod method, CancellationToken cancellationToken = default);
    void RemoveMarketingMethod(RemsMarketingMethod method);

    Task AddTaxFormAsync(RemsTaxForm taxForm, CancellationToken cancellationToken = default);
    void RemoveTaxForm(RemsTaxForm taxForm);

    Task AddTaxDueDateAsync(RemsTaxDueDate dueDate, CancellationToken cancellationToken = default);
    void UpdateTaxDueDate(RemsTaxDueDate dueDate);
    void RemoveTaxDueDate(RemsTaxDueDate dueDate);
}

/// <summary>Approved requests by Maconomy job sync state: created, failed (lost attempts included), under way, and still to post.</summary>
public sealed record RemsMaconomyJobSyncCounts(int Synced, int Failed, int Syncing, int Waiting);
