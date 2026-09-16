using EmsPortal.Domain.Entities;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// The EMS-Inbox query for the REMS form pipeline (WO-112). Every request that has a
/// <see cref="REMSForm"/> is a candidate row; the optional <see cref="FormState"/> narrows to a single
/// <see cref="RemsFormStatus"/>. Tenant isolation is applied by the ambient query filter.
/// </summary>
public sealed record RemsInboxQuery(
    RemsFormStatus? FormState,
    // Quick search over the REMS number and the requested client name.
    string? Search,
    string? RequestStatus,
    int Page,
    int Limit);

/// <summary>Which slice of EMS Review the caller is looking at (the list's quick-filter buttons).</summary>
public enum RemsClientFormAssignment
{
    /// <summary>Every request in the queue, whoever holds it — including the ones waiting for pickup.</summary>
    All = 0,

    /// <summary>Only the requests this admin has picked up.</summary>
    Mine = 1,
}

/// <summary>
/// The Client-Forms list query (WO-114): quick search over REMS number / client name, plus optional
/// narrowing by whether the client has submitted, by the request's status, and by whether the caller is
/// the admin holding it.
/// </summary>
public sealed record RemsClientFormQuery(
    string? Search,
    bool? Submitted,
    string? RequestStatus,
    // Who is asking. Only read by the Mine slice, which is "assigned to whoever is looking".
    Guid CallerUserId,
    RemsClientFormAssignment Assignment,
    SortRequest Sort,
    int Page,
    int Limit,
    /// <summary>A specific holding admin — distinct from the Mine slice, which is whoever is asking.</summary>
    Guid? AssignedAdminUserId = null,
    /// <summary>The CSE named on the request.</summary>
    Guid? CseUserId = null,
    /// <summary>The clients the request is for — any of them. The Client column's dropdown.</summary>
    IReadOnlyList<Guid>? ClientPersonIds = null,
    DateTime? SubmittedFromUtc = null,
    DateTime? SubmittedToUtc = null,
    DateTime? CreatedFromUtc = null,
    DateTime? CreatedToUtc = null,
    DateTime? UpdatedFromUtc = null,
    DateTime? UpdatedToUtc = null);

/// <summary>
/// How many requests each quick-filter button on EMS Review would list: the All / Assigned-to-me pair,
/// and the Request Status group by REMS.Status code (<c>waiting_for_pickup</c> included). Each group is
/// counted under every filter but its own, so a button's number is the rows clicking it produces.
/// </summary>
public sealed record RemsClientFormQuickCounts(
    IReadOnlyDictionary<string, int> Assignment,
    IReadOnlyDictionary<string, int> RequestStatus);

/// <summary>
/// One EMS-Inbox row (WO-112): a request that has a form, projected with the request context, the form
/// state, its creator, and the latest email-delivery event (send/delivered/opened/failed). User ids are
/// resolved to display names by the controller.
/// </summary>
public sealed record RemsInboxItem(
    Guid RemsId,
    string RemsNumber,
    string ClientName,
    string EngagementType,
    string RequestStatus,
    RemsFormStatus FormStatus,
    DateTime FormModifiedOnUtc,
    Guid FormCreatedByUserId,
    DateTime? FormSentOnUtc,
    RemsFormEmailEventType? LatestEmailEventType,
    DateTime? LatestEmailEventOnUtc,
    // Who picked the request up: engagement setup belongs to them, so the inbox can say so rather than
    // offering an action that 403s.
    Guid? AdminAssignedToId,
    // The owning REQUEST's audit trail — the row is keyed on it. Distinct from the form's own creator,
    // which this list already surfaces as "Form Creator".
    Guid? CreatedById,
    DateTime CreatedOnUtc,
    Guid? UpdatedById,
    DateTime UpdatedOnUtc);

/// <summary>
/// One client-forms row (WO-114, AC-REMS-013.1): a request that has an EMS form, with its
/// submitted/not-submitted state, submission date, and the request's assigned Admin/CSE.
/// </summary>
public sealed record RemsClientFormItem(
    Guid RemsId,
    string RemsNumber,
    /// <summary>
    /// The client's name as it reads — "Smith John Jr." for a person, the legal name for an organisation.
    /// Composed by the database on <c>Persons.ClientDisplayName</c>, so every list says it the same way.
    /// </summary>
    string ClientName,
    /// <summary>The generational particle, so the Client column can draw it in bold at the end of the name.</summary>
    string? ClientNameSuffix,
    string RequestStatus,
    bool Submitted,
    DateTime? SubmittedOnUtc,
    Guid? AdminAssignedToId,
    Guid? CSEId,
    Guid? CreatedById,
    DateTime CreatedOnUtc,
    Guid? UpdatedById,
    DateTime UpdatedOnUtc);

/// <summary>
/// Data access for the REMS customer-facing form and its drafts, submissions and email events
/// (WO-110). Submissions and email events are append-only.
/// </summary>
public interface IRemsFormRepository
{
    /// <summary>
    /// The paginated client-forms list (WO-114): every request that has a form, with its submitted state and
    /// the request's assigned Admin/CSE. Tenant-scoped by the ambient query filter.
    /// </summary>
    Task<(IReadOnlyList<RemsClientFormItem> Items, int Total)> ListClientFormsAsync(
        RemsClientFormQuery query, CancellationToken cancellationToken = default);

