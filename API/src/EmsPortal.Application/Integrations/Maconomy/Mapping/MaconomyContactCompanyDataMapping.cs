using EmsPortal.Application.Abstractions.Integrations.Maconomy;

namespace EmsPortal.Application.Integrations.Maconomy.Mapping;

/// <summary>
/// THE mapping of a client to Maconomy's contact company, which has to exist before its customer: the
/// instance hands out no number for one, and refuses a customer that names a company it does not hold
/// (live, 13 Nov 2026). The company and the customer share a number, a name, an address, an email and a
/// phone, so the company is written from what the customer mapping produced.
/// </summary>
public static class MaconomyContactCompanyDataMapping
{
    public const string Container = MaconomyContactPersonDataMapping.Container;
    public const string NumberField = MaconomyContactPersonDataMapping.CompanyNumberField;

    // The same names as on the customer card: Maconomy shows these on both records.
    private static readonly string[] Shared =
    {
        MaconomyCustomerDataMapping.NameField,
        MaconomyCustomerDataMapping.PhysicalLine1Field, MaconomyCustomerDataMapping.PhysicalLine2Field,
        MaconomyCustomerDataMapping.PhysicalCityField, MaconomyCustomerDataMapping.PhysicalCountryField,
        MaconomyCustomerDataMapping.PhysicalZipCodeField,
        MaconomyCustomerDataMapping.EmailField, MaconomyCustomerDataMapping.PhoneField,
    };

    public static readonly string[] Fields = new[] { NumberField }.Concat(Shared).ToArray();

    /// <summary>The company to create under <paramref name="number"/>, from the customer about to follow it.</summary>
    public static MaconomyCardWrite Map(MaconomyCardWrite customer, string number)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [NumberField] = number };
        foreach (var field in Shared)
        {
            if (customer.Values.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                values[field] = value;
            }
        }
        return new MaconomyCardWrite(Container, Fields, values, InitValues: null, KeyField: NumberField);
    }

    /// <summary>
    /// "Latest + 1" (user, 13 Nov 2026): one more than the highest all-digit contact company number.
    /// Numbers with a letter suffix (20280A) are sub-entities of another company and are not counted.
    /// </summary>
    public static long NextNumber(IEnumerable<string?> companyNumbers)
        => companyNumbers
            .Select(n => (n ?? string.Empty).Trim())
            .Where(n => n.Length is > 0 and <= 15 && n.All(char.IsAsciiDigit))
            .Select(long.Parse)
            .DefaultIfEmpty(0)
            .Max() + 1;
}
