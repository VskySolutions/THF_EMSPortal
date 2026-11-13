using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Integrations.Maconomy.Mapping;

/// <summary>
/// THE mapping of a client to Maconomy's customer card. Everything that names a customer card field, or
/// decides what goes in it, is in this file and nowhere else: change what the portal sends here.
/// Every field name is as the instance's card specification spells it and was accepted on opening a
/// card (12 Nov 2026): the specification popups are the <c>…name</c> fields, the remarks are
/// <c>customerremark…</c>. The container is the one thing that differs per instance, so the sync service
/// overrides it from configuration.
/// </summary>
public static class MaconomyCustomerDataMapping
{
    public const string DefaultContainer = "dpl:customercard";

    public const string NumberField = "customernumber";
    /// <summary>The contact company the customer belongs to: created first, under the same number.</summary>
    public const string ContactCompanyNumberField = "contactcompanynumber";
    public const string NameField = "name1";
    public const string EmailField = "electronicmailaddress";
    public const string PhoneField = "telephone";
    public const string EntityTypeField = "specification6name";
    public const string IndustryField = "specification1name";

    // The CSE, as their Maconomy employee number, in both places THF keeps it.
    public const string CseField = "specification5name";
    public const string AccountManagerField = "accountmanagernumber";

    // An individual's name in parts, beside the one string in name1.
    public const string FirstNameField = "customerremark27";
    public const string MiddleNameField = "customerremark28";
    public const string LastNameField = "customerremark29";
    public const string SuffixField = "customerremark30";

    // The physical address is the primary one (user, 13 Nov 2026): it is the card's own address block,
    // which Maconomy shares with the customer's contact company.
    public const string PhysicalLine1Field = "name2";
    public const string PhysicalLine2Field = "name3";
    public const string PhysicalCityField = "postaldistrict";
    public const string PhysicalCountryField = "country";
    public const string PhysicalZipCodeField = "zipcode";

    // The mailing address is secondary and has no block of its own: it goes in remarks.
    public const string MailingLine1Field = "customerremark20";
    public const string MailingLine2Field = "customerremark21";
    public const string MailingCityField = "customerremark23";
    public const string MailingStateOrCountryField = "customerremark24";
    public const string MailingZipCodeField = "customerremark25";

    /// <summary>The popup behind <see cref="PhysicalCountryField"/>: THF keeps the US state codes and the countries in one list.</summary>
    public const string CountryList = "popup_countrytype";

    // REMS.AddressType option values.
    private const string MailingAddress = "mailing";
    private const string PhysicalAddress = "physical";

    /// <summary>The card fields every READ names (search, details, the directory). The number is read back, never sent.</summary>
    public static readonly string[] Fields = { NumberField, NameField, EmailField, PhoneField, EntityTypeField, IndustryField };

    /// <summary>The card fields the create names: the read fields and everything else a new customer is given.</summary>
    public static readonly string[] WriteFields = Fields.Concat(new[]
    {
        ContactCompanyNumberField,
        FirstNameField, MiddleNameField, LastNameField, SuffixField, CseField, AccountManagerField,
        PhysicalLine1Field, PhysicalLine2Field, PhysicalCityField, PhysicalCountryField, PhysicalZipCodeField,
        MailingLine1Field, MailingLine2Field, MailingCityField, MailingStateOrCountryField, MailingZipCodeField,
    }).ToArray();

