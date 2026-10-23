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
    DateTime UpdatedOnUtc,
    /// <summary>Set when the client is a Maconomy customer, whose name is read from Maconomy rather than the database.</summary>
    string? MaconomyCustomerNumber = null);

