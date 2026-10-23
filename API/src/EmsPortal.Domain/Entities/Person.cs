using System.ComponentModel.DataAnnotations.Schema;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// Canonical CRM person master record holding all personal profile information for a
/// <see cref="User"/> (and, in future, CRM contacts/employees/customers). Authentication
/// data stays on <see cref="User"/>; profile data lives here (WO-61).
/// </summary>
public class Person : AuditableEntity
{
    public Guid Id { get; set; }

    /// <summary>Unique business identifier (e.g. PER-000123).</summary>
    public string PersonCode { get; set; } = string.Empty;

    /// <summary>Back-reference to the owning user (the FK lives on <see cref="User.PersonId"/>).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Owning tenant for this person (optional; platform-level persons have none).</summary>
    public Guid? TenantId { get; set; }

    public Guid? ProfileMediaId { get; set; }

    public Guid? AddressId { get; set; }

    // ---- Personal information ----

    /// <summary>
    /// The generational particle on a person's name — Jr., Sr., II, III, IV — held beside the family name
    /// rather than typed into it. It is not part of the name a person is FILED under, so it stays out of
    /// <see cref="FullName"/>: "Smith Jr." in a surname column is somebody nobody finds by searching for
    /// their name, and "John Smith Jr." matches no record when "John Smith" matches the man. It is put
    /// back AFTER the name wherever the name is READ — the order every box that asks for one uses, with
    /// Suffix to the right of Last Name.
    /// <para>
    /// Free text, capped at 16 characters, with the common suffixes offered as suggestions — the same
    /// bargain the REMS client name's suffix strikes: the list is what most people need, not all any
    /// person may have, and one nobody thought to seed is not a reason to file somebody under the wrong
    /// name.
    /// </para>
    /// <para>
    /// This column was <c>Prefix</c> — a courtesy title (Mr., Dr.) — until the platform settled on one
    /// particle per name. A title is not a suffix, so the titles already recorded were not carried across.
    /// </para>
    /// </summary>
    public string? Suffix { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PreferredName { get; set; }
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? MaritalStatus { get; set; }
    public string? Nationality { get; set; }

    /// <summary>FirstName + MiddleName + LastName (trimmed); falls back to DisplayName.</summary>
    [NotMapped]
    public string FullName =>
        string.Join(" ", new[] { FirstName, MiddleName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } name
            ? name
            : DisplayName;

    /// <summary>
    /// The name as a CLIENT is filed and read: <c>LastName FirstName Suffix</c>, surname first so client lists scan and
    /// sort by family name; falls back to <see cref="DisplayName"/>. Computed and persisted by the database (see
    /// <c>PersonConfiguration</c>) so every REMS query searches and sorts it in SQL; empty until the row is saved.
    /// </summary>
    public string ClientDisplayName { get; private set; } = string.Empty;

    // ---- Contact information ----
    public string? PrimaryEmail { get; set; }
    public string? SecondaryEmail { get; set; }
    public string? MobileNumber { get; set; }
    public string? CountryCode { get; set; }
    public string? AlternateMobileNumber { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactRelationship { get; set; }
    public string? EmergencyContactNumber { get; set; }

    // ---- Professional information ----
    // No department here: it is held per tenant on the USER account (UserDepartment, which also names the
    // REMS Department Director), and a person-level copy would be a second answer nothing reads.
    public string? EmployeeCode { get; set; }

    // ---- Profile metadata ----
    public int ProfileCompletionPercentage { get; set; }
    public bool IsProfileVerified { get; set; }
    public DateTime? LastProfileUpdatedOn { get; set; }
    public string? Notes { get; set; }

    /// <summary>Active flag (soft-delete handled by <see cref="AuditableEntity.Deleted"/>).</summary>
    public bool IsActive { get; set; } = true;

    // ---- Provenance ----
    // Persons arrive from several places, not just the Person screen: a REMS engagement's role contacts
    // and a client's submitted EMS form both mint one to satisfy REMSEntityContact.PersonId. Once mixed
    // into the same list they are indistinguishable, which matters — a contact captured off a public
    // form is not a colleague somebody onboarded, and the client picker offers all of them alike.
    //
    // Uses the platform's standard (EntityType, EntityId) pair (Universal Features ADR-001) rather than
    // an FK: the source is polymorphic, and a source record being deleted must not cascade into the
    // person it created.

    /// <summary>
    /// What kind of record created this person, e.g. <c>Rems</c> for a contact captured during an
    /// engagement or from a client's EMS form, <c>Person</c> for one entered on the Person screen.
    /// Null on rows written before provenance was tracked — unknown, not "created by nothing".
    /// <para>
    /// <c>Client</c> is the one value that says what the person IS rather than where they came from: a
    /// client of the firm, captured at REMS intake. It is what the client picker filters on, so a
    /// colleague or a role contact is never offered as somebody to open an engagement for.
    /// </para>
    /// </summary>
    public EntityType? SourceEntityType { get; set; }

    /// <summary>
    /// The specific record that created this person (the REMS request id, and so on). Null when the
    /// source is the Person screen itself, which has no record to point back at.
    /// </summary>
    public Guid? SourceEntityId { get; set; }

    // ---- Navigations ----
    public Address? Address { get; set; }
    public Media? ProfileMedia { get; set; }
    public Tenant? Tenant { get; set; }
}
