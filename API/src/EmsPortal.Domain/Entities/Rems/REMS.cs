using System.ComponentModel.DataAnnotations.Schema;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// Root of a REMS (Request for Engagement / new-client onboarding) request (WO-110). Tenant-owned and
/// soft-deletable. Carries the staff-facing intake details; the customer-facing form, the resulting
/// client, and downstream engagements hang off this aggregate. Option-set-valued columns
/// (<see cref="Type"/>, <see cref="Status"/>) store the option item's string code, not a foreign key.
/// </summary>
public class REMS : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant (tenant-scoped).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Human-readable request number, unique per tenant (e.g. <c>REMS-1</c>).</summary>
    public string REMSNumber { get; set; } = string.Empty;

    /// <summary>
    /// The initiator's message ("Message from Partner"). No longer asked for — the request's Conversation
    /// thread carries that context, because it reaches the admin, the CSE and the approvers and can be
    /// replied to. Kept because older requests hold text somebody wrote, and the request page still shows
    /// it where there is any.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// How this referral relates to THF: a foreign key to the <c>REMS.Type</c> option-set item. Required —
    /// a request is always one or the other, and the client lookup marks it by the code this resolves to.
    /// </summary>
    public Guid TypeId { get; set; }

    /// <summary>
    /// The stage the request is at: a foreign key to the <c>REMS.Status</c> option-set item. Required.
    ///
    /// <para>
    /// The whole workflow keys off the CODE this resolves to (RemsRequestStatuses), which is why the
    /// values on that list cannot be added to, deleted or re-coded. Read it through the
    /// <see cref="Status"/> navigation.
    /// </para>
    /// </summary>
    public Guid StatusId { get; set; }

    /// <summary>Admin/staff member the request is assigned to (User).</summary>
    public Guid? AdminAssignedToId { get; set; }

    /// <summary>Client Service Executive assigned to the request (User).</summary>
    public Guid? CSEId { get; set; }

    /// <summary>Loose reference to an existing client record (not a foreign key).</summary>
    public Guid? ExistingClientReferenceId { get; set; }

    /// <summary>
    /// The <see cref="Person"/> master record this request's client is, set on every save. Where
    /// <see cref="ExistingClientReferenceId"/> records that intake <em>matched</em> a client THF already
    /// had — null for a brand-new client — this is simply who the client is, minted on the spot when
    /// nobody matched. That is what makes the client findable in the picker next time, and what a later
    /// "convert this client into a user" hangs off (a User points at a Person via <c>User.PersonId</c>).
    /// <para>
    /// Null only on requests written before the column existed and never saved since.
    /// </para>
    /// </summary>
    public Guid? ClientPersonId { get; set; }

    // ---- The client's own details are the CLIENT PERSON's ----
    //
    // A request used to carry its own copy of the client's name, generational suffix, email and mobile,
    // beside a ClientPersonId pointing at the Person record for the same client. Two places holding one
    // fact is one too many: editing the person left the request saying something else, and the lists, the
    // emails and the intake link each read whichever copy they happened to reach.
    //
    // The four below are now READ-THROUGHS onto that Person. They keep their old names deliberately, so
    // every surface that already asks a request for its client's email still asks the same question and
    // gets a better answer. What changed is that none of them can be written any more — the client is
    // saved by writing the PERSON (RemsRequestsController.ResolveClientPersonAsync).
    //
    // ClientPerson is AutoInclude'd (see RemsConfiguration), so these are populated on every read of a
    // request without any caller having to remember an Include.

    /// <summary>
    /// The client's name as it reads — "Smith John Jr." for a person, the legal name for an organisation.
    /// Composed by the database on <see cref="Person.ClientDisplayName"/>, which is why every list,
    /// notification and email says it the same way and SQL can sort and search on it.
    /// </summary>
    [NotMapped]
    public string ClientDisplayName => ClientPerson?.ClientDisplayName ?? string.Empty;

    /// <summary>The generational particle on the client's name — Jr., Sr., II, III, IV — or null.</summary>
    [NotMapped]
    public string? ClientNameSuffix => ClientPerson?.Suffix;

    /// <summary>
    /// Any name of this client, read as it should be — with the client's own suffix after it.
    /// <para>
    /// Needed because the client's name exists in two places once their intake form comes back: the name
    /// on the client's Person record, and the name the CLIENT typed, which is what <c>REMSClient.Name</c>
    /// and the main <c>REMSEntity.Name</c> hold. The intake form never asks for a suffix — it is the
    /// firm's own particle on the name, set at intake — so a surface showing the client's own version was
    /// showing "Smith John" where every list beside it said "Smith John Jr.".
    /// </para>
    /// <para>
    /// It joins the particle on and nothing else. It does not REORDER a name: a submission stored before
    /// the client name became surname-first holds "John Smith" and comes back "John Smith Jr.", which is
    /// the record of what was sent with the firm's particle on it. Submissions from here on are stored
    /// surname-first and read "Smith John Jr." — see <c>RemsFormPayloadV1.EffectiveClientName</c>.
    /// </para>
    /// <para>
    /// A blank name falls back to <see cref="ClientDisplayName"/>; a name that already carries the suffix
    /// is returned untouched, so this is safe to apply to a value that may already have been through it.
    /// </para>
    /// </summary>
    public string WithClientSuffix(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return ClientDisplayName;
        }

        var suffix = ClientNameSuffix?.Trim() ?? string.Empty;
        if (suffix.Length == 0 || trimmed.EndsWith(" " + suffix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        // ON THE END, not in front. That is where a generational particle sits whichever order the name
        // itself is in — "Smith John Jr." — and it is the order the form asks in too: the suffix box sits
        // to the right of Last Name.
        return $"{trimmed} {suffix}";
    }

    /// <summary>Customer email used to reach out; required together-or-with mobile at app level.</summary>
    [NotMapped]
    public string? CustomerEmail => ClientPerson?.PrimaryEmail;

    /// <summary>Customer mobile number; required together-or-with email at app level.</summary>
    [NotMapped]
    public string? CustomerMobileNumber => ClientPerson?.MobileNumber;

    /// <summary>
    /// The shareholder or CSE this request was raised FOR, when a delegate raised it on their behalf.
    /// Null when the creator was acting as themselves.
    /// <para>
    /// Dual attribution: <c>CreatedById</c> keeps the person who actually did it, and this keeps whose
    /// work it is — "prepared by X on behalf of Y". It is also what puts the request in the principal's
    /// list rather than only the delegate's.
    /// </para>
    /// </summary>
    public Guid? OnBehalfOfUserId { get; set; }

    // ---- Navigations ----
    public OptionSetItem? Type { get; set; }
    public OptionSetItem? Status { get; set; }
    public Person? ClientPerson { get; set; }
    public ICollection<REMSFiles> Files { get; set; } = new List<REMSFiles>();
    public ICollection<REMSForm> Forms { get; set; } = new List<REMSForm>();
    public ICollection<REMSClient> Clients { get; set; } = new List<REMSClient>();

    /// <summary>Other businesses the client declared at intake, each awaiting its own request.</summary>
    public ICollection<REMSAdditionalEntity> AdditionalEntities { get; set; } = new List<REMSAdditionalEntity>();

    /// <summary>Every time the admin returned this request to its initiator, newest last.</summary>
    public ICollection<REMSSendBack> SendBacks { get; set; } = new List<REMSSendBack>();
}
