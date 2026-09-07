namespace EmsPortal.Api.Models.Rems;

/// <summary>Create payload for a REMS request (WO-111).</summary>
public sealed class CreateRemsRequestRequest
{
    /// <summary>
    /// Loose reference to an existing client (Person id) when the referral is for a client THF already
    /// has.
    /// </summary>
    public Guid? ExistingClientReferenceId { get; set; }

    /// <summary>The client's name as one string.</summary>
    public string ClientName { get; set; } = string.Empty;

    /// <summary>The name in PARTS, for an individual client.</summary>
    public string? ClientFirstName { get; set; }

    /// <inheritdoc cref="ClientFirstName"/>
    public string? ClientLastName { get; set; }

    /// <summary>The legal name, for an ORGANISATION client — every entity type except Individual.</summary>
    public string? ClientCorporateName { get; set; }

    /// <summary>
    /// The generational suffix on that name — Jr., Sr., II, III, IV — kept out of <see
    /// cref="ClientName"/> so the two can be told apart afterwards.
    /// </summary>
    public string? ClientNameSuffix { get; set; }

    /// <summary>Request type (option-set <c>REMS.Type</c> code, e.g. <c>brand_new_client</c>).</summary>
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string? CustomerEmail { get; set; }
    public string? CustomerMobileNumber { get; set; }

    /// <summary>Optional Client Service Executive (User id).</summary>
    public Guid? CSEId { get; set; }

    /// <summary>Optional single attachment: a previously-uploaded media id (POST /api/media).</summary>
    public Guid? MediaId { get; set; }

    // No Submit flag and no admin to name.

    /// <summary>
    /// The <c>REMSAdditionalEntity</c> row this request was raised from, when the initiator used the
    /// Create EMS action on another business the client named.
    /// </summary>
    public Guid? FromAdditionalEntityId { get; set; }
}

/// <summary>Edit payload for a REMS request (WO-111).</summary>
public sealed class UpdateRemsRequestRequest
{
    public string? Description { get; set; }
    public string? Type { get; set; }
    public string? ClientName { get; set; }

    /// <inheritdoc cref="CreateRemsRequestRequest.ClientFirstName"/>
    public string? ClientFirstName { get; set; }

    /// <inheritdoc cref="CreateRemsRequestRequest.ClientFirstName"/>
    public string? ClientLastName { get; set; }

    /// <inheritdoc cref="CreateRemsRequestRequest.ClientCorporateName"/>
    public string? ClientCorporateName { get; set; }

    /// <summary>The client's generational suffix.</summary>
    public string? ClientNameSuffix { get; set; }

    public string? CustomerEmail { get; set; }
    public string? CustomerMobileNumber { get; set; }
    public Guid? CSEId { get; set; }
    public Guid? ExistingClientReferenceId { get; set; }

    // Saving a request cannot re-point who reviews it: an admin gains a request by picking it up and loses
    // it by handing it back, both actions of their own rather than a field somebody else writes on an edit.
}

/// <summary>Attach already-uploaded media to an existing request (POST /api/media first).</summary>
public sealed class AddRemsFilesRequest
{
    public IReadOnlyList<Guid> MediaIds { get; set; } = Array.Empty<Guid>();
}

/// <summary>The Admin's reason for returning a request for rework, and who they are handing it to.</summary>
public sealed class SendBackRemsRequestRequest
{
    /// <summary>Why the setup needs work.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Who owns the rework: <see cref="RemsSendBackTargets.Initiator"/> or <see
    /// cref="RemsSendBackTargets.Cse"/>.
    /// </summary>
    public string? ReturnTo { get; set; }
}

/// <summary>The two people an admin may hand rework to.</summary>
public static class RemsSendBackTargets
{
    public const string Initiator = "initiator";
    public const string Cse = "cse";
}

/// <summary>One return of a request for rework, as history.</summary>
public sealed record RemsSendBackView(
    Guid Id,
    string Reason,
    string? ReturnedBy,
    DateTime ReturnedOnUtc,
    /// <summary>When the initiator handed the revised setup back; null while it is still with them.</summary>
    DateTime? ResolvedOnUtc,
    /// <summary>Who the admin addressed it to, or null on returns made before they were asked to choose.</summary>
    string? ReturnedTo);

/// <summary>A user reference (id + display name) for the assigned admin / CSE columns.</summary>
public sealed record RemsUserRef(Guid Id, string Name);

