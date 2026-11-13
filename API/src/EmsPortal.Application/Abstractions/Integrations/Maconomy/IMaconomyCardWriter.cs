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

    /// <summary>Adds rows under an existing card, skipping any already there; returns how many were added.</summary>
    Task<int> AddTableRowsAsync(MaconomyConnection connection, MaconomyTableRowsAdd add, CancellationToken cancellationToken = default);

    /// <summary>Sets values on the matching row of the table under an existing card. Null, nothing written, when no row matches.</summary>
    Task<IReadOnlyDictionary<string, string?>?> SetTableRowAsync(
        MaconomyConnection connection, MaconomyTableRowSet set, CancellationToken cancellationToken = default);
}
