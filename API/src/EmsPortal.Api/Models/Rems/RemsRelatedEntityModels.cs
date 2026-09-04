namespace EmsPortal.Api.Models.Rems;

/// <summary>
/// One row of the Related Entities list: a submitted request, and the clients its intake declared
/// ALONGSIDE the client it was raised for.
/// <para>
/// The subject of the row is <see cref="RelatedClients"/> — everything above it is there to identify the
/// parent they hang off. Which two questions produced them depends on what kind of entity the client is:
/// an Individual is asked "Spouse &amp; More Individuals", and every other entity type is asked "Other
/// Entities". A request can carry both, because the second question used to be asked of individuals too.
/// </para>
/// </summary>
public sealed record RemsRelatedEntityRow(
    Guid RemsId,
    string RemsNumber,
    /// <summary>
    /// The client's name as it reads — "Smith John Jr." for a person, the legal name for an organisation.
    /// Surname first: a client list is scanned and sorted by family name.
    /// </summary>
    string ClientName,
    /// <summary>The generational particle, so the Client column can draw it in bold at the end of the name.</summary>
    string? ClientNameSuffix,
    /// <summary>The client's email, shown under their name — how one "John Smith" row is told from the next.</summary>
    string? ClientEmail,
    /// <summary>What kind of entity the client is (REMS.IndustryGroup code) — which question they were asked.</summary>
    string? EntityType,
    /// <summary>Where the parent request itself stands (REMS.Status code). Context; off by default.</summary>
    string RequestStatus,
    /// <summary>
    /// The admin holding the request, or null while nobody has picked it up — what lets the status badge
    /// read "Waiting for pickup" here exactly as it does on every other REMS surface.
    /// </summary>
    RemsUserRef? AssignedAdmin,
    DateTime? SubmittedOnUtc,
    RemsRelatedParentView Parent,
    IReadOnlyList<RemsRelatedClientView> RelatedClients,
    /// <summary>How many rows <see cref="RelatedClients"/> holds — a sortable handle on a nested table.</summary>
    int RelatedCount,
    /// <summary>
    /// Whether THIS caller may open the request as a form. The same pair
    /// <c>RemsRequestsController.ActionsFor</c> applies — the record-level rule (a REMS admin, or the
    /// request is theirs) AND <c>rems.requests.update</c> — asked ahead of the click so the row offers the
    /// action exactly where the save would be accepted.
    /// <para>
    /// Answered here rather than worked out in the browser because this list is open to EVERY signed-in
    /// user, so most callers may edit none of what they can see, and a client-side guess at that rule is
    /// a second copy of it waiting to drift.
    /// </para>
    /// </summary>
    bool CanEdit,
    // The owning REQUEST's audit trail — the row is keyed on it, and it is what the actions open.
    //
    // Setting a related client's status restamps it, which is deliberate: the activity entry that records
    // the change is written on the request, and ActivityEventWriter touches the aggregate root it is
    // written on (see AggregateRootTouch — REMS lists show parents whose children are edited elsewhere).
    // So this list's default sort, newest touch first, floats the client groups somebody has just worked.
    string? CreatedBy,
    DateTime CreatedOnUtc,
    string? UpdatedBy,
    DateTime UpdatedOnUtc);

/// <summary>
/// The head of the nested table: the client the request is FOR, plus anyone filed as the same client.
/// <para>
/// <see cref="JointWith"/> is the whole reason this is a record rather than a string. A spouse on a JOINT
/// return is not a related client — one return, one client, one invoice — so giving them a row of their
/// own with a status of their own would be inviting somebody to raise a second request for a person who
/// is already on this one. They belong in the header, named, with the reason beside them.
/// </para>
/// </summary>
public sealed record RemsRelatedParentView(
    string Name,
    string? Suffix,
    RemsRelatedJointFilerView? JointWith);

/// <summary>Somebody filed on the client's own return — read as part of the parent, never under it.</summary>
public sealed record RemsRelatedJointFilerView(
    string Name,
    /// <summary>Their generational particle, drawn apart from the name as it is everywhere else.</summary>
    string? Suffix,
    /// <summary>What they are to the client — <c>spouse</c>, <c>child</c>, <c>other</c>.</summary>
    string? Relation);

/// <summary>
/// One related client under the parent, whichever card declared them.
/// </summary>
public sealed record RemsRelatedClientView(
    /// <summary>Which table this row is in, and therefore which endpoint sets its status.</summary>
    string Kind,
    Guid Id,
    /// <summary>
    /// The name as it reads — surname first for a person, "Smith Jane", matching the Client column beside
    /// it. A business is its declared name.
    /// </summary>
    string Name,
    /// <summary>
    /// The generational particle, beside the name so the cell can draw it in bold at the end as every
    /// other REMS surface does. Null for a business, and for a person declared before the box existed.
    /// </summary>
    string? Suffix,
    /// <summary>What they are to the client — <c>spouse</c>, <c>child</c>, <c>other</c>. Null for a business.</summary>
    string? Relation,
    /// <summary>Their email and phone as declared, for the tooltip. Nothing else on the row shows them.</summary>
    string? Email,
    string? PhoneNumber,
    /// <summary>The hand-set progress code (REMS.RelatedEntityStatus); never null — see the repository.</summary>
    string Status,
    /// <summary>
    /// How this related client is referred to. Either the REAL number of the request raised from the row
    /// (businesses only — a person on a return never gets one), or, failing that, a reference derived from
    /// the parent and this row's position under it: <c>REMS-1042-C1</c>.
    /// <para>
    /// Null while the row is still Not Initiated and has produced nothing. There is no request to point at
    /// yet, and printing a reference anyway invites somebody to go looking for one.
    /// </para>
    /// </summary>
    string? Reference,
    /// <summary>The request raised from this row, for the link behind <see cref="Reference"/>. Businesses only.</summary>
    Guid? CreatedRemsId);

/// <summary>
/// Set one related client's progress. The status is the firm's own note and moves only by hand, so this
/// is the only thing about these rows that any screen writes.
/// </summary>
public sealed class SetRemsRelatedStatusRequest
{
    /// <summary>An option-set CODE from <c>REMS.RelatedEntityStatus</c> — the tenant's own copy of the list.</summary>
    public string Status { get; set; } = string.Empty;
}
