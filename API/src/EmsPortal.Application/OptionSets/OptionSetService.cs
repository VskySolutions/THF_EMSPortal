using EmsPortal.Application.Abstractions.OptionSets;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.OptionSets;

/// <summary>
/// Default <see cref="IOptionSetService"/>. Writes are pinned to the resolved tenant. A standard (seeded)
/// list is a starting point rather than a fixed one: its values can be added, renamed, re-ordered and
/// removed. Only deleting the list itself is refused, since feature code references its key.
/// </summary>
public sealed class OptionSetService : IOptionSetService
{
    private readonly IOptionSetRepository _sets;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;
    private readonly IOptionCodeResolver _codes;

    public OptionSetService(
        IOptionSetRepository sets,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        IOptionCodeResolver codes)
    {
        _sets = sets;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _codes = codes;
    }

    /// <summary>
    /// Saves, and retires the resolver's cached id/code maps.
    ///
    /// <para>
    /// Every write in this class goes through here. Those maps are what turns the option-item id stored on
    /// a row back into the code the application branches on, so a list edited while a stale map is in
    /// memory would keep resolving to the value it USED to have. A tenant taking their own copy of a
    /// standard list changes which ids are effective for them from that moment too.
    /// </para>
    /// </summary>
    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _codes.Invalidate();
    }

    private Guid TenantId =>
        _tenantContext.IsResolved
            ? _tenantContext.TenantId
            : throw new OptionSetException(OptionSetErrorCodes.NoActiveTenant, "No active tenant for the caller.");

    public async Task<OptionSet> CreateSetAsync(CreateOptionSetInput input, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId;
        var key = input.Key.Trim();

        if (await _sets.KeyExistsAsync(tenantId, input.EntityType, key, excludeId: null, cancellationToken))
        {
            throw new OptionSetException(OptionSetErrorCodes.DuplicateKey, $"A list with the key '{key}' already exists for this entity.");
        }

        var set = new OptionSet
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityType = input.EntityType,
            Key = key,
            Name = input.Name.Trim(),
            ParentSetId = input.ParentSetId,
            ItemSortMode = input.ItemSortMode,
            IsSystem = false,
            IsActive = true,
        };

        await _sets.AddSetAsync(set, cancellationToken);
        await SaveChangesAsync(cancellationToken);
        return set;
    }

    public async Task<OptionSet?> UpdateSetAsync(Guid id, UpdateOptionSetInput input, CancellationToken cancellationToken = default)
    {
        var set = await _sets.GetSetWithItemsAsync(id, TenantId, cancellationToken);
        if (set is null)
        {
            return null;
        }

        set.Name = input.Name.Trim();
        set.ItemSortMode = input.ItemSortMode;
        set.IsActive = input.IsActive;

        _sets.UpdateSet(set);
        await SaveChangesAsync(cancellationToken);
        return set;
    }

    public async Task<bool?> DeleteSetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var set = await _sets.GetSetWithItemsAsync(id, TenantId, cancellationToken);
        if (set is null)
        {
            return null;
        }

        EnsureDeletable(set);

        _sets.RemoveSet(set);
        await SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<OptionSetItem?> CreateItemAsync(CreateOptionItemInput input, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId;
        var set = await _sets.GetSetWithItemsAsync(input.OptionSetId, tenantId, cancellationToken);
        if (set is null)
        {
            return null;
        }

        // A closed list's values are the application's own: it writes them and branches on them, so a value
        // nothing ever sets is a value nothing can reach, and a picker offering it is a dead end.
        // Everything about the values that ARE there stays editable.
        if (set.IsClosed)
        {
            throw new OptionSetException(
                OptionSetErrorCodes.ClosedSet,
                $"'{set.Name}' is a list the application depends on, so no value can be added to it. "
                + "The values it has can be renamed, described, coloured and re-ordered.");
        }

        var value = input.Value.Trim();
        if (await _sets.ItemValueExistsAsync(set.Id, tenantId, value, excludeId: null, cancellationToken))
        {
            throw new OptionSetException(OptionSetErrorCodes.DuplicateValue, $"A value '{value}' already exists in this list.");
        }

        // New custom-ordered items go to the end of the list.
        var nextOrder = set.Items.Count == 0 ? 0 : set.Items.Max(i => i.SortOrder) + 1;

        var item = new OptionSetItem
        {
            Id = Guid.NewGuid(),
            OptionSetId = set.Id,
            // Match the parent list: a value added to a standard (platform) list belongs to it exactly as
            // the seeded values do, rather than being stamped with whichever tenant happened to add it.
            TenantId = set.TenantId ?? tenantId,
            ParentItemId = input.ParentItemId,
            Value = value,
            Label = input.Label.Trim(),
            Description = NullIfBlank(input.Description),
            SortOrder = nextOrder,
            IsDefault = input.IsDefault,
            IsActive = true,
            BackgroundColor = NullIfBlank(input.BackgroundColor),
            TextColor = NullIfBlank(input.TextColor),
            Icon = NullIfBlank(input.Icon),
            MetadataJson = NullIfBlank(input.MetadataJson),
            MaconomyNumber = NullIfBlank(input.MaconomyNumber),
        };

        await _sets.AddItemAsync(item, cancellationToken);
        await SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<OptionSetItem?> UpdateItemAsync(Guid setId, Guid itemId, UpdateOptionItemInput input, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId;
        var set = await _sets.GetSetWithItemsAsync(setId, tenantId, cancellationToken);
        if (set is null)
        {
            return null;
        }

        var item = await _sets.GetItemAsync(itemId, setId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var value = input.Value.Trim();
        if (await _sets.ItemValueExistsAsync(setId, item.TenantId, value, excludeId: itemId, cancellationToken))
        {
            throw new OptionSetException(OptionSetErrorCodes.DuplicateValue, $"A value '{value}' already exists in this list.");
        }

        // A system value is the CODE the server writes and reads back, and now the FK every row holding it
        // points at. Re-coding it would strand every one of those rows, so it is always refused; the rest
        // of the row — label, description, colours, icon, order — is edited as any other value's is.
        if (item.IsSystem && !string.Equals(item.Value, value, StringComparison.Ordinal))
        {
            throw new OptionSetException(
                OptionSetErrorCodes.SystemItem,
                $"'{item.Label}' is a value the application writes, so its code cannot change. "
                + "Rename the label instead — that is what everybody sees.");
        }

        // Hiding one is refused only on a CLOSED list, where the value is a state the workflow still
        // reaches and a hidden one leaves the badge with nothing to render. On an open list, hiding a
        // seeded value is a firm saying "we do not do GCS work" — reasonable, and the engagements already
        // recorded against it keep pointing at it either way.
        if (item.IsSystem && set.IsClosed && !input.IsActive)
        {
            throw new OptionSetException(
                OptionSetErrorCodes.SystemItem,
                $"'{item.Label}' is a state the application still sets, so it cannot be hidden.");
        }

        item.Value = value;
        item.Label = input.Label.Trim();
        item.Description = NullIfBlank(input.Description);
        item.ParentItemId = input.ParentItemId;
        item.IsDefault = input.IsDefault;
        item.IsActive = input.IsActive;
        item.BackgroundColor = NullIfBlank(input.BackgroundColor);
        item.TextColor = NullIfBlank(input.TextColor);
        item.Icon = NullIfBlank(input.Icon);
        item.MetadataJson = NullIfBlank(input.MetadataJson);
        item.MaconomyNumber = NullIfBlank(input.MaconomyNumber);

        _sets.UpdateItem(item);
        await SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<bool?> DeleteItemAsync(Guid setId, Guid itemId, CancellationToken cancellationToken = default)
    {
        var set = await _sets.GetSetWithItemsAsync(setId, TenantId, cancellationToken);
        if (set is null)
        {
            return null;
        }

        var item = await _sets.GetItemAsync(itemId, setId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        // Deleting a value the server still writes leaves every row already pointing at it with a dangling
        // reference. The database refuses that too, now the columns are foreign keys — this says so in
        // words rather than letting the save come back as a constraint violation.
        if (item.IsSystem)
        {
            throw new OptionSetException(
                OptionSetErrorCodes.SystemItem,
                $"'{item.Label}' is a value the application writes, so it cannot be deleted. Rename it, "
                + "recolour it or re-order it instead.");
        }

        _sets.RemoveItem(item);
        await SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool?> ReorderItemsAsync(Guid setId, IReadOnlyList<Guid> orderedItemIds, CancellationToken cancellationToken = default)
    {
        var set = await _sets.GetSetWithItemsAsync(setId, TenantId, cancellationToken);
        if (set is null)
        {
            return null;
        }

        var items = await _sets.ListItemsAsync(setId, cancellationToken);
        var byId = items.ToDictionary(i => i.Id);

        // The new order must reference exactly the set's items — no missing, extra, or unknown ids.
        if (orderedItemIds.Count != items.Count || orderedItemIds.Distinct().Count() != orderedItemIds.Count
            || orderedItemIds.Any(id => !byId.ContainsKey(id)))
        {
            throw new OptionSetException(OptionSetErrorCodes.InvalidReorder, "The reorder request must list every item in the set exactly once.");
        }

        for (var index = 0; index < orderedItemIds.Count; index++)
        {
            var item = byId[orderedItemIds[index]];
            if (item.SortOrder != index)
            {
                item.SortOrder = index;
                _sets.UpdateItem(item);
            }
        }

        await SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Guards against DELETING a platform-standard (seeded) list. Their values are editable — a standard
    /// list is a starting point, not a fixed one — but removing the list itself is not offered: the key is
    /// referenced by feature code (REMS.Department, REMS.Status …), and the seeder would simply recreate
    /// it on the next restart.
    /// </summary>
    private static void EnsureDeletable(OptionSet set)
    {
        if (set.TenantId is null || set.IsSystem)
        {
            throw new OptionSetException(OptionSetErrorCodes.ReadOnlyStandardSet, "A standard list cannot be deleted. Its values can be edited instead.");
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
