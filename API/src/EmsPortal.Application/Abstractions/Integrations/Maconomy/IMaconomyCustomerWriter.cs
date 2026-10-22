using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>
/// The writes the customer sync makes to Maconomy's customer card, plus the one read it needs first.
/// Runs through the session manager, so the token takes care of itself.
/// </summary>
public interface IMaconomyCustomerWriter
{
    /// <summary>Every customer whose email address is exactly this one, any case. The duplicate check before a create.</summary>
    Task<IReadOnlyList<MaconomyClientDetails>> FindByEmailAsync(
        MaconomyConnection connection, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the customer with the number left to Maconomy's series, and returns it as created — the
    /// assigned number included.
    /// </summary>
    Task<MaconomyClientDetails> CreateAsync(
        MaconomyConnection connection, MaconomyNewCustomer customer, CancellationToken cancellationToken = default);
}
