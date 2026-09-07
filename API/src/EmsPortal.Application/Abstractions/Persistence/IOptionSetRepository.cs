using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>Persistence for tenant-configurable option lists.</summary>
public interface IOptionSetRepository
{
    /// <summary>Standard lists ∪ the tenant's lists, optionally filtered by entity type.</summary>
    Task<IReadOnlyList<OptionSet>> ListSetsForScopeAsync(Guid? tenantId, EntityType? entityType, CancellationToken cancellationToken = default);

    /// <summary>A single list (standard or owned by the tenant) including its items, or null.</summary>
    Task<OptionSet?> GetSetWithItemsAsync(Guid id, Guid? tenantId, CancellationToken cancellationToken = default);

    /// <summary>The effective list for a key: the tenant's own list when present, otherwise the standard one.</summary>
    Task<OptionSet?> GetEffectiveSetAsync(Guid? tenantId, EntityType entityType, string key, CancellationToken cancellationToken = default);

    /// <summary>Item counts per set id (non-deleted) for the given set ids.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountItemsAsync(IReadOnlyCollection<Guid> setIds, CancellationToken cancellationToken = default);

    /// <summary>True when another non-deleted list in the same scope already uses this key.</summary>
    Task<bool> KeyExistsAsync(Guid? tenantId, EntityType entityType, string key, Guid? excludeId, CancellationToken cancellationToken = default);

    /// <summary>A single item by id within the given set, or null.</summary>
    Task<OptionSetItem?> GetItemAsync(Guid itemId, Guid setId, CancellationToken cancellationToken = default);

    /// <summary>All non-deleted items of a set (any scope).</summary>
    Task<IReadOnlyList<OptionSetItem>> ListItemsAsync(Guid setId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Items by ID, across every list and scope — what the option-code resolver reads to turn the
    /// ids stored on a row back into the codes the application branches on.
    /// </summary>
    Task<IReadOnlyList<OptionSetItem>> ListItemsByIdsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);

    /// <summary>True when another non-deleted item in the same set+scope already uses this value.</summary>
    Task<bool> ItemValueExistsAsync(Guid setId, Guid? tenantId, string value, Guid? excludeId, CancellationToken cancellationToken = default);

    Task AddSetAsync(OptionSet set, CancellationToken cancellationToken = default);
    Task AddItemAsync(OptionSetItem item, CancellationToken cancellationToken = default);
    void UpdateSet(OptionSet set);
    void UpdateItem(OptionSetItem item);
    void RemoveSet(OptionSet set);
    void RemoveItem(OptionSetItem item);
}
