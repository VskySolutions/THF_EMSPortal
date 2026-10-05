using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>The customer lookup a tenant's Maconomy connection powers.</summary>
public interface IMaconomyCustomerService
{
    /// <summary>
    /// Customers whose number, name, email or telephone contains <paramref name="search"/>, as dropdown
    /// options in Maconomy's own order, and whether that is every match. Empty below two characters,
    /// without a call. A null <paramref name="limit"/> takes the tenant's default; either is capped
    /// platform-wide.
    /// </summary>
    Task<MaconomyCustomerSearch> SearchAsync(
        Guid tenantId,
        string? search,
        int? limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The customers holding these numbers, by number, read from Maconomy now in as few calls as the
    /// count allows. A number missing from the answer is not a customer in Maconomy.
    /// </summary>
    Task<IReadOnlyDictionary<string, MaconomyClientDetails>> GetByNumbersAsync(
        Guid tenantId,
        IReadOnlyCollection<string> numbers,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every customer, in one call, and whether that is all of them — false when the row cap
    /// (<c>Maconomy:PreloadMaxRows</c>) was reached.
    /// </summary>
    Task<(IReadOnlyList<MaconomyClientDetails> Customers, bool Complete)> ListAllAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
