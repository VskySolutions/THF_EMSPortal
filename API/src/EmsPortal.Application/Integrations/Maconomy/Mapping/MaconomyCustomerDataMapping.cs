using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Integrations.Maconomy.Mapping;

/// <summary>
/// THE mapping of a client to Maconomy's customer card. Everything that names a customer card field, or
/// decides what goes in it, is in this file and nowhere else: change what the portal sends here.
/// The field names are the ones the instance accepted on 22 Oct 2026 (the specification popups ARE the
/// <c>…name</c> fields; the instance refused <c>specification6</c>). The container is the one thing that
/// differs per instance, so the sync service overrides it from configuration.
/// </summary>
public static class MaconomyCustomerDataMapping
{
    public const string DefaultContainer = "dpl:customercard";

    public const string NumberField = "customernumber";
    public const string NameField = "name1";
    public const string EmailField = "electronicmailaddress";
    public const string PhoneField = "telephone";
    public const string EntityTypeField = "specification6name";
    public const string IndustryField = "specification1name";

    /// <summary>The card fields every read and write names. The number is read back, never sent: Maconomy's series assigns it.</summary>
    public static readonly string[] Fields = { NumberField, NameField, EmailField, PhoneField, EntityTypeField, IndustryField };

    /// <summary>
    /// What the portal posts as a new customer. A blank value is left out, so Maconomy applies its own
    /// default. The email is passed in because the sync has already checked it against Maconomy; the
    /// numbers are the tenant's option items' (the specification 6 number on the entity type, the
    /// specification 1 number on the industry).
    /// </summary>
    public static MaconomyCardWrite Map(Client client, string email, MaconomyNumberMap numbers)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [NameField] = CustomerNameOf(client),
        };
        Put(values, EmailField, email);
        Put(values, PhoneField, client.PrimaryPhone ?? client.Person?.MobileNumber);
        Put(values, EntityTypeField, numbers.EntityTypeNumberOf(client.EntityType?.Value));
        Put(values, IndustryField, numbers.IndustryNumberOf(client.Industry?.Value));
        return new MaconomyCardWrite(DefaultContainer, Fields, values, InitValues: null, KeyField: NumberField);
    }

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

    private static void Put(IDictionary<string, string?> values, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[field] = value.Trim();
        }
    }

    private static string? Sent(MaconomyCardWrite write, string field)
        => write.Values.TryGetValue(field, out var value) ? value : null;

    private static string Value(IReadOnlyDictionary<string, string?> record, string field)
        => record.TryGetValue(field, out var value) ? (value ?? string.Empty).Trim() : string.Empty;

    private static string? Optional(IReadOnlyDictionary<string, string?> record, string field)
        => Value(record, field) is { Length: > 0 } value ? value : null;
}
