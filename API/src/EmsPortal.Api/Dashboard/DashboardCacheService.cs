using Microsoft.Extensions.Caching.Memory;

namespace EmsPortal.Api.Dashboard;

/// <summary>
/// Short-lived in-process cache for expensive dashboard aggregations (primarily the platform fan-out).
/// 60-second TTL; <c>forceRefresh</c> evicts the entry and re-queries.
/// </summary>
public interface IDashboardCacheService
{
    Task<T> GetOrAddAsync<T>(string key, bool forceRefresh, Func<Task<T>> factory);
}

/// <summary>Singleton <see cref="IMemoryCache"/>-backed implementation with a fixed 60s TTL.</summary>
public sealed class DashboardCacheService : IDashboardCacheService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private readonly IMemoryCache _cache;

    public DashboardCacheService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<T> GetOrAddAsync<T>(string key, bool forceRefresh, Func<Task<T>> factory)
    {
        if (forceRefresh)
        {
            _cache.Remove(key);
        }
        else if (_cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var value = await factory();
        _cache.Set(key, value, Ttl);
        return value;
    }
}
