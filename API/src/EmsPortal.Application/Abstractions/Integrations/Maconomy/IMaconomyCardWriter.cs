using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>
/// The one way a record is created on any Maconomy card: the write a mapping class produced goes to the
/// instance, and the record Maconomy made comes back. The mapping says what to write; this says how.
/// Runs through the session manager, so the token takes care of itself.
/// </summary>
public interface IMaconomyCardWriter
{
    Task<MaconomyCreatedRecord> CreateAsync(MaconomyConnection connection, MaconomyCardWrite write, CancellationToken cancellationToken = default);
}
