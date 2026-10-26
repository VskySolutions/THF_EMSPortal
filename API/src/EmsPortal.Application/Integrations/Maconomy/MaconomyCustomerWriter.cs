using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace EmsPortal.Application.Integrations.Maconomy;

/// <summary>
/// Default <see cref="IMaconomyCustomerWriter"/>: the duplicate check and the create on the customer
/// card, through the session manager. Field names come from <see cref="MaconomyCustomerCardOptions"/>,
/// because they are confirmed against the instance rather than taken from documentation.
/// </summary>
public sealed class MaconomyCustomerWriter : IMaconomyCustomerWriter
{
    private const int EmailMatchesMax = 50;

    private readonly IMaconomySessionManager _session;
    private readonly IMaconomyGateway _gateway;
    private readonly MaconomyOptions _options;

    public MaconomyCustomerWriter(IMaconomySessionManager session, IMaconomyGateway gateway, IOptions<MaconomyOptions> options)
    {
        _session = session;
        _gateway = gateway;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaconomyClientDetails>> FindByEmailAsync(
        MaconomyConnection connection, string email, CancellationToken cancellationToken = default)
    {
        var term = MaconomyCustomerService.Sanitize(email);
        if (term.Length == 0)
        {
            return Array.Empty<MaconomyClientDetails>();
        }

        var card = _options.CustomerCard;
        // `like` without a wildcard is equality any case; an underscore in the address may read as a
        // pattern, so the exact comparison is made again in memory. Read from the container every other
        // read uses, not the one the create writes to: the two are the same card, and this one is proven.
        var request = new MaconomyFilterRequest(
            $"{card.EmailField} like '{term}'",
            new[] { card.NumberField, card.NameField, card.EmailField, card.PhoneField },
            EmailMatchesMax);
        var result = await _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.FilterAsync(Endpoint(connection), token, MaconomyCustomerService.CustomerContainer, request, ct),
            cancellationToken);

        return result.Records
            .Select(r => new MaconomyClientDetails(
                Value(r, card.NumberField), Value(r, card.NameField), null,
                Optional(r, card.EmailField), Optional(r, card.PhoneField)))
            .Where(c => c.Number.Length > 0 && string.Equals(c.Email?.Trim(), term, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<MaconomyClientDetails> CreateAsync(
        MaconomyConnection connection, MaconomyNewCustomer customer, CancellationToken cancellationToken = default)
    {
        var card = _options.CustomerCard;
        // The number is NOT among the values: Maconomy's series assigns it, and the reply carries it.
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [card.NameField] = customer.Name };
        Put(values, card.EmailField, customer.Email);
        Put(values, card.PhoneField, customer.Phone);
        Put(values, card.EntityTypeField, customer.EntityTypeNumber);
        Put(values, card.IndustryField, customer.IndustryNumber);
        var fields = new[] { card.NumberField, card.NameField, card.EmailField, card.PhoneField, card.EntityTypeField, card.IndustryField }
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var created = await _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.CreateAsync(Endpoint(connection), token, new MaconomyCreateRequest(card.Container, fields, values), ct),
            cancellationToken);

        var number = Value(created.Data, card.NumberField);
        if (number.Length == 0)
        {
            throw new MaconomyException(
                MaconomyFailure.Rejected,
                $"Maconomy created the customer but sent no {card.NumberField} back. Look for a new customer named \"{customer.Name}\" in Maconomy before syncing again.");
        }
        var name = Value(created.Data, card.NameField);
        return new MaconomyClientDetails(
            number,
            name.Length > 0 ? name : customer.Name,
            null,
            Optional(created.Data, card.EmailField) ?? customer.Email,
            Optional(created.Data, card.PhoneField) ?? customer.Phone);
    }

    private static void Put(IDictionary<string, string?> values, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(field) && !string.IsNullOrWhiteSpace(value))
        {
            values[field] = value.Trim();
        }
    }

    private static MaconomyEndpoint Endpoint(MaconomyConnection connection) => new(connection.BaseUrl, connection.InstanceCode);

    private static string Value(IReadOnlyDictionary<string, string?> record, string field)
        => record.TryGetValue(field, out var value) ? (value ?? string.Empty).Trim() : string.Empty;

    private static string? Optional(IReadOnlyDictionary<string, string?> record, string field)
        => Value(record, field) is { Length: > 0 } value ? value : null;
}
