namespace EmsPortal.Api.Models.Rems;

/// <summary>
/// One row of the Related Entities list: a submitted request, and the clients its intake declared
/// ALONGSIDE the client it was raised for.
/// </summary>
public sealed record RemsRelatedEntityRow(
    Guid RemsId,
    string RemsNumber,
    /// <summary>
    /// The client's name as it reads — "Smith John Jr." for a person, the legal name for an
    /// organisation.
    /// </summary>
    string ClientName,
    /// <summary>The generational particle, so the Client column can draw it in bold at the end of the name.</summary>
    string? ClientNameSuffix,
    /// <summary>The client's email, shown under their name — how one "John Smith" row is told from the next.</summary>
    string? ClientEmail,
    /// <summary>What kind of entity the client is (REMS.EntityType code) — which question they were asked.</summary>
    string? EntityType,
    /// <summary>Where the parent request itself stands (REMS.Status code).</summary>
    string RequestStatus,
    /// <summary>
    /// The admin holding the request, or null while nobody has picked it up — what lets the status
    /// badge read "Waiting for pickup" here exactly as it does on every other REMS surface.
    /// </summary>
    RemsUserRef? AssignedAdmin,
    DateTime? SubmittedOnUtc,
    RemsRelatedParentView Parent,
    IReadOnlyList<RemsRelatedClientView> RelatedClients,
    /// <summary>How many rows <see cref="RelatedClients"/> holds — a sortable handle on a nested table.</summary>
    int RelatedCount,
    /// <summary>Whether THIS caller may open the request as a form.</summary>
    bool CanEdit,
    // The owning REQUEST's audit trail — the row is keyed on it, and it is what the actions open.
    string? CreatedBy,
    DateTime CreatedOnUtc,
    string? UpdatedBy,
    DateTime UpdatedOnUtc);

/// <summary>The head of the nested table: the client the request is FOR, plus anyone filed as the same client.</summary>
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

/// <summary>One related client under the parent, whichever card declared them.</summary>
public sealed record RemsRelatedClientView(
    /// <summary>Which table this row is in, and therefore which endpoint sets its status.</summary>
    string Kind,
    Guid Id,
    /// <summary>
    /// The name as it reads — surname first for a person, "Smith Jane", matching the Client column
    /// beside it.
    /// </summary>
    string Name,
    /// <summary>
    /// The generational particle, beside the name so the cell can draw it in bold at the end as every
    /// other REMS surface does.
    /// </summary>
    string? Suffix,
    /// <summary>What they are to the client — <c>spouse</c>, <c>child</c>, <c>other</c>.</summary>
    string? Relation,
    /// <summary>Their email and phone as declared, for the tooltip.</summary>
    string? Email,
    string? PhoneNumber,
    /// <summary>The hand-set progress code (REMS.RelatedEntityStatus); never null — see the repository.</summary>
    string Status,
    /// <summary>How this related client is referred to.</summary>
    string? Reference,
    /// <summary>The request raised from this row, for the link behind <see cref="Reference"/>.</summary>
    Guid? CreatedRemsId);

/// <summary>Set one related client's progress.</summary>
public sealed class SetRemsRelatedStatusRequest
{
    /// <summary>An option-set CODE from <c>REMS.RelatedEntityStatus</c> — the tenant's own copy of the list.</summary>
    public string Status { get; set; } = string.Empty;
}
