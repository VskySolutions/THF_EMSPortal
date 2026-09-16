namespace EmsPortal.Domain.Entities;

/// <summary>
/// The <see cref="REMS.Status"/> option-set codes that make up the request lifecycle (WO-111).
/// <see cref="REMS.Status"/> stores the option item's string VALUE, so these mirror the seeded
/// <c>REMS.Status</c> option list (see <c>DefaultOptionSets</c>).
/// <para>
/// The lifecycle names the STAGE the request is in — i.e. who it is waiting on — rather than the event
/// that last happened to it, so a row's badge answers "what is happening to this request right now":
/// </para>
/// <list type="number">
///   <item><see cref="Draft"/> — with its initiator, not yet sent to the client.</item>
///   <item><see cref="AwaitingCustomer"/> — the intake form has been emailed; the ball is with the client.</item>
///   <item><see cref="AdminReview"/> — the client's form is in; the named Admin is reviewing it.</item>
///   <item><see cref="ReturnedToInitiator"/> — the Admin sent Engagement Setup back with a reason.</item>
///   <item><see cref="AwaitingAdminConfirmation"/> — the initiator revised it; back with the Admin to confirm.</item>
///   <item><see cref="PendingApproval"/> — routed to the approvers; waiting on their decisions.</item>
///   <item><see cref="ChangesRequested"/> — a round failed on approver declines; back with the initiator.</item>
///   <item><see cref="Approved"/> — the engagement is approved (terminal).</item>
/// </list>
/// <para>
/// Steps 3-5 loop: the Admin can return the request as often as the setup needs work. The Admin is the
/// only route into approval, so both rework states hand back to them rather than straight to the approvers.
/// </para>
/// <para>
/// A request carries exactly one engagement, so the last three track that engagement directly.
/// </para>
/// </summary>
public static class RemsRequestStatuses
{
    /// <summary>A saved, not-yet-sent request. Visible only to its creator (AC-REMS-004.11).</summary>
    public const string Draft = "draft";

    /// <summary>The intake form has been emailed to the client and is awaiting their submission (REMS WO-112).</summary>
    public const string AwaitingCustomer = "awaiting_customer";

    /// <summary>
    /// The customer completed and submitted their intake form (set on public submit, REMS WO-113), so the
    /// request is now with the Admin the initiator named. The code is kept as <c>customer_submitted</c>
    /// because it is what existing rows hold; only the label moved, from "Engagement Setup" to
    /// "Admin Review", which is what the stage actually is now that the initiator fills the setup up front.
    /// </summary>
    public const string AdminReview = "customer_submitted";

    /// <summary>
    /// The Admin returned the request to its initiator for Engagement Setup rework, with a mandatory
    /// reason (see <c>REMSSendBack</c>). Client Intake is read-only to them; only the setup accepts writes.
    /// </summary>
    public const string ReturnedToInitiator = "returned_to_initiator";

    /// <summary>The initiator revised the setup and handed it back; the Admin confirms or returns it again.</summary>
    public const string AwaitingAdminConfirmation = "awaiting_admin_confirmation";

    /// <summary>The engagement has been routed for approval and no round has failed (REMS WO-114).</summary>
    public const string PendingApproval = "pending_approval";

    /// <summary>
    /// Enough approvers declined to close the round (see <c>RemsApprovalThreshold</c>), so it is back with
    /// the INITIATOR for Engagement Setup rework — not with the Admin, who sees it again only when the
    /// initiator hands it back for confirmation. Carries the same edit rights as
    /// <see cref="ReturnedToInitiator"/>; kept separate so the lists can tell "the admin sent this back"
    /// from "the approvers declined it".
    /// </summary>
    public const string ChangesRequested = "changes_requested";

    /// <summary>The request's engagement has been fully approved.</summary>
    public const string Approved = "approved";

    /// <summary>
    /// Not a stored status: what every list calls a request that is with the admins and that no admin has
    /// picked up. The filters take it beside the real codes, so the status column can be narrowed to what
    /// it shows — see <c>RemsRequestFilters</c>.
    /// </summary>
    public const string WaitingForPickup = "waiting_for_pickup";

    /// <summary>
    /// The stages where the request sits with its INITIATOR — the person who raised it fills the client
    /// details and the engagement setup, sends the intake link, and does any rework the Admin or the
    /// approvers ask for. <see cref="AwaitingCustomer"/> counts: the ball is with the client, but the setup
    /// is still the initiator's to finish while they wait.
    /// </summary>
    public static bool IsWithInitiator(string? status)
        => status is Draft or AwaitingCustomer or ReturnedToInitiator or ChangesRequested;

    /// <summary>The stages where the request sits with the reviewing Admin (the client has answered).</summary>
    public static bool IsWithAdmin(string? status)
        => status is AdminReview or AwaitingAdminConfirmation;

    /// <summary>
    /// The two SEND-BACK stages: the setup has been sent back for rework, either by the Admin
    /// (<see cref="ReturnedToInitiator"/>) or by the approvers declining a round
    /// (<see cref="ChangesRequested"/>). Both sit with the initiator, and in both the Admin keeps the
    /// ability to work the setup — see <c>RemsSetupAccess.CanWork</c>: a send-back asks the initiator for
    /// changes, it does not hand the request away.
    /// </summary>
    public static bool IsRework(string? status)
        => status is ReturnedToInitiator or ChangesRequested;

    /// <summary>
    /// The stages where nothing on the request may change: a round is open with the approvers, or they
    /// have approved it. The request page goes read-only here, and the lists offer View alone.
    /// </summary>
    public static bool IsFrozen(string? status)
        => status is PendingApproval or Approved;
}
