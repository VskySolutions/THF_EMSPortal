using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace EmsPortal.Application.Integrations.Maconomy;

/// <summary>
/// Default <see cref="IMaconomyNumberMaps"/>: the three lists read through the option-set repository
/// (the tenant's own copy where it has one, the standard list otherwise) and kept for a minute per
/// tenant. Saving an item under Option Sets forgets the tenant's map, so an edit shows at once.
/// </summary>
public sealed class MaconomyNumberMaps : IMaconomyNumberMaps
{
    public const string EntityTypeSetKey = "REMS.EntityType";
    public const string IndustrySetKey = "REMS.Industry";
    public const string JobTemplateSetKey = "REMS.JobTemplate";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    private readonly IOptionSetRepository _sets;
    private readonly IMemoryCache _cache;

    public MaconomyNumberMaps(IOptionSetRepository sets, IMemoryCache cache)
    {
        _sets = sets;
        _cache = cache;
    }

    public async Task<MaconomyNumberMap> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(Key(tenantId), out MaconomyNumberMap? cached) && cached is not null)
        {
            return cached;
        }
        var map = new MaconomyNumberMap(
            await ListAsync(tenantId, EntityTypeSetKey, cancellationToken),
            await ListAsync(tenantId, IndustrySetKey, cancellationToken),
            await ListAsync(tenantId, JobTemplateSetKey, cancellationToken));
        _cache.Set(Key(tenantId), map, Lifetime);
        return map;
    }

    public void Forget(Guid tenantId) => _cache.Remove(Key(tenantId));

    private async Task<IReadOnlyList<MaconomyNumbered>> ListAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        var set = await _sets.GetEffectiveSetAsync(tenantId, EntityType.Rems, key, cancellationToken);
        if (set is null)
        {
            return Array.Empty<MaconomyNumbered>();
        }
        // Hidden values stay on the list: a customer filed under one still reads as that code.
        return (await _sets.ListItemsAsync(set.Id, cancellationToken))
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Label)
            .Select(i => new MaconomyNumbered(i.Id, i.Value, i.Label, string.IsNullOrWhiteSpace(i.MaconomyNumber) ? null : i.MaconomyNumber.Trim()))
            .ToList();
    }

    private static string Key(Guid tenantId) => $"maconomy:numbers:{tenantId:N}";
}
