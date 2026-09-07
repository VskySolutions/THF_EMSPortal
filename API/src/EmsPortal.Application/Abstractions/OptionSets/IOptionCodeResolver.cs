using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.OptionSets;

/// <summary>
/// Translates between an option-set item's ID — which is what every table stores, as a foreign key
/// — and its CODE, which is what the application branches on.
/// </summary>
public interface IOptionCodeResolver
{
    /// <summary>
    /// The item id a code resolves to in the caller's tenant, or null when the list has no such value
    /// — which happens when a tenant added the code and then removed.
    /// </summary>
    Task<Guid?> IdOfAsync(EntityType entityType, string setKey, string? code, CancellationToken cancellationToken = default);

    /// <summary>The code an item id stands for, or null when the id is unknown.</summary>
    Task<string?> CodeOfAsync(Guid? itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The codes for several ids in one read — what every list and packet builder uses, so a page of
    /// twenty requests costs one lookup rather than twenty.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> CodesOfAsync(
        IEnumerable<Guid?> itemIds, CancellationToken cancellationToken = default);

    /// <summary>Every (code → id) pair in a list for the caller's tenant.</summary>
    Task<IReadOnlyDictionary<string, Guid>> IdsByCodeAsync(
        EntityType entityType, string setKey, CancellationToken cancellationToken = default);

    /// <summary>Drops the cached lists.</summary>
    void Invalidate();
}
