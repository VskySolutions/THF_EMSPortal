namespace EmsPortal.Domain.Entities;

/// <summary>
/// Another person on an individual client's return — a spouse, a child, anyone else the firm is preparing
/// for — declared on the intake form's "Spouse &amp; More Individuals" card.
/// <para>
/// A row of its own rather than a <see cref="REMSEntityContact"/>, for two reasons. An entity holds at
/// most ONE contact per role (the unique index on (tenant, entity, role) exempts only BillingContact), and
/// a client with three children has three people of one kind. And a contact record answers "who do we
/// speak to?", which is not what is being asked here: what the firm needs to know about a second person
/// on a return is how it is FILED and who is INVOICED for it, and neither of those is a property of a
/// contact.
/// </para>
/// <para>
/// It is not an entity either. These people do not get an engagement, an approval round or a request of
/// their own — that is what <see cref="REMSAdditionalEntity"/> is for, and it is a different question.
/// They belong to the client's main entity and are prepared alongside them.
/// </para>
/// </summary>
public class REMSAdditionalIndividual : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant (tenant-scoped).</summary>
    public Guid TenantId { get; set; }

    /// <summary>The request whose intake declared this person.</summary>
    public Guid REMSId { get; set; }

    /// <summary>The client entity they are prepared alongside — the request's main entity.</summary>
    public Guid REMSEntityId { get; set; }

    /// <summary>
    /// The person record minted for them, so they are findable in the CRM like any other person the
    /// platform captures. The name, email and phone are duplicated onto the columns below deliberately:
    /// this row is the record of what was DECLARED, and a Person edited afterwards must not silently
    /// rewrite the client's own answer.
    /// </summary>
    public Guid PersonId { get; set; }

    /// <summary>Stable key identifying this row within the submitted payload.</summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>What they are to the client — <c>spouse</c>, <c>child</c> or <c>other</c>.</summary>
    public string RelationType { get; set; } = string.Empty;

    /// <summary>How their return is filed — <c>joint</c> or <c>individual</c>. A child always files individually.</summary>
    public string FilingType { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// The generational particle on their name — Jr., Sr., II, III, IV. Asked for on the same card, in the
    /// box after Last Name, exactly as the client themselves is asked.
    /// <para>
    /// A related client is a client: the Related Entities list names them beside the client they were
    /// declared under, and "Smith John" tells a reader nothing about which John Smith is on this return
    /// when the father and the son are both on file. Held in its own column rather than typed into
    /// <see cref="LastName"/>, for the reason every other name on the platform holds it apart — a surname
    /// of "Smith Jr." is somebody nobody finds by searching for their name.
    /// </para>
    /// <para>
    /// Nullable, and blank on every row declared before the box existed. Nothing is inferred for those:
    /// the particle is an answer the client gives, and a row that was never asked has none.
    /// </para>
    /// </summary>
    public string? Suffix { get; set; }

    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }

    /// <summary>Whether a child is still a minor. Null for anybody who is not a child — it is not asked.</summary>
    public bool? IsMinor { get; set; }

    /// <summary>
    /// Who is invoiced — <c>primary</c> (the client) or <c>separate</c>. A spouse on a JOINT return is
    /// always billed to the primary client, and so is a minor child: one return means one invoice, and it
    /// goes to whoever the return is filed under. A spouse who files individually may be billed either
    /// way.
    /// </summary>
    public string BillingPreference { get; set; } = string.Empty;

    /// <summary>Who the separate invoice is addressed to. Null wherever billing goes to the primary client.</summary>
    public string? BillingFirstName { get; set; }

    /// <inheritdoc cref="BillingFirstName"/>
    public string? BillingLastName { get; set; }

    /// <summary>
    /// How far this person's own return has got, as a foreign key to the
    /// <c>REMS.RelatedEntityStatus</c> option-set item. Null until somebody sets it, which reads as
    /// <see cref="RemsRelatedEntityStatuses.NotInitiated"/> — see that class for why it is set by hand.
    /// <para>
    /// The same column as the one on <see cref="REMSAdditionalEntity"/>, because it answers the same
    /// question: the Related Entities list shows both kinds of related client side by side and reports
    /// their progress the same way. Nothing else about the two rows is alike, which is why they stay two
    /// tables rather than becoming one.
    /// </para>
    /// </summary>
    public Guid? RelatedStatusId { get; set; }

    // ---- Navigations ----
    public REMS? Rems { get; set; }
    public REMSEntity? Entity { get; set; }
    public Person? Person { get; set; }
    public OptionSetItem? RelatedStatus { get; set; }
}
