using System.Globalization;
using System.Text.RegularExpressions;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Integrations.Maconomy.Mapping;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EmsPortal.Application.Integrations.Maconomy;

/// <summary>
/// Default <see cref="IMaconomyCustomerService"/>: filters the <c>customercard</c> container through the
/// session manager, so the token takes care of itself, and shapes the records for a dropdown or as the
/// details of a client.
/// </summary>
public sealed class MaconomyCustomerService : IMaconomyCustomerService
{
    /// <summary>The Maconomy container the customer search filters.</summary>
    public const string CustomerContainer = "customercard";

    private const int SearchMinLength = 2;
    private const int SearchMaxLength = 100;
    private const int NumbersPerCallMax = 200;
    private const int MaxWords = 6;
    private static readonly char[] WordBreaks = { ' ', ',' };

    // Only what is shown or filled in: every extra field is more for Maconomy to send on each call. The
    // names are the mapping's, so a field renamed for the write is renamed for the read in the same edit.
    private static readonly string[] Fields = MaconomyCustomerDataMapping.Fields;

    // The restriction is written in Maconomy's query language with the search text inside a quoted
    // literal, so a quote — or a backslash, or a control character — would break out of it.
    private static readonly Regex Unsafe = new(@"[\p{C}'\\]", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    // Characters Maconomy may read as a pattern. A term holding one is never narrowed in memory.
    private static readonly char[] Wildcards = { '*', '?', '%', '_', '[', ']' };

    private readonly IMaconomyConnectionRepository _connections;
    private readonly IMaconomySessionManager _session;
    private readonly IMaconomyGateway _gateway;
    private readonly IMemoryCache _cache;
    private readonly IMaconomyNumberMaps _numbers;
    private readonly MaconomyOptions _options;

    public MaconomyCustomerService(
        IMaconomyConnectionRepository connections,
        IMaconomySessionManager session,
        IMaconomyGateway gateway,
        IMemoryCache cache,
        IMaconomyNumberMaps numbers,
        IOptions<MaconomyOptions> options)
    {
        _connections = connections;
        _session = session;
        _gateway = gateway;
        _cache = cache;
        _numbers = numbers;
        _options = options.Value;
    }

    public async Task<MaconomyCustomerSearch> SearchAsync(
        Guid tenantId, string? search, int? limit, CancellationToken cancellationToken = default)
    {
        var connection = await RequireConnectionAsync(tenantId, cancellationToken);
        var map = await _numbers.GetAsync(tenantId, cancellationToken);

        var term = Sanitize(search);
        if (term.Length < SearchMinLength)
        {
            return new MaconomyCustomerSearch(Array.Empty<MaconomyCustomerOption>(), Complete: true);
        }
        var rows = Math.Clamp(limit ?? connection.DefaultLimit, 1, Math.Max(1, _options.MaxLimit));

        // Keystrokes arrive faster than Maconomy answers, and the same search a few seconds apart is the
        // same list.
        var cacheKey = SearchKey(tenantId, rows, term);
        if (_cache.TryGetValue(cacheKey, out MaconomyCustomerSearch? cached) && cached is not null)
        {
            return cached;
        }
        if (NarrowFromShorterSearch(tenantId, rows, term) is { } narrowed)
        {
            Remember(cacheKey, narrowed);
            return narrowed;
        }

        // The name is matched word by word, in any order: Maconomy files a person as "SMITH, JOHN", and
        // nobody looking for them types the comma.
        var words = Words(term);
        var name = words.Length == 0
            ? $"name1 like '*{term}*'"
            : string.Join(" and ", words.Select(word => $"name1 like '*{word}*'"));
        var restriction =
            $"({name}) or customernumber like '*{term}*' "
            + $"or electronicmailaddress like '*{term}*' or telephone like '*{term}*'";
        var result = await FilterAsync(connection, restriction, rows, cancellationToken);

        var options = result.Records.Select(r => ToOption(r, map)).Where(option => option.Value.Length > 0).ToList();
        var found = new MaconomyCustomerSearch(options, Complete: result.Records.Count < rows);
        Remember(cacheKey, found);
        return found;
    }

    public async Task<IReadOnlyDictionary<string, MaconomyClientDetails>> GetByNumbersAsync(
        Guid tenantId, IReadOnlyCollection<string> numbers, CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, MaconomyClientDetails>(StringComparer.OrdinalIgnoreCase);
        var wanted = numbers
            .Select(Sanitize)
            .Where(number => number.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (wanted.Count == 0)
        {
            return found;
        }

        var connection = await RequireConnectionAsync(tenantId, cancellationToken);
        var map = await _numbers.GetAsync(tenantId, cancellationToken);
        foreach (var chunk in wanted.Chunk(Math.Clamp(_options.NumbersPerCall, 1, NumbersPerCallMax)))
        {
            // Maconomy's query language has no "in"; a chain of "or" is one call all the same.
            var restriction = string.Join(" or ", chunk.Select(number => $"customernumber = '{number}'"));
            var result = await FilterAsync(connection, restriction, chunk.Length, cancellationToken);
            foreach (var customer in result.Records.Select(r => ToDetails(r, map)).Where(c => c.Number.Length > 0))
            {
                found[customer.Number] = customer;
            }
        }
        return found;
    }

    public async Task<(IReadOnlyList<MaconomyClientDetails> Customers, bool Complete)> ListAllAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        var connection = await RequireConnectionAsync(tenantId, cancellationToken);
        var map = await _numbers.GetAsync(tenantId, cancellationToken);
        var cap = Math.Max(1, _options.PreloadMaxRows);
        var result = await FilterAsync(connection, "customernumber like '*'", cap, cancellationToken);
        var customers = result.Records.Select(r => ToDetails(r, map)).Where(c => c.Number.Length > 0).ToList();
        return (customers, result.Records.Count < cap);
    }

    /// <summary>The search as it may appear inside the restriction: trimmed, single-spaced, quote-free and no longer than the cap.</summary>
    public static string Sanitize(string? search)
    {
        var text = Whitespace.Replace(Unsafe.Replace(search ?? string.Empty, string.Empty), " ").Trim();
        return text.Length <= SearchMaxLength ? text : text[..SearchMaxLength].TrimEnd();
    }

    /// <summary>Whether the customer's name, number, email or telephone contains the term, read the way Maconomy's own search reads it.</summary>
    public static bool Matches(MaconomyClientDetails customer, string term)
        => NameMatches(customer.Name, term) || Contains(customer.Number, term)
            || Contains(customer.Email, term) || Contains(customer.Phone, term);

    /// <summary>Whether a name holds every word of the term, in any order.</summary>
    public static bool NameMatches(string? name, string term)
    {
        var words = Words(term);
        return words.Length == 0 ? Contains(name, term) : words.All(word => Contains(name, word));
    }

    /// <summary>"Contains", any case and accents aside.</summary>
    public static bool Contains(string? value, string term)
        => !string.IsNullOrEmpty(value)
            && CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                value, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private async Task<MaconomyConnection> RequireConnectionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken)
            ?? throw new MaconomyException(MaconomyFailure.NotConfigured, "Maconomy is not connected for this tenant.");
        if (!connection.IsEnabled)
        {
            throw new MaconomyException(MaconomyFailure.NotConfigured, "The Maconomy connection is switched off for this tenant.");
        }
        return connection;
    }

    private Task<MaconomyFilterResult> FilterAsync(
        MaconomyConnection connection, string restriction, int limit, CancellationToken cancellationToken)
    {
        var endpoint = new MaconomyEndpoint(connection.BaseUrl, connection.InstanceCode);
        var request = new MaconomyFilterRequest(restriction, Fields, limit);
        return _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.FilterAsync(endpoint, token, CustomerContainer, request, ct),
            cancellationToken);
    }

    // A longer term can only match rows a shorter one already matched. Where the shorter search came back
    // whole, the longer one is answered from it without asking Maconomy again.
    private MaconomyCustomerSearch? NarrowFromShorterSearch(Guid tenantId, int rows, string term)
    {
        if (_options.SearchCacheSeconds <= 0 || term.IndexOfAny(Wildcards) >= 0)
        {
            return null;
        }
        for (var length = term.Length - 1; length >= SearchMinLength; length--)
        {
            var shorter = term[..length];
            if (shorter.EndsWith(' ')
                || !_cache.TryGetValue(SearchKey(tenantId, rows, shorter), out MaconomyCustomerSearch? held)
                || held is not { Complete: true })
            {
                continue;
            }

            var kept = held.Customers
                .Where(o => NameMatches(o.Name, term) || Contains(o.Value, term)
                    || Contains(o.EmailAddress, term) || Contains(o.PhoneNumber, term))
                .ToList();
            return new MaconomyCustomerSearch(kept, Complete: true);
        }
        return null;
    }

    private void Remember(string cacheKey, MaconomyCustomerSearch found)
    {
        if (_options.SearchCacheSeconds > 0)
        {
            _cache.Set(cacheKey, found, TimeSpan.FromSeconds(_options.SearchCacheSeconds));
        }
    }

    private static string[] Words(string term)
        => term.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(MaxWords)
            .ToArray();

    private static string SearchKey(Guid tenantId, int rows, string term)
        => $"maconomy:customers:{tenantId:N}:{rows}:{term.ToLowerInvariant()}";

    /// <summary>"number - name (specification 6 name)" to read, blanks left out; the number to store. Public for the mapping check harness.</summary>
    public static MaconomyCustomerOption ToOption(IReadOnlyDictionary<string, string?> record, MaconomyNumberMap numbers)
    {
        var number = Value(record, MaconomyCustomerDataMapping.NumberField);
        var name = Value(record, MaconomyCustomerDataMapping.NameField);
        var specification6 = Value(record, MaconomyCustomerDataMapping.EntityTypeField);
        var entityType = numbers.EntityTypeOf(specification6);
        var specification6Name = entityType?.Label ?? specification6;
        var text = string.Join(" - ", new[] { number, name }.Where(s => s.Length > 0));
        if (specification6Name.Length > 0)
        {
            text = $"{text} ({specification6Name})";
        }
        return new MaconomyCustomerOption(
            text, number, specification6Name.Length > 0 ? specification6Name : null, entityType?.Code,
            Optional(record, MaconomyCustomerDataMapping.EmailField), Optional(record, MaconomyCustomerDataMapping.PhoneField), name,
            numbers.IndustryOf(Value(record, MaconomyCustomerDataMapping.IndustryField))?.Code);
    }

    public static MaconomyClientDetails ToDetails(IReadOnlyDictionary<string, string?> record, MaconomyNumberMap numbers)
        => new(
            Value(record, MaconomyCustomerDataMapping.NumberField),
            Value(record, MaconomyCustomerDataMapping.NameField),
            numbers.EntityTypeOf(Value(record, MaconomyCustomerDataMapping.EntityTypeField))?.Code,
            Optional(record, MaconomyCustomerDataMapping.EmailField),
            Optional(record, MaconomyCustomerDataMapping.PhoneField),
            numbers.IndustryOf(Value(record, MaconomyCustomerDataMapping.IndustryField))?.Code);

    private static string Value(IReadOnlyDictionary<string, string?> record, string field)
        => record.TryGetValue(field, out var value) ? (value ?? string.Empty).Trim() : string.Empty;

    private static string? Optional(IReadOnlyDictionary<string, string?> record, string field)
        => Value(record, field) is { Length: > 0 } value ? value : null;
}
