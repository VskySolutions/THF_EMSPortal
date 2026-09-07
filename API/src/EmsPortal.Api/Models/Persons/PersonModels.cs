using EmsPortal.Api.Models.Profile;

namespace EmsPortal.Api.Models.Persons;

/// <summary>Create payload for a standalone <c>Person</c> master record (WO-61).</summary>
public sealed class CreatePersonRequest
{
    /// <summary>Owning tenant (optional).</summary>
    public Guid? TenantId { get; set; }

    // Personal
    /// <summary>
    /// The generational particle on the name — Jr., Sr., II, III, IV. Free text; not part of the
    /// filed name.
    /// </summary>
    public string? Suffix { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? PreferredName { get; set; }
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? MaritalStatus { get; set; }
    public string? Nationality { get; set; }

    // Contact
    public string? PrimaryEmail { get; set; }
    public string? SecondaryEmail { get; set; }
    public string? MobileNumber { get; set; }
    public string? CountryCode { get; set; }
    public string? AlternateMobileNumber { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactRelationship { get; set; }
    public string? EmergencyContactNumber { get; set; }

    // Professional
    public string? EmployeeCode { get; set; }

    public string? Notes { get; set; }

    /// <summary>Optional primary address; created alongside the person when present.</summary>
    public AddressInput? Address { get; set; }
}

/// <summary>Update payload for a person.</summary>
public sealed class UpdatePersonRequest
{
    /// <summary>Owning tenant.</summary>
    public Guid? TenantId { get; set; }

    // Personal
    /// <summary>The generational particle on the name.</summary>
    public string? Suffix { get; set; }

    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? PreferredName { get; set; }
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? MaritalStatus { get; set; }
    public string? Nationality { get; set; }

    // Contact
    public string? PrimaryEmail { get; set; }
    public string? SecondaryEmail { get; set; }
    public string? MobileNumber { get; set; }
    public string? CountryCode { get; set; }
    public string? AlternateMobileNumber { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactRelationship { get; set; }
    public string? EmergencyContactNumber { get; set; }

    // Professional
    public string? EmployeeCode { get; set; }

    public string? Notes { get; set; }
    public bool? IsActive { get; set; }

    /// <summary>Primary address; upserted when present.</summary>
    public AddressInput? Address { get; set; }
}

/// <summary>List-row projection for the People grid.</summary>
public sealed record PersonSummary(
    Guid Id,
    string PersonCode,
    string FullName,
    string? PrimaryEmail,
    string? MobileNumber,
    Guid? TenantId,
    string? TenantName,
    bool IsUser,
    bool IsActive,
    // Where this record came from — the Person screen, a REMS engagement, a client's EMS form.
    string? SourceEntityType,
    Guid? SourceEntityId,
    string? CreatedBy,
    string? UpdatedBy,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

/// <summary>Lightweight option for the user-create Person dropdown.</summary>
public sealed record PersonSelectItem(
    Guid Id,
    string FullName,
    string? PrimaryEmail,
    string? MobileNumber,
    string? CountryCode,
    Guid? TenantId,
    bool IsUser);

/// <summary>Full person record (reuses <see cref="PersonProfileResponse"/> plus a user-link flag).</summary>
public sealed record PersonDetail(
    PersonProfileResponse Profile,
    bool IsUser);
