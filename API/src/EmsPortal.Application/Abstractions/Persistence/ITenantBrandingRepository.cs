using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// Data access for a tenant's branding. Scoped by an explicit tenant id and bypassing the ambient query
/// filter, like the Maconomy connection: a Super Admin edits any tenant's from the Tenants screen, and the
/// sign-in screen reads one with no tenant resolved at all. Soft-deleted rows are always excluded.
/// </summary>
public interface ITenantBrandingRepository
{
    /// <summary>The tenant's live branding row, or null when it still wears the stock look.</summary>
    Task<TenantBranding?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task AddAsync(TenantBranding branding, CancellationToken cancellationToken = default);

    void Update(TenantBranding branding);

    void Remove(TenantBranding branding);
}
