namespace EmsPortal.Api.Models.Rems;

/// <summary>Build/save the EMS onboarding form for a REMS request (WO-112, AC-REMS-007).</summary>
public sealed class SaveRemsFormRequest
{
    /// <summary>The Client Service Executive to assign to the request (User id) — required.</summary>
    public Guid CseUserId { get; set; }

    /// <summary>
    /// Industry group (option-set <c>REMS.EntityType</c> code: individual/business/government) —
    /// required.
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
}

/// <summary>The current EMS form state on a request (WO-112).</summary>
public sealed record RemsFormInfo(
    Guid Id,
    string EntityType,
    string InviteCode,
    string FormLink,
    string Status,
    DateTime? SentOnUtc,
    DateTime? SubmittedOnUtc,
    DateTime? InviteLockedOnUtc,
    bool IsLocked);

/// <summary>
/// The EMS form build-screen model (WO-112, AC-REMS-007.1): the REMS request context plus the current
/// form (if any).
/// </summary>
public sealed record RemsFormBuildScreen(
    Guid RemsId,
    string RemsNumber,
    string ClientName,
    string RequestStatus,
    string? CustomerEmail,
    string? CustomerMobileNumber,
    RemsUserRef? Cse,
    RemsFormInfo? Form);

/// <summary>
/// The pre-send preview (WO-112, AC-REMS-008.1): where the form link will be emailed, and the link
/// itself.
/// </summary>
public sealed record RemsFormPreview(
    string? DestinationEmail,
    string FormLink,
    // The effective template rendered with this request's values — exactly what the client would receive if
    // the admin sent without touching it.
    string? Subject,
    string? Body);

/// <summary>Send payload: the subject / body as the admin left them in the send dialog.</summary>
public sealed class SendRemsFormRequest
{
    public string? Subject { get; set; }
    public string? Body { get; set; }
}

/// <summary>A single email-delivery event row in the form email log (WO-112, AC-REMS-008.6), newest first.</summary>
public sealed record RemsEmailEventRow(
    Guid Id,
    string EventType,
    string RecipientEmail,
    DateTime OccurredOnUtc,
    string? Detail,
    // Who pressed Send or Remind. Null on the events a provider reported back (delivered / opened /
    // failed): nobody here caused those, and "the system" is not a person worth naming.
    string? SentBy,
    string? Subject,
    string? Body);

/// <summary>
/// The email log as one screen: the delivery events, and whether THIS caller can nudge the client from
/// it.
/// </summary>
/// <paramref name="ClientFormLink"/>
public sealed record RemsEmailLog(
    bool CanRemind,
    string? RemindBlockedReason,
    IReadOnlyList<RemsEmailEventRow> Events,
    string? ClientFormLink);

/// <summary>
/// One EMS-Inbox row (WO-112, AC-REMS-009): a request with a form, its request context, form state,
/// creator, and the latest send/delivery/open info.
/// </summary>
// Trailing four: the owning request's audit trail, offered as hidden-by-default columns.
public sealed record RemsInboxRow(
    Guid RemsId,
    string RemsNumber,
    string ClientName,
    string EngagementType,
    string RequestStatus,
    string FormStatus,
    RemsUserRef? FormCreatedBy,
    DateTime? FormSentOnUtc,
    string? LatestEmailEventType,
    DateTime? LatestEmailEventOnUtc,
    // Who picked the request up — engagement setup is theirs, so the list can say so.
    RemsUserRef? AssignedAdmin,
    string? CreatedBy,
    DateTime CreatedOnUtc,
    string? UpdatedBy,
    DateTime UpdatedOnUtc);
