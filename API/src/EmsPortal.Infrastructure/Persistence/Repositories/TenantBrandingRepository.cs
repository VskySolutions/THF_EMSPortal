using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core data access for <see cref="TenantBranding"/>. Bypasses the ambient tenant filter and filters
/// by the explicit tenant id instead, so the <c>?tenantId=</c> override and the anonymous sign-in screen
/// both read the row they asked for. Soft-deleted rows are excluded.
/// </summary>
internal sealed class TenantBrandingRepository : ITenantBrandingRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public TenantBrandingRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<TenantBranding?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => _dbContext.TenantBrandings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && !b.Deleted, cancellationToken);

    public async Task AddAsync(TenantBranding branding, CancellationToken cancellationToken = default)
        => await _dbContext.TenantBrandings.AddAsync(branding, cancellationToken);

    public void Update(TenantBranding branding) => _dbContext.TenantBrandings.Update(branding);

    public void Remove(TenantBranding branding) => _dbContext.TenantBrandings.Remove(branding);
}
