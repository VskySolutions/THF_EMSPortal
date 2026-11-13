using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>
/// The one read the customer sync makes before it writes: the duplicate check by email. The write itself
/// goes through <see cref="IMaconomyCardWriter"/> with what <c>MaconomyCustomerDataMapping</c> produced.
/// </summary>
public interface IMaconomyCustomerWriter
{
    /// <summary>Every customer whose email address is exactly this one, any case.</summary>
    Task<IReadOnlyList<MaconomyClientDetails>> FindByEmailAsync(
        MaconomyConnection connection, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// The number for a new contact company and its customer: one more than the highest all-digit contact
    /// company number, stepped past any number a customer already holds.
    /// </summary>
    Task<string> NextContactCompanyNumberAsync(MaconomyConnection connection, CancellationToken cancellationToken = default);
}
