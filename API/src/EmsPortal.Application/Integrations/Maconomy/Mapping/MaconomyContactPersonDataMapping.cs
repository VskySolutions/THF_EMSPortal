using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Integrations.Maconomy.Mapping;

/// <summary>
/// THE mapping of a client's contacts to Maconomy's contact persons. Maconomy makes the contact company
/// itself when the customer is created, under the customer's number, sharing its name and address; the
/// portal only adds the people under it. Field names are the instance's own (read 13 Nov 2026).
/// </summary>
public static class MaconomyContactPersonDataMapping
{
    /// <summary>The contact company card with its table of contact persons. Maconomy spells it this way.</summary>
    public const string Container = "contactcompanys";

    public const string CompanyNumberField = "contactcompanynumber";
    public const string CompanyNameField = "name1";

    public const string KindField = "remark2";
    public const string FirstNameField = "firstname";
    public const string LastNameField = "lastname";
    public const string SuffixField = "initials";
    public const string EmailField = "electronicmailaddress";
    public const string PhoneField = "telephone";
    public const string TypeField = "contactpersonpopup2";
    public const string FilingTypeField = "contactpersonpopup3";
    public const string MinorField = "contactpersonpopup4";
    public const string BillingPreferenceField = "contactpersonpopup5";

    public const string TypeList = "popup_contactpersonpopup2type";
    public const string FilingTypeList = "popup_contactpersonpopup3type";
    public const string MinorList = "popup_contactpersonpopup4type";
    public const string BillingPreferenceList = "popup_contactpersonpopup5type";

    // What remark2 says a person is (user, 13 Nov 2026).
    public const string PrimaryClientContact = "Primary Client Contact";
    public const string FinancialContact = "Financial Contact";
    public const string OtherContact = "Other Contact";
    public const string TrustAndEstates = "Trust And Estates";
    public const string Individual = "Spouse And More Individuals";

    public static readonly string[] CardFields = { CompanyNumberField, CompanyNameField };

    public static readonly string[] RowFields =
    {
        KindField, FirstNameField, LastNameField, SuffixField, EmailField, PhoneField,
        TypeField, FilingTypeField, MinorField, BillingPreferenceField,
    };

    /// <summary>What makes two rows the same person, so a second attempt adds nobody twice.</summary>
    public static readonly string[] MatchFields = { KindField, FirstNameField, LastNameField, EmailField };

    // REMS.ContactRole → remark2. A role not named here is not sent.
    private static readonly Dictionary<string, string> KindByRole = new(StringComparer.OrdinalIgnoreCase)
    {
        ["primary_client_contact"] = PrimaryClientContact,
        ["financial_contact"] = FinancialContact,
        ["other_contact"] = OtherContact,
        ["trust_estate_contact"] = TrustAndEstates,
    };

    // REMS.RelationType values that are people; a related entity is not a contact person.
    private static readonly HashSet<string> IndividualRelations = new(StringComparer.OrdinalIgnoreCase) { "spouse", "child", "other" };

    /// <summary>The four pick lists an individual's row draws on, as Maconomy holds them now.</summary>
    public sealed record PickLists(MaconomyPickList Type, MaconomyPickList FilingType, MaconomyPickList Minor, MaconomyPickList BillingPreference);

    /// <summary>
    /// The rows to add under the client's contact company: one per contact in a mapped role, one per spouse
    /// or other individual. A pick-list value Maconomy's list does not hold is left out (user, 13 Nov 2026).
    /// </summary>
    public static MaconomyTableRowsAdd Map(Client client, string companyNumber, PickLists lists)
    {
        var rows = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var contact in client.Contacts.Where(c => !c.Deleted && c.Person is not null).OrderBy(c => c.CreatedOnUtc))
        {
            if (contact.ContactRole?.Value is { } role && KindByRole.TryGetValue(role, out var kind) && RowOf(kind, contact.Person!) is { } row)
            {
                rows.Add(row);
            }
        }
        foreach (var relation in client.Relationships.Where(r => !r.Deleted).OrderBy(r => r.CreatedOnUtc))
        {
            if (relation.RelationType?.Value is not { } type || !IndividualRelations.Contains(type)
                || relation.RelatedClient?.Person is not { } person || RowOf(Individual, person) is not { } row)
            {
                continue;
            }
            Put(row, TypeField, lists.Type.ValueOf(relation.RelationType.Label));
            Put(row, FilingTypeField, lists.FilingType.ValueOf(relation.FilingType?.Label));
            Put(row, MinorField, lists.Minor.ValueOf(relation.IsMinor ? "Yes" : "No"));
            Put(row, BillingPreferenceField, lists.BillingPreference.ValueOf(relation.BillingPreference?.Label));
            rows.Add(row);
        }
        return new MaconomyTableRowsAdd(Container, CompanyNumberField, companyNumber, CardFields, RowFields, rows, MatchFields);
    }

    // A person with no name at all is not a row.
    private static Dictionary<string, string?>? RowOf(string kind, Person person)
    {
        var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [KindField] = kind };
        Put(row, FirstNameField, person.FirstName);
        Put(row, LastNameField, person.LastName);
        if (row.Count == 1)
        {
            return null;
        }
        Put(row, SuffixField, person.Suffix);
        Put(row, EmailField, person.PrimaryEmail);
        Put(row, PhoneField, person.MobileNumber);
        return row;
    }

    private static void Put(IDictionary<string, string?> values, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[field] = value.Trim();
        }
    }
}
