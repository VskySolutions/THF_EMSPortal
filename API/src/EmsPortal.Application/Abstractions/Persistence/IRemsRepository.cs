using EmsPortal.Domain.Entities;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>The dashboard "view" a request list is filtered to (WO-111).</summary>
public enum RemsListScope
{
    /// <summary>No view filter — every request the caller is allowed to see (record-level predicate only).</summary>
    All = 0,

    /// <summary>The partner "my requests" view: created or involved by the caller (AC-REMS-002.1).</summary>
    Partner = 1,

    /// <summary>The Admin Pool view: submitted (non-draft) requests.</summary>
    Pool = 2,
}

/// <summary>Additional Admin-Pool narrowing applied when <see cref="RemsListScope.Pool"/> is requested.</summary>
public enum RemsPoolFilter
{
    /// <summary>All pool requests, regardless of assignment.</summary>
    All = 0,

    /// <summary>Only pool requests with no admin assigned.</summary>
    Unassigned = 1,

    /// <summary>Only pool requests assigned to the caller.</summary>
    Mine = 2,
}

/// <summary>
/// Whose requests the "My Requests" list shows — the All / Created By Me toggle beside that list's
/// column picker.
/// <para>
/// It is a VIEW, not a permission: <see cref="All"/> narrows nothing on top of the record-level
/// visibility predicate, so what it reaches is still bounded by what the caller may see at all. Only a
/// REMS Admin sees the toggle, because only for them do the two answers differ by other people's work;
/// for everyone else "All" is the requests they created or are named on, which is all there is.
/// </para>
/// </summary>
public enum RemsListOwnership
{
    /// <summary>
    /// Authorship: raised BY the caller, or FOR them by a delegate acting in their seat. NOT the requests
    /// that merely name them as CSE or reviewing admin — those are somebody else's referral.
    /// </summary>
    Mine = 0,

    /// <summary>Everything the caller may see — for a REMS Admin the whole tenant, other people's drafts included.</summary>
    All = 1,
}

/// <summary>
/// The resolved query for a REMS request dashboard list (WO-111). Carries the caller identity and the
/// caller's privilege (Admin role or Super Admin) so the repository can apply the record-level
/// visibility predicate, plus the server-side filters and the view scope.
/// </summary>
public sealed record RemsRequestListOptions(
    Guid CallerUserId,
    bool CallerIsPrivileged,
    string? ClientName,
    string? Contact,
    string? Status,
    // Option-set CODE (REMS.Type), matched exactly — the label is the tenant's to
    // rename, the code is what the row stores.
    string? Type,
    /// <summary>A specific owning admin. Distinct from <see cref="PoolFilter"/>, which asks
    /// unassigned/mine/any rather than naming somebody.</summary>
    Guid? AssignedAdminUserId,
    DateTime? CreatedFromUtc,
    DateTime? CreatedToUtc,
    RemsListScope Scope,
    RemsPoolFilter PoolFilter,
    /// <summary>Whose requests the Partner "My Requests" view shows. The other scopes ignore it.
    /// Callers with no toggle on screen should send <see cref="RemsListOwnership.All"/>, which is the
    /// list they had before there was one.</summary>
    RemsListOwnership Ownership,
    SortRequest Sort,
    int Page,
    int Limit);

/// <summary>
/// How many pool requests fall into each Admin Pool view, under the caller's visibility and the filters
/// currently applied. <see cref="All"/> is the total the other two are drawn from, not their sum: a
/// request assigned to someone else is in neither.
/// </summary>
public sealed record RemsPoolCounts(int Unassigned, int Mine, int All);

/// <summary>
/// The EMS-form and client-submission state for a request, projected from the (at most one active)
/// <see cref="REMSForm"/> and its submissions. Absent when the form has not been started (WO-111 left-join;
/// forms/submissions are populated by later WOs).
/// </summary>
public sealed record RemsFormStateInfo(
    Guid RemsId,
    string? IndustryGroup,
    RemsFormStatus? FormStatus,
    DateTime? FormSentOnUtc,
    DateTime? FormSubmittedOnUtc,
    bool HasSubmission,
    /// <summary>
    /// The code behind the client's public form link. Carried here so a caller holding the request can
    /// build that link without a second read of the form — the request detail offers it for copying while
    /// the form is out with the client. It is not a secret to anyone who can already read the request:
    /// they can open the Email Log, which shows the same link.
    /// </summary>
    string? InviteCode);

