namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>The customer lookup a tenant's Maconomy connection powers.</summary>
public interface IMaconomyCustomerService
{
    /// <summary>
    /// Customers whose number or name contains <paramref name="search"/>, as dropdown options in
    /// Maconomy's own order. Empty below two characters, without a call. A null <paramref name="limit"/>
    /// takes the tenant's default; either is capped platform-wide.
    /// </summary>
    Task<IReadOnlyList<MaconomyCustomerOption>> SearchAsync(
        Guid tenantId,
        string? search,
        int? limit,
        CancellationToken cancellationToken = default);
}
