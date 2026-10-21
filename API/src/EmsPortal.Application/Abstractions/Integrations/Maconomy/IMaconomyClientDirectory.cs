using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>
/// What Maconomy says about the customers a tenant's clients are, held in memory. The portal stores a
/// client's Maconomy customer number and nothing else, so every name, email and phone shown for such a
/// client is read from here — and this is what keeps that from being a call to Maconomy each time.
/// One instance for the process; nothing in it is ever written to the database.
/// </summary>
public interface IMaconomyClientDirectory
{
    /// <summary>
    /// Makes what is held for the tenant fit to read: loads it the first time, fetches a customer number
    /// met since, and refreshes it in the background once it is older than
    /// <c>Maconomy:CustomerCacheSeconds</c>. Never throws for Maconomy's sake — when it cannot be asked,
    /// what was held last stays in use.
    /// </summary>
    Task EnsureLoadedAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Sets <see cref="Person.Maconomy"/> on a client that is a Maconomy customer, loading first. For a read that happens outside a REMS controller.</summary>
    Task HydrateAsync(Person? client, CancellationToken cancellationToken = default);

    /// <summary>The customer as held, or null. Never a call.</summary>
    MaconomyClientDetails? Find(Guid? tenantId, string? number);

    /// <summary>
    /// Reads one customer from Maconomy NOW, whatever is held, and holds the answer. Null when Maconomy
    /// has no such customer. Throws <see cref="MaconomyException"/> when Maconomy cannot be asked.
    /// </summary>
    Task<MaconomyClientDetails?> ReadAsync(Guid tenantId, string number, CancellationToken cancellationToken = default);

    /// <summary>Whether what is held was read within the cache lifetime.</summary>
    bool IsFresh(Guid tenantId);

    /// <summary>Numbers of the held customers whose name or number contains the term — a list's client search.</summary>
    IReadOnlyList<string> MatchByName(Guid tenantId, string term);

    /// <summary>Numbers of the held customers whose email or telephone contains the term — a list's contact search.</summary>
    IReadOnlyList<string> MatchByContact(Guid tenantId, string term);

    /// <summary>
    /// The client picker's search over Maconomy: by name, number, email or telephone. Answered from memory
    /// when every customer is held (<c>Maconomy:PreloadAllCustomers</c>), by asking Maconomy otherwise.
    /// Throws <see cref="MaconomyException"/> when it has to ask and cannot.
    /// </summary>
    Task<MaconomyClientSearch> SearchAsync(Guid tenantId, string? term, CancellationToken cancellationToken = default);

    /// <summary>Drops everything held for the tenant — its connection now points somewhere else.</summary>
    void Forget(Guid tenantId);
}

/// <summary>A picker search's customers, and whether more matched than are returned.</summary>
public sealed record MaconomyClientSearch(IReadOnlyList<MaconomyClientDetails> Customers, bool More);