/// <summary>Which table a related client came out of — the two are read together and written apart.</summary>
public enum RemsRelatedClientKind
{
    /// <summary>
    /// Another person on an individual client's return, from the intake form's "Spouse &amp; More
    /// Individuals" card (<see cref="REMSAdditionalIndividual"/>).
    /// </summary>
    Individual = 0,

    /// <summary>
    /// Another business the client named on the intake form's "Other Entities" card
    /// (<see cref="REMSAdditionalEntity"/>). Asked of every entity type except Individual.
    /// </summary>
    Entity = 1,
}

/// <summary>
/// The Related Entities list query: every submitted request whose client declared somebody ALONGSIDE
/// themselves, one row per request with its related clients hanging off it.
/// <para>
/// The two sources are the two cards the intake form asks that on — "Spouse &amp; More Individuals" for
/// an individual client, "Other Entities" for every other entity type. A request qualifies by having at
/// least one row in either; because both are written only on submit, that is also what makes this a list
/// of submitted requests without asking about the form at all.
/// </para>
/// </summary>
public sealed record RemsRelatedEntityQuery(
    /// <summary>Quick search over the REMS number, the client's name, and the related clients' names.</summary>
    string? Search,
    /// <summary>Option-set CODE (REMS.IndustryGroup), matched exactly — what kind of entity the client is.</summary>
    string? EntityType,
    /// <summary>
    /// Option-set CODE (REMS.RelatedEntityStatus). Narrows to requests with AT LEAST ONE related client at
    /// that status, which is what the column shows — a request is not at a single status, its rows are.
    /// Filtering on <c>not_initiated</c> also matches rows nobody has answered for, since a null status
    /// reads as exactly that.
    /// </summary>
    string? RelatedStatus,
    SortRequest Sort,
    int Page,
    int Limit);

/// <summary>
/// One Related Entities row: the PARENT request, plus the count of related clients declared on it. The
/// related clients themselves come back separately (<see cref="IRemsRepository.ListRelatedClientsAsync"/>),
/// one read for the whole page rather than one per row.
/// </summary>
public sealed record RemsRelatedEntityItem(
    Guid RemsId,
    string RemsNumber,
    /// <summary>
    /// The client's name as it reads — "Smith John Jr." for a person, the legal name for an organisation.
    /// Composed by the database on <c>Persons.ClientDisplayName</c>, so every list says it the same way.
    /// </summary>
    string ClientName,
    /// <summary>The generational particle, so the Client column can draw it in bold at the end of the name.</summary>
    string? ClientNameSuffix,
    string? ClientEmail,
    /// <summary>What kind of entity the client is (REMS.IndustryGroup code), off the request's form.</summary>
    string? EntityType,
    /// <summary>Where the request itself has got to (REMS.Status code) — context, not this list's subject.</summary>
    string RequestStatus,
    /// <summary>
    /// The admin holding the request, or null while nobody has picked it up. Carried only so the request's
    /// status badge can say "Waiting for pickup" where that is what it means — the refinement every other
    /// REMS surface applies, and a list that skipped it would say something different about the same row.
    /// </summary>
    Guid? AdminAssignedToId,
    /// <summary>When the client sent their intake form back. Never null in practice: no submission, no rows.</summary>
    DateTime? SubmittedOnUtc,
    int RelatedCount,
    Guid? CreatedById,
    /// <summary>
    /// The principal a delegate raised this FOR, when one did. Carried for the same reason
    /// <see cref="CreatedById"/> is: together they are "whose request this is", which is half of whether
    /// the caller may edit it (RemsRequestsController.IsMine).
    /// </summary>
    Guid? OnBehalfOfUserId,
    DateTime CreatedOnUtc,
    Guid? UpdatedById,
    DateTime UpdatedOnUtc);

