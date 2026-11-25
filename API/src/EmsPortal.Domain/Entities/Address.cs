using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// Reusable address record shared across CRM modules (user/employee/company/billing/
/// shipping/office/vendor/customer addresses). Referenced by other entities via its
/// <see cref="Id"/> (WO-61).
/// </summary>
public class Address : AuditableEntity
{
    public Guid Id { get; set; }

    // ---- Address information ----
    public AddressType AddressType { get; set; } = AddressType.Home;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? Landmark { get; set; }
    public string? BuildingName { get; set; }
    public string? FloorNumber { get; set; }
    public string? UnitNumber { get; set; }

    // ---- Location information ----
    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }
    public string? StateCode { get; set; }
    public string? StateName { get; set; }
    public string? CityName { get; set; }
    public string? PostalCode { get; set; }

    // ---- Who the post is addressed to ----
    //
    // An address is a place; these five are the person AT it. They are here rather than on a contact
    // record of their own because "where do we send the invoice, and who is it addressed to?" is one
    // question with one answer, and splitting it across two records is how a client ends up with a
    // billing address and nobody to send it to — or with three billing addresses and three billing
    // contacts and nothing saying which belongs to which.
    //
    // Every one of them is optional, on every address. Most addresses in the platform are a place and
    // nothing more (a person's home, an office), and the field-set only asks for these where a form
    // opts in — see AppAddressFields' `contact`, which today only the client intake's billing
    // addresses set.

    /// <summary>
    /// The generational particle on the addressee's name — Jr., Sr., III. Held beside the name rather
    /// than typed into it, for the same reason it is on <see cref="Person"/>: the name is what a record
    /// is filed and searched under, and "Smith Jr." in a surname column is somebody nobody finds by
    /// searching for their name.
    /// </summary>
    public string? Suffix { get; set; }

    /// <summary>The addressee's given name.</summary>
    public string? FirstName { get; set; }

    /// <summary>The addressee's family name.</summary>
    public string? LastName { get; set; }

    /// <summary>Where to reach the addressee by email — the address an invoice is sent to.</summary>
    public string? Email { get; set; }

    /// <summary>The addressee's phone number, stored in E.164 like every other number on the platform.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>The country picked for <see cref="PhoneNumber"/>, ISO-2 ("CA"): +1 alone cannot tell Canada from the US.</summary>
    public string? PhoneCountryCode { get; set; }
}
