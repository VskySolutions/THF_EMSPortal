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

    /// <summary>
    /// Whether this row stands for a human being or an organisation — see <see cref="Enums.PartyType"/>.
    /// It decides which field holds the name, and it is what the client picker filters on when a request
    /// is for an individual. Defaults to <see cref="Enums.PartyType.Individual"/>, which is what every row
    /// written before the column existed is.
    /// </summary>
    public PartyType PartyType { get; set; } = PartyType.Individual;

    /// <summary>
    /// The ORGANISATION's legal name, for a person record that stands for a company rather than a human —
    /// "Falcon Manufacturing Group". Null on everybody else, which is nearly everybody.
    /// <para>
    /// It exists because a REMS client is a Person whatever kind of entity they are, and an entity type
    /// other than Individual has no first or last name to put in the two boxes above. Splitting
    /// "Falcon Manufacturing Group" across them produced a first name of "Falcon" and a surname of
    /// "Manufacturing Group", which is how a company ended up filed under a person's name and sorted
    /// under F.
    /// </para>
    /// <para>
    /// One column rather than a second table because the two are the same record from every other angle:
    /// the same email and phone, the same addresses, the same provenance, the same picker. What differs
    /// is only which field holds the name — see <see cref="IsOrganisation"/> and
    /// <see cref="ClientDisplayName"/>.
    /// </para>
    /// </summary>
    public string? CorporateName { get; set; }

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
    /// Whether this record stands for a company rather than a human. Reads the declared
    /// <see cref="PartyType"/> — NOT whether <see cref="CorporateName"/> happens to be filled in, so a
    /// half-completed organisation is still an organisation and a person who somehow acquired a corporate
    /// name is still a person.
    /// </summary>
    [NotMapped]
    public bool IsOrganisation => PartyType == PartyType.Organisation;

    /// <summary>
    /// The name as a CLIENT is filed and read across the platform: <c>LastName FirstName Suffix</c> for a
    /// person — "Smith John Jr." — and the plain corporate name for an organisation.
    ///
    /// <para>
    /// SURNAME FIRST, which is not how <see cref="FullName"/> reads and is deliberate. A client list is
    /// scanned and searched by family name, so that is what has to be at the left edge of the column and
    /// what an alphabetical sort has to order on. FullName stays as it was — it is how a person is
    /// ADDRESSED, and an email that opened "Dear Smith John" would be worse than the sort was.
    /// </para>
    /// <para>
    /// The suffix comes last, as it does everywhere else the platform writes a name (see
    /// <see cref="Suffix"/>): it is a particle on the name, not part of the family name, and "Smith Jr."
    /// in a surname column is somebody nobody finds by searching for their name.
    /// </para>
    /// <para>
    /// Falls back to <see cref="DisplayName"/> for a record that carries neither a corporate name nor a
    /// surname — a contact captured from one free-text box before the split existed.
    /// </para>
    /// <para>
    /// COMPUTED BY THE DATABASE (a persisted computed column — see <c>PersonConfiguration</c>), which is
    /// why it has no body here and no setter. Every REMS list searches, sorts and pages on the client's
    /// name, and all of that has to happen in SQL over the whole set rather than over the twenty rows
    /// already fetched. The alternative was this CASE expression written out at eight query sites, where
    /// the eighth would eventually disagree with the other seven. Here the database is the only thing
    /// that composes it, so it cannot drift and it can be indexed.
    /// </para>
    /// <para>
    /// It is therefore EMPTY on an entity that has not been saved yet. Nothing should read it before the
    /// round trip; compose the name from the parts if you need it in-flight.
    /// </para>
    /// </summary>
    public string ClientDisplayName { get; private set; } = string.Empty;

    // ---- A client that is a Maconomy customer ----

    /// <summary>
    /// The Maconomy customer this client is, when they were picked from Maconomy. It is ALL the portal
    /// holds about them: the name, email and phone columns stay empty and are read from Maconomy by this
    /// number. Unique per tenant.
    /// </summary>
    public string? MaconomyCustomerNumber { get; set; }

    /// <summary>What Maconomy says about that customer, set on each read and never saved. Null when it could not be read.</summary>
    [NotMapped]
    public MaconomyClientDetails? Maconomy { get; set; }

    [NotMapped]
    public bool IsMaconomyClient => !string.IsNullOrWhiteSpace(MaconomyCustomerNumber);

    /// <summary>
    /// The client's name wherever it is READ: Maconomy's for a Maconomy customer, the record's own
    /// otherwise. SQL cannot see the first, so lists search and sort it through the client directory.
    /// </summary>
    [NotMapped]
    public string ClientName => IsMaconomyClient
        ? Maconomy?.Name ?? MaconomyPlaceholderName(MaconomyCustomerNumber!)
        : ClientDisplayName;

    /// <summary>What stands in for the name while Maconomy cannot be read: the one thing the portal does hold.</summary>
    public static string MaconomyPlaceholderName(string customerNumber) => $"Maconomy customer {customerNumber.Trim()}";

    /// <summary>The email to reach the client at: Maconomy's for a Maconomy customer.</summary>
    [NotMapped]
    public string? ClientEmail => IsMaconomyClient ? Maconomy?.Email : PrimaryEmail;

    /// <summary>The client's telephone: Maconomy's for a Maconomy customer.</summary>
    [NotMapped]
    public string? ClientPhone => IsMaconomyClient ? Maconomy?.Phone : MobileNumber;

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
