using System.Globalization;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

/// <summary>
/// What a REMS list needs to know about clients that are Maconomy customers. Their rows hold a customer
/// number and no name, email or phone, so SQL can neither search nor order by those: the search is
/// answered here as the customer numbers that match, the order is worked out here over the whole
/// filtered set, and a projected row has its client columns filled in from here. Everything is read
/// from memory (<see cref="IMaconomyClientDirectory"/>) — nothing in this class calls Maconomy.
/// </summary>
internal sealed class MaconomyClientReader
{
    // Close to how the database orders text: any case, by culture rather than by code point.
    private static readonly StringComparer Order = StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true);

    private readonly ITenantContext _tenantContext;
    private readonly IMaconomyClientDirectory _directory;

    public MaconomyClientReader(ITenantContext tenantContext, IMaconomyClientDirectory directory)
    {
        _tenantContext = tenantContext;
        _directory = directory;
    }

    private Guid TenantId => _tenantContext.IsResolved ? _tenantContext.TenantId : Guid.Empty;

    /// <summary>Customer numbers whose Maconomy name or number contains the term.</summary>
    public IReadOnlyList<string> NumbersByName(string term) => _directory.MatchByName(TenantId, term);

    /// <summary>Customer numbers whose Maconomy email or telephone contains the term.</summary>
    public IReadOnlyList<string> NumbersByContact(string term) => _directory.MatchByContact(TenantId, term);

    /// <summary>The client's name as a list shows it: Maconomy's for a Maconomy client, else what the row holds.</summary>
    public string Name(string? maconomyNumber, string stored)
        => string.IsNullOrEmpty(maconomyNumber)
            ? stored
            : _directory.Find(TenantId, maconomyNumber)?.Name ?? Person.MaconomyPlaceholderName(maconomyNumber);

    public string? Email(string? maconomyNumber, string? stored)
        => string.IsNullOrEmpty(maconomyNumber) ? stored : _directory.Find(TenantId, maconomyNumber)?.Email;

    public string? Phone(string? maconomyNumber, string? stored)
        => string.IsNullOrEmpty(maconomyNumber) ? stored : _directory.Find(TenantId, maconomyNumber)?.Phone;

    /// <summary>
    /// A Client filter's options, named and put in order here: a Maconomy client's name is not in the
    /// database to order by.
    /// </summary>
    public IReadOnlyList<RemsClientChoice> Choices(IEnumerable<(Guid Id, string Name, string? MaconomyNumber)> clients)
        => clients
            .Select(c => new RemsClientChoice(c.Id, Name(c.MaconomyNumber, c.Name)))
            .OrderBy(c => c.Name, Order)
            .ToList();

    /// <summary>One page of row ids, in the order of the client's name.</summary>
    public IReadOnlyList<Guid> PageByName<TTie>(
        IEnumerable<ClientSortKey<TTie>> rows, bool descending, int page, int limit)
        => Page(rows, k => Name(k.MaconomyNumber, k.Stored ?? string.Empty), descending, page, limit);

    public IReadOnlyList<Guid> PageByEmail<TTie>(
        IEnumerable<ClientSortKey<TTie>> rows, bool descending, int page, int limit)
        => Page(rows, k => Email(k.MaconomyNumber, k.Stored), descending, page, limit);

    public IReadOnlyList<Guid> PageByPhone<TTie>(
        IEnumerable<ClientSortKey<TTie>> rows, bool descending, int page, int limit)
        => Page(rows, k => Phone(k.MaconomyNumber, k.Stored), descending, page, limit);

    private static IReadOnlyList<Guid> Page<TTie>(
        IEnumerable<ClientSortKey<TTie>> rows, Func<ClientSortKey<TTie>, string?> value,
        bool descending, int page, int limit)
    {
        var keyed = rows.Select(k => (k.Id, Value: value(k), k.Tie));
        var ordered = descending
            ? keyed.OrderByDescending(k => k.Value, Order).ThenByDescending(k => k.Tie)
            : keyed.OrderBy(k => k.Value, Order).ThenBy(k => k.Tie);
        return ordered.Skip((page - 1) * limit).Take(limit).Select(k => k.Id).ToList();
    }
}

/// <summary>
/// One row of a list as its client column is ordered: the value the row itself holds, the Maconomy
/// customer number that says to read it from Maconomy instead, and what breaks a tie.
/// </summary>
internal sealed record ClientSortKey<TTie>(Guid Id, string? Stored, string? MaconomyNumber, TTie Tie);