    /// <summary>
    /// What the portal posts as a new customer. A blank value is left out, so Maconomy applies its own
    /// default. The email is passed in because the sync has already checked it against Maconomy; the
    /// numbers are the tenant's option items'; <paramref name="countries"/> is Maconomy's country list,
    /// which turns a state code or a country into the literal the popup stores; <paramref name="cseMaconomyNumber"/>
    /// is the CSE's Maconomy Number from their user record, null when they have none.
    /// </summary>
    public static MaconomyCardWrite Map(
        Client client, string email, MaconomyNumberMap numbers, MaconomyPickList countries, string? cseMaconomyNumber = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [NameField] = CustomerNameOf(client),
        };
        Put(values, EmailField, email);
        Put(values, PhoneField, client.PrimaryPhone ?? client.Person?.MobileNumber);
        Put(values, EntityTypeField, numbers.EntityTypeNumberOf(client.EntityType?.Value));
        Put(values, IndustryField, numbers.IndustryNumberOf(client.Industry?.Value));
        Put(values, CseField, cseMaconomyNumber);
        Put(values, AccountManagerField, cseMaconomyNumber);
        if (!client.IsOrganisation && client.Person is { } person)
        {
            Put(values, FirstNameField, person.FirstName);
            Put(values, MiddleNameField, person.MiddleName);
            Put(values, LastNameField, person.LastName);
            Put(values, SuffixField, person.Suffix);
        }
        if (AddressOf(client, MailingAddress) is { } mailing)
        {
            Put(values, MailingLine1Field, mailing.AddressLine1);
            Put(values, MailingLine2Field, mailing.AddressLine2);
            Put(values, MailingCityField, mailing.CityName);
            Put(values, MailingStateOrCountryField, StateOrCountryOf(mailing));
            Put(values, MailingZipCodeField, mailing.PostalCode);
        }
        if (AddressOf(client, PhysicalAddress) is { } physical)
        {
            Put(values, PhysicalLine1Field, physical.AddressLine1);
            Put(values, PhysicalLine2Field, physical.AddressLine2);
            Put(values, PhysicalCityField, physical.CityName);
            Put(values, PhysicalCountryField, countries.ValueOf(StateOrCountryOf(physical)));
            Put(values, PhysicalZipCodeField, physical.PostalCode);
        }
        return new MaconomyCardWrite(DefaultContainer, WriteFields, values, InitValues: null, KeyField: NumberField);
    }

    /// <summary>The same write under a number: the customer's own, and the contact company it belongs to.</summary>
    public static MaconomyCardWrite WithNumber(MaconomyCardWrite write, string number)
        => write with
        {
            Values = new Dictionary<string, string?>(write.Values, StringComparer.OrdinalIgnoreCase)
            {
                [NumberField] = number,
                [ContactCompanyNumberField] = number,
            },
        };

    /// <summary>
    /// The physical address's state or country when Maconomy's country list does not hold it, else null.
    /// The sync stops on it rather than post a customer Maconomy would refuse, or one without a country.
    /// </summary>
    public static string? UnlistedCountryOf(Client client, MaconomyPickList countries)
    {
        var wanted = AddressOf(client, PhysicalAddress) is { } physical ? StateOrCountryOf(physical) : null;
        return wanted is not null && countries.ValueOf(wanted) is null ? wanted : null;
    }

    /// <summary>
    /// What stands for "where" on the card (user, 12 Nov 2026): the state code for an address in the
    /// United States, the country as selected for any other, with the state then not sent.
    /// </summary>
    public static string? StateOrCountryOf(Address address)
        => IsUnitedStates(address)
            ? Clean(address.StateCode)?.ToUpperInvariant() ?? Clean(address.StateName)
            : Clean(address.CountryName) ?? Clean(address.CountryCode);

    private static bool IsUnitedStates(Address address)
        => string.Equals(Clean(address.CountryCode), "US", StringComparison.OrdinalIgnoreCase)
            || (Clean(address.CountryCode) is null
                && Clean(address.CountryName) is { } name
                && (name.Equals("United States", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("United States of America", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("USA", StringComparison.OrdinalIgnoreCase)));

    // The first address of a kind, as the intake form reads it back.
    private static Address? AddressOf(Client client, string kind)
        => client.Addresses
            .Where(a => !a.Deleted && a.Address is not null && string.Equals(a.AddressType?.Value, kind, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.CreatedOnUtc)
            .Select(a => a.Address)
            .FirstOrDefault();

    /// <summary>The name Maconomy files the customer under: the legal name, or "Last, First Suffix" as typed (user, 22 Oct 2026).</summary>
    public static string CustomerNameOf(Client client)
    {
        if (client.IsOrganisation)
        {
            return client.LegalName?.Trim() ?? string.Empty;
        }
        var person = client.Person;
        var last = person?.LastName?.Trim();
        if (!string.IsNullOrEmpty(last))
        {
            var given = string.Join(" ", new[] { person!.FirstName?.Trim(), person.Suffix?.Trim() }.Where(s => !string.IsNullOrEmpty(s)));
            return given.Length > 0 ? $"{last}, {given}" : last;
        }
        return client.DisplayName.Trim();
    }

    /// <summary>The customer as Maconomy created it, the assigned number included; what was sent fills any blank the reply leaves.</summary>
    public static MaconomyClientDetails FromCreated(IReadOnlyDictionary<string, string?> record, MaconomyCardWrite sent)
    {
        var number = Value(record, NumberField);
        if (number.Length == 0)
        {
            throw new MaconomyException(
                MaconomyFailure.Rejected,
                $"Maconomy created the customer but sent no {NumberField} back. Look for a new customer named \"{Sent(sent, NameField)}\" in Maconomy before syncing again.");
        }
        var name = Value(record, NameField);
        return new MaconomyClientDetails(
            number,
            name.Length > 0 ? name : Sent(sent, NameField) ?? string.Empty,
            null,
            Optional(record, EmailField) ?? Sent(sent, EmailField),
            Optional(record, PhoneField) ?? Sent(sent, PhoneField));
    }

    // ---- The mapping in words ----

    private const string ClientInformation = "Client Information";
    private const string FormContact = "Client's form · Confirm Your Contact Details";
    private const string FormPhysical = "Client's form · Physical Address";
    private const string FormMailing = "Client's form · Mailing Address";
    private const string FormBilling = "Client's form · Billing Information";
    private const string FormIndividuals = "Client's form · Spouse & More Individuals";
    private const string FormContract = "Client's form · Contract Details";
    private const string FormContacts = "Client's form · Contacts (each role)";
    private const string FormEntities = "Client's form · Other Entities";
    private const string NotOnAForm = "Not on a form";

    private const string SameAsClientInformation = "The form corrects the client's record, and the record is what is sent.";
    private const string LeftOutWhenBlank = "Blank is left out.";
    private const string SharedWithCompany = "Blank is left out. Maconomy shows the same address on the customer's contact company.";
    private const string ContactPerson = "Contact person · ";
    private const string PickListRule = "Sent when Maconomy's list for this popup holds the value; left out otherwise.";

    /// <summary>
    /// The mapping in words, for the Customer tab: every field of the Client Information tab and of the
    /// client's form, in the order they are asked. Kept beside <see cref="Map"/> so a change to what is
    /// sent is a change to what the page says.
    /// </summary>
    public static MaconomyCardMapping Describe(string container) => new(container, new[]
    {
        new MaconomyFieldMapping(ClientInformation, "Client"),
        new MaconomyFieldMapping(ClientInformation, "Entity Type", EntityTypeField, "Specification 6",
            "The value's Maconomy number, set under Option Sets → REMS Entity Type. A value without one is left out."),
        new MaconomyFieldMapping(ClientInformation, "First Name", FirstNameField, "Remark 27",
            "Individuals. Also part of Name 1."),
        new MaconomyFieldMapping(ClientInformation, "Last Name", LastNameField, "Remark 29",
            "Individuals. Also part of Name 1."),
        new MaconomyFieldMapping(ClientInformation, "Suffix", SuffixField, "Remark 30",
            "Individuals. Also part of Name 1."),
        new MaconomyFieldMapping(ClientInformation, "Client/Entity Name", NameField, "Name 1",
            "An organisation: its legal name. An individual has no such box: Name 1 is \"Last, First Suffix\", from the three above."),
        new MaconomyFieldMapping(ClientInformation, "Client Email Address", EmailField, "E-mail",
            "Checked against Maconomy first: a customer already holding it is linked instead of created."),
        new MaconomyFieldMapping(ClientInformation, "Client Phone Number", PhoneField, "Phone", LeftOutWhenBlank),
        new MaconomyFieldMapping(ClientInformation, "Request type"),
        new MaconomyFieldMapping(ClientInformation, "Industry", IndustryField, "Specification 1",
            "The value's Maconomy number, set under Option Sets → REMS Industry. A value without one is left out."),
        new MaconomyFieldMapping(ClientInformation, "CSE", CseField, "Specification 5 and Account Manager No.",
            "The CSE's Maconomy Number, set on their user record, goes in both. A CSE without one is left out."),
        new MaconomyFieldMapping(ClientInformation, "Attachments", null, "Customer's Documents",
            "Each file is uploaded to the customer once the request is approved and the client is in Maconomy. Where Maconomy filed it is kept on the file.",
            IsDocument: true),

        new MaconomyFieldMapping(FormContact, "First Name", FirstNameField, "Remark 27", SameAsClientInformation),
        new MaconomyFieldMapping(FormContact, "Last Name", LastNameField, "Remark 29", SameAsClientInformation),
        new MaconomyFieldMapping(FormContact, "Suffix", SuffixField, "Remark 30", SameAsClientInformation),
        new MaconomyFieldMapping(FormContact, "Client/Entity Name", NameField, "Name 1", SameAsClientInformation),
        new MaconomyFieldMapping(FormContact, "Email", EmailField, "E-mail", "Locked to the request's address."),
        new MaconomyFieldMapping(FormContact, "Phone Number", PhoneField, "Phone", SameAsClientInformation),
        new MaconomyFieldMapping(FormContact, "Referral Source"),
        new MaconomyFieldMapping(FormContact, "Tell us more"),
        new MaconomyFieldMapping(FormContact, "EIN"),

        new MaconomyFieldMapping(FormPhysical, "Country", PhysicalCountryField, "Country",
            "Outside the United States: the country as selected. It has to be on Maconomy's Country list; the sync stops and says so when it is not."),
        new MaconomyFieldMapping(FormPhysical, "State / Province", PhysicalCountryField, "Country",
            "United States: the state code (FL), which Maconomy's Country list holds. Any other country: not sent."),
        new MaconomyFieldMapping(FormPhysical, "City", PhysicalCityField, "Postal District", SharedWithCompany),
        new MaconomyFieldMapping(FormPhysical, "Address Line 1", PhysicalLine1Field, "Name 2", SharedWithCompany),
        new MaconomyFieldMapping(FormPhysical, "Address Line 2", PhysicalLine2Field, "Name 3", SharedWithCompany),
        new MaconomyFieldMapping(FormPhysical, "Zip Code", PhysicalZipCodeField, "Zip Code", SharedWithCompany),

        new MaconomyFieldMapping(FormMailing, "Mailing address is the same as physical address", null, null,
            "When ticked, the mailing address is kept as a copy of the physical one and goes out in the fields below."),
        new MaconomyFieldMapping(FormMailing, "Country", MailingStateOrCountryField, "Remark 24",
            "Outside the United States: the country as selected."),
        new MaconomyFieldMapping(FormMailing, "State / Province", MailingStateOrCountryField, "Remark 24",
            "United States: the state code (FL). Any other country: not sent."),
        new MaconomyFieldMapping(FormMailing, "City", MailingCityField, "Remark 23", LeftOutWhenBlank),
        new MaconomyFieldMapping(FormMailing, "Address Line 1", MailingLine1Field, "Remark 20", LeftOutWhenBlank),
        new MaconomyFieldMapping(FormMailing, "Address Line 2", MailingLine2Field, "Remark 21", LeftOutWhenBlank),
        new MaconomyFieldMapping(FormMailing, "Zip Code", MailingZipCodeField, "Remark 25", LeftOutWhenBlank),

        new MaconomyFieldMapping(FormBilling, "First Name"),
        new MaconomyFieldMapping(FormBilling, "Last Name"),
        new MaconomyFieldMapping(FormBilling, "Suffix"),
        new MaconomyFieldMapping(FormBilling, "Email Address"),
        new MaconomyFieldMapping(FormBilling, "Phone Number"),
        new MaconomyFieldMapping(FormBilling, "Country"),
        new MaconomyFieldMapping(FormBilling, "State / Province"),
        new MaconomyFieldMapping(FormBilling, "City"),
        new MaconomyFieldMapping(FormBilling, "Address Line 1"),
        new MaconomyFieldMapping(FormBilling, "Address Line 2"),
        new MaconomyFieldMapping(FormBilling, "Zip Code"),

        new MaconomyFieldMapping(FormIndividuals, "Each individual", MaconomyContactPersonDataMapping.KindField, ContactPerson + "Remarks 2",
            $"A contact person under the customer's contact company, marked \"{MaconomyContactPersonDataMapping.Individual}\"."),
        new MaconomyFieldMapping(FormIndividuals, "Type", MaconomyContactPersonDataMapping.TypeField, ContactPerson + "Popup 2", PickListRule),
        new MaconomyFieldMapping(FormIndividuals, "Filing Type", MaconomyContactPersonDataMapping.FilingTypeField, ContactPerson + "Popup 3", PickListRule),
        new MaconomyFieldMapping(FormIndividuals, "Is this child a minor?", MaconomyContactPersonDataMapping.MinorField, ContactPerson + "Popup 4", PickListRule),
        new MaconomyFieldMapping(FormIndividuals, "First Name", MaconomyContactPersonDataMapping.FirstNameField, ContactPerson + "First Name", LeftOutWhenBlank),
        new MaconomyFieldMapping(FormIndividuals, "Last Name", MaconomyContactPersonDataMapping.LastNameField, ContactPerson + "Last Name", LeftOutWhenBlank),
        new MaconomyFieldMapping(FormIndividuals, "Suffix", MaconomyContactPersonDataMapping.SuffixField, ContactPerson + "Initials", LeftOutWhenBlank),
        new MaconomyFieldMapping(FormIndividuals, "Email Address", MaconomyContactPersonDataMapping.EmailField, ContactPerson + "E-mail", LeftOutWhenBlank),
        new MaconomyFieldMapping(FormIndividuals, "Phone Number", MaconomyContactPersonDataMapping.PhoneField, ContactPerson + "Phone", LeftOutWhenBlank),
        new MaconomyFieldMapping(FormIndividuals, "Billing Preference", MaconomyContactPersonDataMapping.BillingPreferenceField, ContactPerson + "Popup 5", PickListRule),

        new MaconomyFieldMapping(FormContract, "Contract Start Date"),
        new MaconomyFieldMapping(FormContract, "Contract End Date"),
        new MaconomyFieldMapping(FormContract, "Original Term"),
        new MaconomyFieldMapping(FormContract, "Renewal Terms"),
        new MaconomyFieldMapping(FormContract, "Purchase Order Start Date", null, null, "Not on the customer: it goes on the job card, as Date 4."),
        new MaconomyFieldMapping(FormContract, "Purchase Order End Date", null, null, "Not on the customer: it goes on the job card, as Date 5."),

        new MaconomyFieldMapping(FormContacts, "Each contact", MaconomyContactPersonDataMapping.KindField, ContactPerson + "Remarks 2",
            $"A contact person under the customer's contact company, marked by role: \"{MaconomyContactPersonDataMapping.PrimaryClientContact}\", \"{MaconomyContactPersonDataMapping.FinancialContact}\", \"{MaconomyContactPersonDataMapping.OtherContact}\" or \"{MaconomyContactPersonDataMapping.TrustAndEstates}\". Any other role is not sent."),
        new MaconomyFieldMapping(FormContacts, "First Name", MaconomyContactPersonDataMapping.FirstNameField, ContactPerson + "First Name", LeftOutWhenBlank),
        new MaconomyFieldMapping(FormContacts, "Last Name", MaconomyContactPersonDataMapping.LastNameField, ContactPerson + "Last Name", LeftOutWhenBlank),
        new MaconomyFieldMapping(FormContacts, "Suffix", MaconomyContactPersonDataMapping.SuffixField, ContactPerson + "Initials", LeftOutWhenBlank),
        new MaconomyFieldMapping(FormContacts, "Email", MaconomyContactPersonDataMapping.EmailField, ContactPerson + "E-mail", LeftOutWhenBlank),
        new MaconomyFieldMapping(FormContacts, "Phone Number", MaconomyContactPersonDataMapping.PhoneField, ContactPerson + "Phone", LeftOutWhenBlank),

        new MaconomyFieldMapping(FormEntities, "Client/Entity Name"),
        new MaconomyFieldMapping(FormEntities, "Email Address"),
        new MaconomyFieldMapping(FormEntities, "Phone Number"),

        new MaconomyFieldMapping(NotOnAForm, "Middle Name", MiddleNameField, "Remark 28",
            "Individuals. Sent when the person's record holds one; no form asks for it."),
        new MaconomyFieldMapping(NotOnAForm, "Customer number", NumberField, "Customer No. and Contact Company No.",
            "The portal picks it: one more than the highest all-digit contact company number in Maconomy. The contact company is created first under it, with the name, physical address, email and phone; the customer follows under the same number, and it is kept on the client.",
            ReadBack: true),
    });

    private static void Put(IDictionary<string, string?> values, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[field] = value.Trim();
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Sent(MaconomyCardWrite write, string field)
        => write.Values.TryGetValue(field, out var value) ? value : null;

    private static string Value(IReadOnlyDictionary<string, string?> record, string field)
        => record.TryGetValue(field, out var value) ? (value ?? string.Empty).Trim() : string.Empty;

    private static string? Optional(IReadOnlyDictionary<string, string?> record, string field)
        => Value(record, field) is { Length: > 0 } value ? value : null;
}