/// <summary>
/// One related client, from either source table, in the shape the list draws them in.
/// <para>
/// The fields that only one kind carries are null on the other, and deliberately so rather than being
/// split into two records: the list shows both kinds in one nested table, and a reader is looking at
/// "who else is on this client" rather than at which table a row came from. What an individual has that
/// an entity does not is a RELATION (spouse / child / other) and a FILING TYPE; what an entity has that
/// an individual does not is the follow-up REQUEST it produced — an entity gets its own REMS, a person
/// on a return does not.
/// </para>
/// </summary>
public sealed record RemsRelatedClientItem(
    Guid Id,
    Guid RemsId,
    RemsRelatedClientKind Kind,
    /// <summary>
    /// The name as it READS on the list: surname first for a person — "Smith Jane" — and the plain
    /// declared name for a business. The same order the Client column beside it uses, because these are
    /// clients too and one list should not name two of them two ways.
    /// </summary>
    string Name,
    /// <summary>
    /// The generational particle, beside the name rather than inside it, so the list can draw it apart
    /// from the name the way every other REMS surface does. Null for a business, and for a person
    /// declared before the intake asked for one.
    /// </summary>
    string? Suffix,
    /// <summary>What they are to the client — <c>spouse</c>, <c>child</c>, <c>other</c>. Individuals only.</summary>
    string? Relation,
    /// <summary>How their return is filed — <c>joint</c> or <c>individual</c>. Individuals only.</summary>
    string? FilingType,
    string? Email,
    string? PhoneNumber,
    /// <summary>
    /// The hand-set progress code (REMS.RelatedEntityStatus), already resolved: a row nobody has answered
    /// for comes back as <c>not_initiated</c> rather than as null, because that is what it means.
    /// </summary>
    string Status,
    /// <summary>The follow-up request raised from this row, or null. Entities only.</summary>
    Guid? CreatedRemsId,
    /// <summary>Stable ordering within its parent — the client declared them in this order.</summary>
    DateTime DeclaredOnUtc,
    string SourceKey);

