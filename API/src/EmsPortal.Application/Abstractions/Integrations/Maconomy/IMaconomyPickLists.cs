using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>
/// Maconomy's pick lists (popups), read from their own containers and kept briefly per tenant. A popup
/// field is written with one of the list's literals, so what the portal holds — a state code, a country,
/// a year — is looked up here before it is sent.
/// </summary>
public interface IMaconomyPickLists
{
    /// <summary>The list in <paramref name="container"/> (<c>popup_countrytype</c>, <c>popup_jobpopuptype1</c>), hidden and blank entries left out.</summary>
    Task<MaconomyPickList> GetAsync(MaconomyConnection connection, string container, CancellationToken cancellationToken = default);

    /// <summary>Maconomy's template jobs as a list: the value is the job number, the title the job name.</summary>
    Task<MaconomyPickList> GetJobTemplatesAsync(MaconomyConnection connection, CancellationToken cancellationToken = default);
}
