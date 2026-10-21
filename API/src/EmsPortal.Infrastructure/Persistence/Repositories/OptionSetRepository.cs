using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class OptionSetRepository : IOptionSetRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public OptionSetRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<OptionSet>> ListSetsForScopeAsync(Guid? tenantId, EntityType? entityType, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.OptionSets
            .Where(s => s.TenantId == null || s.TenantId == tenantId);

        if (entityType is { } et)
        {
            query = query.Where(s => s.EntityType == et);
        }

        return await query
            .OrderByDescending(s => s.UpdatedOnUtc)
            .ThenBy(s => s.Name)
            .Take(_dbContext.MaxRows)
            .ToListAsync(cancellationToken);
    }

    public async Task<OptionSet?> GetSetWithItemsAsync(Guid id, Guid? tenantId, CancellationToken cancellationToken = default)
    {
        var set = await _dbContext.OptionSets
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id && (s.TenantId == null || s.TenantId == tenantId), cancellationToken);

        if (set is not null)
        {
            // Items carry their own soft-delete; the navigation include honours the global filter, but
            // sort the visible items deterministically for the caller.
            set.Items = set.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Label).ToList();
        }

        return set;
    }

    public Task<OptionSet?> GetEffectiveSetAsync(Guid? tenantId, EntityType entityType, string key, CancellationToken cancellationToken = default)
        => _dbContext.OptionSets
            .Include(s => s.Items)
            .Where(s => s.EntityType == entityType && s.Key == key && s.IsActive
                && (s.TenantId == tenantId || s.TenantId == null))
            // Prefer the tenant's own list over the standard one.
            .OrderByDescending(s => s.TenantId != null)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountItemsAsync(IReadOnlyCollection<Guid> setIds, CancellationToken cancellationToken = default)
    {
        if (setIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var counts = await _dbContext.OptionSetItems
            .Where(i => setIds.Contains(i.OptionSetId))
            .GroupBy(i => i.OptionSetId)
            .Select(g => new { SetId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(c => c.SetId, c => c.Count);
    }

    public Task<bool> KeyExistsAsync(Guid? tenantId, EntityType entityType, string key, Guid? excludeId, CancellationToken cancellationToken = default)
        => _dbContext.OptionSets
            .AnyAsync(s => s.TenantId == tenantId && s.EntityType == entityType && s.Key == key
                && (excludeId == null || s.Id != excludeId), cancellationToken);

    public Task<OptionSetItem?> GetItemAsync(Guid itemId, Guid setId, CancellationToken cancellationToken = default)
        => _dbContext.OptionSetItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.OptionSetId == setId, cancellationToken);

    public async Task<IReadOnlyList<OptionSetItem>> ListItemsAsync(Guid setId, CancellationToken cancellationToken = default)
        => await _dbContext.OptionSetItems
            .Where(i => i.OptionSetId == setId)
            .OrderBy(i => i.SortOrder)
            .Take(_dbContext.MaxRows)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OptionSetItem>> ListItemsByIdsAsync(
        IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default)
        => itemIds.Count == 0
            ? Array.Empty<OptionSetItem>()
            : await _dbContext.OptionSetItems
                .Where(i => itemIds.Contains(i.Id))
                .ToListAsync(cancellationToken);

    public Task<bool> ItemValueExistsAsync(Guid setId, Guid? tenantId, string value, Guid? excludeId, CancellationToken cancellationToken = default)
        => _dbContext.OptionSetItems
            .AnyAsync(i => i.OptionSetId == setId && i.TenantId == tenantId && i.Value == value
                && (excludeId == null || i.Id != excludeId), cancellationToken);

    public async Task AddSetAsync(OptionSet set, CancellationToken cancellationToken = default)
        => await _dbContext.OptionSets.AddAsync(set, cancellationToken);

    public async Task AddItemAsync(OptionSetItem item, CancellationToken cancellationToken = default)
        => await _dbContext.OptionSetItems.AddAsync(item, cancellationToken);

    public void UpdateSet(OptionSet set) => _dbContext.OptionSets.Update(set);

    public void UpdateItem(OptionSetItem item) => _dbContext.OptionSetItems.Update(item);

    public void RemoveSet(OptionSet set) => _dbContext.OptionSets.Remove(set);

    public void RemoveItem(OptionSetItem item) => _dbContext.OptionSetItems.Remove(item);
}
