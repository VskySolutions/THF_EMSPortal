using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Integrations.Maconomy.Mapping;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Integrations.Maconomy;

/// <summary>
/// Default <see cref="IMaconomyCustomerWriter"/>: the duplicate check on the customer card, through the
/// session manager, on the field names <see cref="MaconomyCustomerDataMapping"/> holds.
/// </summary>
public sealed class MaconomyCustomerWriter : IMaconomyCustomerWriter
{
    private const int EmailMatchesMax = 50;
    private const int CompanyNumbersMax = 1_000_000;
    private const int FreeNumberTries = 25;

    private readonly IMaconomySessionManager _session;
    private readonly IMaconomyGateway _gateway;

    public MaconomyCustomerWriter(IMaconomySessionManager session, IMaconomyGateway gateway)
    {
        _session = session;
        _gateway = gateway;
    }

    public async Task<IReadOnlyList<MaconomyClientDetails>> FindByEmailAsync(
        MaconomyConnection connection, string email, CancellationToken cancellationToken = default)
    {
        var term = MaconomyCustomerService.Sanitize(email);
        if (term.Length == 0)
        {
            return Array.Empty<MaconomyClientDetails>();
        }

        // `like` without a wildcard is equality any case; an underscore in the address may read as a
        // pattern, so the exact comparison is made again in memory. Read from the container every other
        // read uses, not the one the create writes to: the two are the same card, and this one is proven.
        var request = new MaconomyFilterRequest(
            $"{MaconomyCustomerDataMapping.EmailField} like '{term}'",
            new[] { MaconomyCustomerDataMapping.NumberField, MaconomyCustomerDataMapping.NameField, MaconomyCustomerDataMapping.EmailField, MaconomyCustomerDataMapping.PhoneField },
            EmailMatchesMax);
        var result = await _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.FilterAsync(
                new MaconomyEndpoint(connection.BaseUrl, connection.InstanceCode), token, MaconomyCustomerService.CustomerContainer, request, ct),
            cancellationToken);

        return result.Records
            .Select(r => new MaconomyClientDetails(
                Value(r, MaconomyCustomerDataMapping.NumberField), Value(r, MaconomyCustomerDataMapping.NameField), null,
                Optional(r, MaconomyCustomerDataMapping.EmailField), Optional(r, MaconomyCustomerDataMapping.PhoneField)))
            .Where(c => c.Number.Length > 0 && string.Equals(c.Email?.Trim(), term, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<string> NextContactCompanyNumberAsync(MaconomyConnection connection, CancellationToken cancellationToken = default)
    {
        var endpoint = new MaconomyEndpoint(connection.BaseUrl, connection.InstanceCode);
        // Every company number in one read: the filter compares numbers as text, so the highest cannot be asked for.
        var companies = await _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.FilterAsync(
                endpoint, token, MaconomyContactCompanyDataMapping.Container,
                new MaconomyFilterRequest(string.Empty, new[] { MaconomyContactCompanyDataMapping.NumberField }, CompanyNumbersMax), ct),
            cancellationToken);
        var next = MaconomyContactCompanyDataMapping.NextNumber(
            companies.Records.Select(r => r.GetValueOrDefault(MaconomyContactCompanyDataMapping.NumberField)));
        for (var tries = 0; tries < FreeNumberTries; tries++, next++)
        {
            var number = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var held = await _session.InvokeAsync(
                connection,
                (token, ct) => _gateway.FilterAsync(
                    endpoint, token, MaconomyCustomerService.CustomerContainer,
                    new MaconomyFilterRequest($"{MaconomyCustomerDataMapping.NumberField} = '{number}'", new[] { MaconomyCustomerDataMapping.NumberField }, 1), ct),
                cancellationToken);
            if (held.Records.Count == 0)
            {
                return number;
            }
        }
        throw new MaconomyException(
            MaconomyFailure.Rejected, $"No free number was found for a new contact company near {next}. Ask your Maconomy administrator which number to use next.");
    }

    private static string Value(IReadOnlyDictionary<string, string?> record, string field)
        => record.TryGetValue(field, out var value) ? (value ?? string.Empty).Trim() : string.Empty;

    private static string? Optional(IReadOnlyDictionary<string, string?> record, string field)
        => Value(record, field) is { Length: > 0 } value ? value : null;
}
