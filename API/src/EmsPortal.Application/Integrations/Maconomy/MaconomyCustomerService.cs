using System.Globalization;
using System.Text.RegularExpressions;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
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

    // Only what is shown or filled in: every extra field is more for Maconomy to send on each call.
    private static readonly string[] Fields =
    {
        "customernumber", "name1", "specification6name", "specification1name", "electronicmailaddress", "telephone",
    };

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

    public async Task<MaconomyCustomerSearch> SearchAsync(
        Guid tenantId, string? search, int? limit, CancellationToken cancellationToken = default)
    {
        var connection = await RequireConnectionAsync(tenantId, cancellationToken);

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

        var options = result.Records.Select(ToOption).Where(option => option.Value.Length > 0).ToList();
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
        foreach (var chunk in wanted.Chunk(Math.Clamp(_options.NumbersPerCall, 1, NumbersPerCallMax)))
        {
            // Maconomy's query language has no "in"; a chain of "or" is one call all the same.
            var restriction = string.Join(" or ", chunk.Select(number => $"customernumber = '{number}'"));
            var result = await FilterAsync(connection, restriction, chunk.Length, cancellationToken);
            foreach (var customer in result.Records.Select(ToDetails).Where(c => c.Number.Length > 0))
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
        var cap = Math.Max(1, _options.PreloadMaxRows);
        var result = await FilterAsync(connection, "customernumber like '*'", cap, cancellationToken);
        var customers = result.Records.Select(ToDetails).Where(c => c.Number.Length > 0).ToList();
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

    // THF's Maconomy holds the customer's entity type in specification 6, as a number. The codes are the
    // REMS.EntityType option set's; a number not listed is shown as Maconomy sent it.
    private static (string Code, string Name)? EntityTypeOf(string specification6) => specification6 switch
    {
        "1" => ("individual", "Individual"),
        "2" => ("government", "Government"),
        "3" => ("not_for_profit", "Not-for-Profit"),
        "4" => ("insurance", "Insurance"),
        "5" => ("commercial", "Commercial"),
        "6" => ("trust_estate", "Trust and Estate"),
        _ => null,
    };

    /// <summary>The specification 6 number a REMS entity type code is filed under, or null for a code Maconomy has no number for.</summary>
    public static string? EntityTypeNumberOf(string? entityTypeCode)
    {
        var code = (entityTypeCode ?? string.Empty).Trim();
        if (code.Length == 0)
        {
            return null;
        }
        foreach (var number in new[] { "1", "2", "3", "4", "5", "6" })
        {
            if (EntityTypeOf(number) is { } type && string.Equals(type.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return number;
            }
        }
        return null;
    }

    /// <summary>The specification 1 number a REMS industry code is filed under, or null for a code Maconomy has no number for.</summary>
    public static string? IndustryNumberOf(string? industryCode)
    {
        var code = (industryCode ?? string.Empty).Trim();
        return code.Length == 0
            ? null
            : Industries.FirstOrDefault(i => string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase)).Number;
    }

    // Specification 1 is the customer's industry. THF numbers the values 17 to 38; the portal fills the
    // picker with the REMS.Industry code each stands for. Matched by the number when the value starts
    // with one ("17", "17-Manufacturing"), by the label otherwise.
    private static readonly (string Number, string Label, string Code)[] Industries =
    {
        ("17", "Manufacturing", "manufacturing"),
        ("18", "Professional Service Firms", "professional_service_firms"),
        ("19", "Real Estate", "real_estate"),
        ("20", "Retail", "retail"),
        ("21", "Health Care", "health_care"),
        ("22", "Oil & Gas Distribution", "oil_gas_distribution"),
        ("23", "Wholesale", "wholesale"),
        ("24", "Technology", "technology"),
        ("25", "State government", "state_government"),
        ("26", "Local government", "local_government"),
        ("27", "Federal government", "federal_government"),
        ("28", "Educational Institutions", "educational_institutions"),
        ("29", "Insurance - Health", "insurance_health"),
        ("30", "Insurance - Property and Casualty", "insurance_property_casualty"),
        ("31", "Insurance - Life", "insurance_life"),
        ("32", "Insurance - Other", "insurance_other"),
        ("33", "Trade associations", "trade_associations"),
        ("34", "Charitable organizations or Foundations", "charitable_organizations_foundations"),
        ("35", "Other Not-for-Profit", "other_not_for_profit"),
        ("36", "Individual", "individual"),
        ("37", "Distribution", "distribution"),
        ("38", "Trust / Estate", "trust_estate"),
    };

    /// <summary>The REMS.Industry code a specification 1 value stands for, or null when it stands for none.</summary>
    public static string? IndustryOf(string? specification1)
    {
        var text = (specification1 ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }
        var number = new string(text.TakeWhile(char.IsDigit).ToArray());
        if (number.Length > 0)
        {
            return Industries.FirstOrDefault(i => i.Number == number).Code;
        }
        var key = Letters(text);
        return Industries.FirstOrDefault(i => Letters(i.Label) == key).Code;
    }

    // Letters and digits alone, lower-cased: "Insurance - Health" and "insurance health" are one value.
    private static string Letters(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

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

    /// <summary>"number - name (specification 6 name)" to read, blanks left out; the number to store.</summary>
    private static MaconomyCustomerOption ToOption(IReadOnlyDictionary<string, string?> record)
    {
        var number = Value(record, "customernumber");
        var name = Value(record, "name1");
        var specification6 = Value(record, "specification6name");
        var entityType = EntityTypeOf(specification6);
        var specification6Name = entityType?.Name ?? specification6;
        var text = string.Join(" - ", new[] { number, name }.Where(s => s.Length > 0));
        if (specification6Name.Length > 0)
        {
            text = $"{text} ({specification6Name})";
        }
        return new MaconomyCustomerOption(
            text, number, specification6Name.Length > 0 ? specification6Name : null, entityType?.Code,
            Optional(record, "electronicmailaddress"), Optional(record, "telephone"), name,
            IndustryOf(Value(record, "specification1name")));
    }

    private static MaconomyClientDetails ToDetails(IReadOnlyDictionary<string, string?> record)
        => new(
            Value(record, "customernumber"),
            Value(record, "name1"),
            EntityTypeOf(Value(record, "specification6name"))?.Code,
            Optional(record, "electronicmailaddress"),
            Optional(record, "telephone"),
            IndustryOf(Value(record, "specification1name")));

    private static string Value(IReadOnlyDictionary<string, string?> record, string field)
        => record.TryGetValue(field, out var value) ? (value ?? string.Empty).Trim() : string.Empty;

    private static string? Optional(IReadOnlyDictionary<string, string?> record, string field)
        => Value(record, field) is { Length: > 0 } value ? value : null;
}