/// <summary>
/// Data access for the REMS request aggregate root and its file links (WO-110). Tenant isolation is
/// applied by the DbContext global query filter; the number-generation helpers deliberately bypass it
/// to see every row for the tenant.
/// </summary>
public interface IRemsRepository
{
    /// <summary>The request with its file links (and media) loaded; tenant-scoped by the ambient filter.</summary>
    Task<REMS?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Active requests for the current tenant.</summary>
    Task<IReadOnlyList<REMS>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated dashboard list. Applies the WO-111 record-level visibility predicate (drafts are
    /// creator-only; privileged callers see all non-draft; partner-only callers see created-or-involved)
    /// plus the requested filters and view scope, tenant-scoped by the ambient filter.
    /// </summary>
    Task<(IReadOnlyList<REMS> Items, int Total)> ListRequestsAsync(
        RemsRequestListOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts for the Admin Pool's Unassigned / Assigned-to-me / All views in one round trip, under the
    /// same visibility predicate and field filters as <see cref="ListRequestsAsync"/>. The options'
    /// <see cref="RemsRequestListOptions.PoolFilter"/> is ignored — it is what is being counted.
    /// </summary>
    Task<RemsPoolCounts> CountPoolScopesAsync(
        RemsRequestListOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// EMS-form / client-submission state for the given requests (one active form per request), for the
    /// dashboard rows. Requests with no form are simply absent from the result.
    /// </summary>
    Task<IReadOnlyList<RemsFormStateInfo>> GetFormStatesAsync(
        IReadOnlyCollection<Guid> remsIds, CancellationToken cancellationToken = default);

    Task AddAsync(REMS rems, CancellationToken cancellationToken = default);

    void Update(REMS rems);

    void Remove(REMS rems);

    Task AddFileAsync(REMSFiles file, CancellationToken cancellationToken = default);

    void RemoveFile(REMSFiles file);

    /// <summary>Stage another business the client named at intake (WO-116, initiator-first rebuild).</summary>
    Task AddAdditionalEntityAsync(REMSAdditionalEntity additionalEntity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage another PERSON on an individual client's return — a spouse, a child, anyone else the firm is
    /// preparing for. Not an entity and not a contact: see <see cref="REMSAdditionalIndividual"/>.
    /// </summary>
    Task AddAdditionalIndividualAsync(
        REMSAdditionalIndividual additionalIndividual, CancellationToken cancellationToken = default);

    /// <summary>Everyone else on a request's return, in the order the client declared them.</summary>
    Task<IReadOnlyList<REMSAdditionalIndividual>> ListAdditionalIndividualsAsync(
        Guid remsId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The other businesses declared on a request's intake, newest last. Backs the follow-up flag on the
    /// Partner/CSE list — rows with no <c>CreatedREMSId</c> are the ones still needing an EMS.
    /// </summary>
    Task<IReadOnlyList<REMSAdditionalEntity>> ListAdditionalEntitiesAsync(Guid remsId, CancellationToken cancellationToken = default);

    Task<REMSAdditionalEntity?> GetAdditionalEntityAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>One declared person, TRACKED — for setting the hand-managed status on the Related Entities list.</summary>
    Task<REMSAdditionalIndividual?> GetAdditionalIndividualAsync(Guid id, CancellationToken cancellationToken = default);

    void UpdateAdditionalIndividual(REMSAdditionalIndividual additionalIndividual);

    /// <summary>
    /// The paginated Related Entities list: every request whose client declared other people or other
    /// businesses at intake, one row per REQUEST. Tenant-scoped by the ambient query filter.
    /// </summary>
    Task<(IReadOnlyList<RemsRelatedEntityItem> Items, int Total)> ListRelatedEntitiesAsync(
        RemsRelatedEntityQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every related client declared on the given requests, both kinds together, ordered as the client
    /// declared them. One read for a whole page of parents rather than one per row.
    /// </summary>
    Task<IReadOnlyList<RemsRelatedClientItem>> ListRelatedClientsAsync(
        IReadOnlyCollection<Guid> remsIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// REMS numbers for the given ids, keyed by id. Lets an additional-entity row link to the request it
    /// produced by NAME rather than only claiming one exists.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNumbersAsync(
        IReadOnlyCollection<Guid> remsIds, CancellationToken cancellationToken = default);

    void UpdateAdditionalEntity(REMSAdditionalEntity additionalEntity);

    /// <summary>Record an admin's return of a request to its initiator, with the reason they gave.</summary>
    Task AddSendBackAsync(REMSSendBack sendBack, CancellationToken cancellationToken = default);

    /// <summary>The still-open return for a request, or null when it is not currently back with its initiator.</summary>
    Task<REMSSendBack?> GetOpenSendBackAsync(Guid remsId, CancellationToken cancellationToken = default);

    /// <summary>Every return of a request, oldest first — the send-back half of its history.</summary>
    Task<IReadOnlyList<REMSSendBack>> ListSendBacksAsync(Guid remsId, CancellationToken cancellationToken = default);

    void UpdateSendBack(REMSSendBack sendBack);

    /// <summary>
    /// Count of active REMS requests for a tenant, ignoring the ambient query filter. Backs
    /// <c>REMS-{seq}</c> number generation (seq = count + 1).
    /// </summary>
    Task<int> CountActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a REMS number is already taken (active) for the tenant. Advisory only — the filtered
    /// unique index <c>(TenantId, REMSNumber) WHERE [Deleted] = 0</c> is the definitive guard.
    /// </summary>
    Task<bool> NumberExistsAsync(Guid tenantId, string number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether any request other than <paramref name="excludingRemsId"/> names this person as its client —
    /// either as the client person or as the existing-client it matched at intake. A person only one
    /// request has ever referred to is that request's to keep in step with; once a second request points
    /// at them they are a shared client record, and editing one request must not rename them underneath
    /// the others.
    /// </summary>
    Task<bool> IsClientPersonSharedAsync(Guid personId, Guid excludingRemsId, CancellationToken cancellationToken = default);

}