    /// <summary>The EMS Review quick-filter counts — see <see cref="RemsClientFormQuickCounts"/>.</summary>
    Task<RemsClientFormQuickCounts> CountClientFormQuickFiltersAsync(
        RemsClientFormQuery query, CancellationToken cancellationToken = default);

    /// <summary>The clients across the EMS Review queue, for its Client filter — whoever it could narrow to.</summary>
    Task<IReadOnlyList<RemsClientChoice>> ListClientFormClientsAsync(CancellationToken cancellationToken = default);

    /// <summary>The form with its drafts, submissions and email events loaded.</summary>
    Task<REMSForm?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The active form (with its email events) for a request, or null when none has been built (WO-112).</summary>
    Task<REMSForm?> GetByRemsIdAsync(Guid remsId, CancellationToken cancellationToken = default);

    /// <summary>The active form for a request WITH its submissions loaded (WO-114 submitted-form view).</summary>
    Task<REMSForm?> GetWithSubmissionsByRemsIdAsync(Guid remsId, CancellationToken cancellationToken = default);

    /// <summary>The active form for a tenant's invite code (public link resolution).</summary>
    Task<REMSForm?> GetByInviteCodeAsync(Guid tenantId, string inviteCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a form by invite code alone, WITHOUT the tenant / soft-delete query filters (WO-113 public
    /// client form). The public endpoints run with no resolved tenant, so the code — a 128-bit random value,
    /// unique per tenant and unguessable — is the sole lookup key; the form's own <see cref="REMSForm.TenantId"/>
    /// then becomes the authoritative tenant. The owning <see cref="REMS"/> request (including a soft-deleted
    /// one, so the caller can surface an "unavailable" state) and the single in-progress draft are loaded and
    /// TRACKED so the submit transaction can lock the form and flip the request status. Null when unmatched.
    /// </summary>
    Task<REMSForm?> GetByInviteCodeUnscopedAsync(string inviteCode, CancellationToken cancellationToken = default);

    /// <summary>Whether an invite code is already taken (active) for the tenant.</summary>
    Task<bool> InviteCodeExistsAsync(Guid tenantId, string inviteCode, CancellationToken cancellationToken = default);

    Task AddAsync(REMSForm form, CancellationToken cancellationToken = default);

    void Update(REMSForm form);

    void Remove(REMSForm form);

    Task AddDraftAsync(REMSFormDraft draft, CancellationToken cancellationToken = default);

    void UpdateDraft(REMSFormDraft draft);

    Task AddSubmissionAsync(REMSFormSubmission submission, CancellationToken cancellationToken = default);

    Task AddEmailEventAsync(REMSFormEmailEvent emailEvent, CancellationToken cancellationToken = default);

    /// <summary>Email-delivery events for a form, newest first (WO-112 email log, AC-REMS-008.6).</summary>
    Task<IReadOnlyList<REMSFormEmailEvent>> ListEmailEventsAsync(Guid remsFormId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The anchoring <c>Sent</c> event that carries a provider message id, resolved WITHOUT the tenant /
    /// soft-delete query filters — the WO-121 delivery-event webhook runs with no tenant context. Its
    /// <see cref="REMSFormEmailEvent.TenantId"/> + <see cref="REMSFormEmailEvent.REMSFormId"/> anchor any
    /// provider-reported delivery/open/failed event for the same message. Null when the id is unmatched.
    /// </summary>
    Task<REMSFormEmailEvent?> GetSentEventByProviderMessageIdUnscopedAsync(
        string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether an email event already exists for (<paramref name="tenantId"/>,
    /// <paramref name="providerMessageId"/>, <paramref name="eventType"/>) — the filtered unique-index key
    /// that makes webhook ingestion idempotent (WO-121). Evaluated without the tenant query filter.
    /// </summary>
    Task<bool> EmailEventExistsAsync(
        Guid tenantId, string providerMessageId, RemsFormEmailEventType eventType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a provider-reported email event and commits it immediately (WO-121), returning <c>false</c>
    /// when the filtered unique index <c>(TenantId, ProviderMessageId, EventType)</c> rejects it as a
    /// concurrent duplicate. The row is inserted from an unauthenticated context, so its
    /// <see cref="REMSFormEmailEvent.TenantId"/> must already be set by the caller (from the anchor).
    /// </summary>
    Task<bool> TryAppendProviderEmailEventAsync(
        REMSFormEmailEvent emailEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// The paginated EMS Inbox: every request that has a form, newest-modified first, with the latest
    /// email-event state (WO-112, AC-REMS-009). Tenant-scoped by the ambient query filter.
    /// </summary>
    Task<(IReadOnlyList<RemsInboxItem> Items, int Total)> ListInboxAsync(
        RemsInboxQuery query, CancellationToken cancellationToken = default);
}
