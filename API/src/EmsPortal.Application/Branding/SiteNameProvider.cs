using EmsPortal.Application.Abstractions.Branding;
using EmsPortal.Application.Abstractions.Persistence;
using Microsoft.Extensions.Caching.Memory;

namespace EmsPortal.Application.Branding;

internal sealed class SiteNameProvider : ISiteNameProvider
{
    // Short: a renamed site shows within the minute, and a burst of messages costs one read.
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly ITenantBrandingRepository _branding;
    private readonly ITenantRepository _tenants;
    private readonly IMemoryCache _cache;

    public SiteNameProvider(ITenantBrandingRepository branding, ITenantRepository tenants, IMemoryCache cache)
    {
        _branding = branding;
        _tenants = tenants;
        _cache = cache;
    }

    public async Task<string> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var key = $"site-name:{tenantId:N}";
        if (_cache.TryGetValue(key, out string? cached) && cached is not null)
        {
            return cached;
        }
        var branding = await _branding.GetByTenantAsync(tenantId, cancellationToken);
        var name = BrandingThemeRules.Deserialize(branding?.ThemeJson)?.Identity?.ApplicationName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = (await _tenants.GetByIdAsync(tenantId, cancellationToken))?.Name?.Trim();
        }
        if (string.IsNullOrEmpty(name))
        {
            name = SiteWording.Fallback;
        }
        _cache.Set(key, name, Lifetime);
        return name;
    }

    public async Task<string> ApplyAsync(Guid? tenantId, string text, CancellationToken cancellationToken = default)
        => tenantId is { } id && SiteWording.Mentions(text)
            ? SiteWording.Apply(text, await GetAsync(id, cancellationToken))
            : text;
}
