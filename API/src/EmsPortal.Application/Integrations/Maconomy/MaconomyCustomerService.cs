using System.Text.RegularExpressions;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EmsPortal.Application.Integrations.Maconomy;

/// <summary>
/// Default <see cref="IMaconomyCustomerService"/>: filters the <c>customercard</c> container on the
/// number and the name, through the session manager so the token takes care of itself, and shapes the
/// records for a dropdown.
/// </summary>
public sealed class MaconomyCustomerService : IMaconomyCustomerService
{
    /// <summary>The Maconomy container the customer search filters.</summary>
    public const string CustomerContainer = "customercard";

    private const int SearchMinLength = 2;
    private const int SearchMaxLength = 100;

    private static readonly string[] Fields = { "customernumber", "name1", "specification6name", "createddate", "createdby" };

    // The restriction is written in Maconomy's query language with the search text inside a quoted
    // literal, so a quote — or a backslash, or a control character — would break out of it.
    private static readonly Regex Unsafe = new(@"[\p{C}'\\]", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly IMaconomyConnectionRepository _connections;
    private readonly IMaconomySessionManager _session;
    private readonly IMaconomyGateway _gateway;
    private readonly IMemoryCache _cache;
    private readonly MaconomyOptions _options;

    public MaconomyCustomerService(
        IMaconomyConnectionRepository connections,
        IMaconomySessionManager session,
        IMaconomyGateway gateway,
        IMemoryCache cache,
        IOptions<MaconomyOptions> options)
    {
        _connections = connections;
        _session = session;
        _gateway = gateway;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaconomyCustomerOption>> SearchAsync(
        Guid tenantId, string? search, int? limit, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken)
            ?? throw new MaconomyException(MaconomyFailure.NotConfigured, "Maconomy is not connected for this tenant.");
        if (!connection.IsEnabled)
        {
            throw new MaconomyException(MaconomyFailure.NotConfigured, "The Maconomy connection is switched off for this tenant.");
        }

        var term = Sanitize(search);
        if (term.Length < SearchMinLength)
        {
            return Array.Empty<MaconomyCustomerOption>();
        }
        var rows = Math.Clamp(limit ?? connection.DefaultLimit, 1, Math.Max(1, _options.MaxLimit));

        // Keystrokes arrive faster than Maconomy answers, and the same search a few seconds apart is the
        // same list.
        var cacheKey = $"maconomy:customers:{tenantId:N}:{rows}:{term.ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<MaconomyCustomerOption>? cached) && cached is not null)
        {
            return cached;
        }

        var endpoint = new MaconomyEndpoint(connection.BaseUrl, connection.InstanceCode);
        var request = new MaconomyFilterRequest($"name1 like '*{term}*' or customernumber like '*{term}*'", Fields, rows);
        var result = await _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.FilterAsync(endpoint, token, CustomerContainer, request, ct),
            cancellationToken);

        IReadOnlyList<MaconomyCustomerOption> options = result.Records
            .Select(ToOption)
            .Where(option => option.Value.Length > 0)
            .ToList();

        if (_options.SearchCacheSeconds > 0)
        {
            _cache.Set(cacheKey, options, TimeSpan.FromSeconds(_options.SearchCacheSeconds));
        }
        return options;
    }

    /// <summary>The search as it may appear inside the restriction: trimmed, single-spaced, quote-free and no longer than the cap.</summary>
    public static string Sanitize(string? search)
    {
        var text = Whitespace.Replace(Unsafe.Replace(search ?? string.Empty, string.Empty), " ").Trim();
        return text.Length <= SearchMaxLength ? text : text[..SearchMaxLength].TrimEnd();
    }

    /// <summary>"number - name (specification 6 name)" to read, blanks left out; the number to store.</summary>
    private static MaconomyCustomerOption ToOption(IReadOnlyDictionary<string, string?> record)
    {
        var number = Value(record, "customernumber");
        var name = Value(record, "name1");
        var specification6Name = Value(record, "specification6name");
        var text = string.Join(" - ", new[] { number, name }.Where(s => s.Length > 0));
        if (specification6Name.Length > 0)
        {
            text = $"{text} ({specification6Name})";
        }
        return new MaconomyCustomerOption(text, number, specification6Name.Length > 0 ? specification6Name : null);
    }

    private static string Value(IReadOnlyDictionary<string, string?> record, string field)
        => record.TryGetValue(field, out var value) ? (value ?? string.Empty).Trim() : string.Empty;
}
