using EmsPortal.Application.Abstractions.OptionSets;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace EmsPortal.Application.OptionSets;

/// <summary>
/// Default <see cref="IOptionCodeResolver"/>, over <see cref="IMemoryCache"/>.
///
/// <para>
/// Two caches, because the two directions are asked in different shapes. CODE → ID is asked per (tenant,
/// list) and is answered from the effective list for that tenant — their own copy where they have one, the
/// platform's otherwise, exactly as <c>GetEffectiveSetAsync</c> resolves it. ID → CODE is asked for a
/// handful of ids off rows already read, has no tenant to scope by (an item id is unique platform-wide),
/// and so is cached per id.
/// </para>
/// <para>
/// The TTL is long because these lists change about never: the values the application branches on are
/// locked against deletion and re-coding. It is not INFINITE because a tenant taking their own copy of a
/// standard list changes which ids are effective for them, and <see cref="Invalidate"/> covers only the
/// writes this process made — another instance's write is picked up when the entry expires.
/// </para>
/// </summary>
public sealed class OptionCodeResolver : IOptionCodeResolver
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    // Bumped by Invalidate(). Every cache key carries it, so one increment retires every entry at once
    // without walking the cache — IMemoryCache has no "remove by prefix".
    private static long _generation;

    private readonly IOptionSetRepository _sets;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;

    public OptionCodeResolver(IOptionSetRepository sets, ITenantContext tenantContext, IMemoryCache cache)
    {
        _sets = sets;
        _tenantContext = tenantContext;
        _cache = cache;
    }

    private Guid? ScopeTenantId => _tenantContext.IsResolved ? _tenantContext.TenantId : null;

    public async Task<Guid?> IdOfAsync(
        string setKey, string? code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var byCode = await IdsByCodeAsync(setKey, cancellationToken);
        return byCode.TryGetValue(code.Trim(), out var id) ? id : null;
    }

    public async Task<IReadOnlyDictionary<string, Guid>> IdsByCodeAsync(
        string setKey, CancellationToken cancellationToken = default)
    {
        var key = $"optioncodes:{_generation}:ids:{ScopeTenantId}:{setKey}";
        if (_cache.TryGetValue(key, out IReadOnlyDictionary<string, Guid>? cached) && cached is not null)
        {
            return cached;
        }

        var set = await _sets.GetEffectiveSetAsync(ScopeTenantId, setKey, cancellationToken);

        // INACTIVE values are included on purpose. A record already recorded against a value a tenant has
        // since hidden must still resolve — hiding a value stops it being OFFERED, it does not unsay what
        // is already stored. Only the pickers filter on IsActive.
        var map = (set?.Items ?? new List<Domain.Entities.OptionSetItem>())
            .Where(i => !i.Deleted)
            .GroupBy(i => i.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        _cache.Set(key, (IReadOnlyDictionary<string, Guid>)map, Ttl);
        return map;
    }

    public async Task<string?> CodeOfAsync(Guid? itemId, CancellationToken cancellationToken = default)
    {
        if (itemId is not { } id || id == Guid.Empty)
        {
            return null;
        }

        var codes = await CodesOfAsync(new Guid?[] { id }, cancellationToken);
        return codes.TryGetValue(id, out var code) ? code : null;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> CodesOfAsync(
        IEnumerable<Guid?> itemIds, CancellationToken cancellationToken = default)
    {
        var wanted = itemIds
            .Where(i => i is { } id && id != Guid.Empty)
            .Select(i => i!.Value)
            .Distinct()
            .ToList();
        if (wanted.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var resolved = new Dictionary<Guid, string>();
        var missing = new List<Guid>();
        foreach (var id in wanted)
        {
            if (_cache.TryGetValue(CodeKey(id), out string? code) && code is not null)
            {
                resolved[id] = code;
            }
            else
            {
                missing.Add(id);
            }
        }

        if (missing.Count > 0)
        {
            foreach (var item in await _sets.ListItemsByIdsAsync(missing, cancellationToken))
            {
                resolved[item.Id] = item.Value;
                _cache.Set(CodeKey(item.Id), item.Value, Ttl);
            }
        }

        return resolved;
    }

    public void Invalidate() => Interlocked.Increment(ref _generation);

    private static string CodeKey(Guid itemId) => $"optioncodes:{_generation}:code:{itemId}";
}