/// <summary>Which row actions the caller may perform on a request (drives the dashboard action menu).</summary>
public sealed record RemsRowActions(
    bool CanView,
    bool CanEdit,
    /// <summary>The caller may claim this request as its reviewing admin.</summary>
    bool CanPickUp,
    bool CanDelete);

/// <summary>Dashboard list row for a REMS request (WO-111).</summary>
public sealed record RemsRequestRow(
    Guid Id,
    string RemsNumber,
    /// <summary>
    /// The client's name as it reads — "Smith John Jr." for a person, the legal name for an
    /// organisation.
    /// </summary>
    string ClientName,
    /// <summary>The generational particle, so the Client column can draw it in bold at the end of the name.</summary>
    string? ClientNameSuffix,
    string Type,
    DateTime CreatedOnUtc,
    string Status,
    // The Admin Pool renders these under the client name and lets you filter on them (its `contact` filter
    // searches exactly these two columns server-side).
    string? CustomerEmail,
    string? CustomerMobileNumber,
    RemsUserRef? AssignedAdmin,
    RemsUserRef? Cse,
    string? EntityType,
    string EmsFormState,
    string? ClientSubmissionState,
    // Audit trail, offered as hidden-by-default columns on every list (mirrors RemsRequestDetail).
    string? CreatedBy,
    string? UpdatedBy,
    DateTime UpdatedOnUtc,
    RemsRowActions Actions);

/// <summary>An attached file on a request detail (linked media).</summary>
public sealed record RemsFileRef(
    Guid Id,
    Guid MediaId,
    string? FileName,
    string? MimeType,
    long? FileSize,
    string? Url);

/// <summary>Full REMS request detail (WO-111).</summary>
public sealed record RemsRequestDetail(
    Guid Id,
    string RemsNumber,
    string? Description,
    /// <summary>The client's name as it reads — the suffix after the requested name.</summary>
    string ClientName,
    /// <summary>The generational particle, so a cell can draw it in bold at the end of the name.</summary>
    string? ClientNameSuffix,
    /// <summary>
    /// The name in PARTS, which is how the form asks for it: two boxes for an individual, one for an
    /// organisation.
    /// </summary>
    string? ClientFirstName,
    string? ClientLastName,
    string? ClientCorporateName,
    string Type,
    string Status,
    string? CustomerEmail,
    string? CustomerMobileNumber,
    Guid? ExistingClientReferenceId,
    // The Person master record this request's client is.
    Guid? ClientPersonId,
    RemsUserRef? AssignedAdmin,
    RemsUserRef? Cse,
    string? EntityType,
    string EmsFormState,
    string? ClientSubmissionState,
    IReadOnlyList<RemsFileRef> Files,
    RecordAudit Audit,
    RemsRowActions Actions,
    /// <summary>Whether the send-back dialog may offer the CSE as the person to hand the rework to.</summary>
    bool CanSendBackToCse,
    /// <summary>
    /// The client's own intake link, for copying — non-null only while it is theirs to follow: the
    /// form has been sent and they have not answered yet.
    /// </summary>
    string? ClientFormLink);

/// <summary>A client-lookup result (WO-111).</summary>
public sealed record RemsClientLookupItem(
    Guid Id,
    /// <summary>The name as it reads — "Smith John Jr." for a person, the legal name for an organisation.</summary>
    string Name,
    string? Email,
    string? Phone,
    string? Suffix,
    /// <summary>
    /// The name in PARTS, so picking a result can fill the three boxes a person's name is asked in
    /// rather than making the browser split a joined string and guess where the split was.
    /// </summary>
    string FirstName,
    string LastName,
    /// <summary>The legal name, for an organisation.</summary>
    string? CorporateName,
    /// <summary>Which of the two this is.</summary>
    bool IsOrganisation);

/// <summary>An option in the assign-to-admin dropdown (WO-111).</summary>
public sealed record RemsAdminOption(Guid Id, string Name, string? Email);

/// <summary>
/// The client's own details as a request submits them — the four things that used to be columns on
/// <c>REMS</c> and now live on the client's <c>Person</c>.
/// </summary>
public sealed record ClientDetails(
    string? Name,
    string? Suffix,
    string? Email,
    string? Phone,
    /// <summary>The name in PARTS, for an individual client.</summary>
    string? FirstName = null,
    string? LastName = null,
    /// <summary>The legal name, for an ORGANISATION client.</summary>
    string? CorporateName = null)
{
    /// <summary>Whether these details describe a company rather than a human.</summary>
    public bool IsOrganisation => !string.IsNullOrWhiteSpace(CorporateName);
}
