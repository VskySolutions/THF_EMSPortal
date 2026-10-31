using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.OptionSets;

/// <summary>
/// Gives a tenant its own copy of the platform's default option lists (<see cref="DefaultOptionSets"/>) at
/// tenant creation. The copies are tenant-owned and NOT system lists, which is what makes their items
/// add/rename/delete/re-orderable by a Super Admin or Tenant Admin — the platform-level originals stay
/// read-only because one row is shared by every tenant.
/// <para>
/// Idempotent per LIST, not per item: a tenant that already holds a list is left exactly as they edited it.
/// So adding a whole new list to <see cref="DefaultOptionSets"/> reaches tenants created afterwards, while
/// adding an item to a list a tenant already owns does not — that is deliberate, since reconciling items
/// would resurrect values the tenant deleted and fight their renames.
/// </para>
/// </summary>
public static class TenantOptionSetSeeder
{
    /// <summary>
    /// Adds a tenant-owned copy of every default list the tenant is missing. Returns true when anything was
    /// staged, so the caller can decide whether a save is needed. Does not save.
    /// </summary>
    public static async Task<bool> EnsureDefaultsAsync(
        IOptionSetRepository sets, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var added = false;

        foreach (var def in DefaultOptionSets.All)
        {
            if (await sets.KeyExistsAsync(tenantId, def.EntityType, def.Key, excludeId: null, cancellationToken))
            {
                continue;
            }

            var set = new OptionSet
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EntityType = def.EntityType,
                Key = def.Key,
                Name = def.Name,
                ItemSortMode = def.ItemSortMode,
                IsSystem = false,
                // A list the application branches on keeps its closed-ness through the copy: the tenant
                // owns the row and every label on it, but the CODES are the workflow's.
                IsClosed = def.IsClosed,
                IsActive = true,
                Items = def.Items.Select(i => new OptionSetItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Value = i.Value,
                    Label = i.Label,
                    Description = i.Description,
                    SortOrder = i.SortOrder,
                    IsActive = i.IsActive,
                    MetadataJson = i.MetadataJson,
                    MaconomyNumber = i.MaconomyNumber,
                    BackgroundColor = i.BackgroundColor,
                    TextColor = i.TextColor,
                    Icon = i.Icon,
                    // Seeded values on a closed list are the ones the server writes and reads back, so
                    // they cannot be deleted, deactivated or re-coded. Everything else about them is the
                    // tenant's to change.
                    IsSystem = def.SeedsSystemValues,
                }).ToList(),
            };

            await sets.AddSetAsync(set, cancellationToken);
            added = true;
        }

        return added;
    }
}
