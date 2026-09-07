namespace EmsPortal.Domain.Enums;

/// <summary>
/// The role a person plays as a contact on a REMS entity (REMS WO-110). A known set that varies by
/// industry group; persisted as a free string code on <c>REMSEntityContact.ContactRole</c> rather
/// than an enum column, so this type documents the canonical values used by the application.
/// <para>
/// The business roles are named for what the firm needs from the person rather than for the office they
/// hold: not every client has a CEO or a CFO, and asking a two-partner practice for both left the client
/// guessing which of them to put where. <c>RenameRemsBusinessContactRoles</c> rewrote the stored codes.
/// </para>
/// </summary>
public enum RemsContactRole
{
    /// <summary>The individual themselves (Individual industry group).</summary>
    Self,

    /// <summary>The individual's spouse.</summary>
    Spouse,

    /// <summary>
    /// Who the firm speaks to about the engagement — the main person on the client's side. Was
    /// <c>CEO</c>.
    /// </summary>
    PrimaryClientContact,

    /// <summary>Who the firm speaks to about the client's finances. Was <c>CFO</c>.</summary>
    FinancialContact,

    /// <summary>
    /// Who the firm bills. Was <c>AccountsPayable</c>.
    /// <para>
    /// The only role an entity may hold MORE THAN ONE of: the intake form asks who should be invoiced and
    /// lets the client name several people, and being named second does not make somebody a different kind
    /// of contact. The unique index on (TenantId, REMSEntityId, ContactRole) excludes this role by name for
    /// exactly that reason — every other role below is one per entity.
    /// </para>
    /// </summary>
    BillingContact,

    /// <summary>Anyone else the client wants the firm to have — optional, and asked once.</summary>
    OtherContact,

    /// <summary>Finance Director (Government industry group).</summary>
    FinanceDirector,

    /// <summary>The trustee or personal representative for a trust or estate. Optional for now.</summary>
    TrustEstateContact,

    // ---- Retired ----
    // Neither is asked for any more: a client's banker and lawyer are their advisers rather than the
    // firm's contacts on the engagement, and the two boxes were left blank on almost every form. The
    // members stay because rows written before RenameRemsBusinessContactRoles still carry these codes,
    // and a stored string with nothing behind it fails every read of the contact it names.

    /// <summary>Banking contact. RETIRED — no longer asked for; historical rows only.</summary>
    Banker,

    /// <summary>Legal contact. RETIRED — no longer asked for; historical rows only.</summary>
    Lawyer,
}
