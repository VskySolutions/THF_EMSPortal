using EmsPortal.Domain.Entities;

namespace EmsPortal.Api.Models.Profile;

/// <summary>Shared projection of a <see cref="Person"/> (and its address) to API response shapes.</summary>
public static class PersonProfileMapper
{
    /// <summary>
    /// <paramref name="audit"/> is the record's provenance block, resolved by the caller: naming the
    /// actors is a query, and a static mapper is not where a query belongs.
    /// </summary>
    public static PersonProfileResponse Map(Person p, RecordAudit audit) => new(
        p.Id, p.PersonCode, p.UserId, p.TenantId,
        p.Suffix,
        // A Maconomy client's name and contact details are Maconomy's; their own columns are empty.
        p.FirstName, p.MiddleName, p.LastName,
        p.IsMaconomyClient ? p.ClientName : p.DisplayName, p.IsMaconomyClient ? p.ClientName : p.FullName,
        p.PreferredName, p.Gender, p.DateOfBirth, p.MaritalStatus, p.Nationality,
        p.ClientEmail, p.SecondaryEmail, p.ClientPhone, p.CountryCode, p.AlternateMobileNumber,
        p.EmergencyContactName, p.EmergencyContactRelationship, p.EmergencyContactNumber,
        p.EmployeeCode,
        p.ProfileCompletionPercentage, p.IsProfileVerified, p.LastProfileUpdatedOn, p.Notes,
        p.ProfileMediaId, p.ProfileMedia?.PublicUrl,
        p.Address is null ? null : MapAddress(p.Address),
        audit);

    public static AddressResponse MapAddress(Address a) => new(
        a.Id, a.AddressType.ToString(), a.AddressLine1, a.AddressLine2, a.Landmark,
        a.BuildingName, a.FloorNumber, a.UnitNumber, a.CountryCode, a.CountryName, a.StateCode,
        a.StateName, a.CityName, a.PostalCode);
}
