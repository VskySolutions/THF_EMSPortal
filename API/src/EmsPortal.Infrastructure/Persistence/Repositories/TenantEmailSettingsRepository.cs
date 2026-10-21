using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core data access for <see cref="TenantEmailSettings"/>. Bypasses the ambient tenant filter and
/// filters by the explicit tenant id instead, so the <c>?tenantId=</c> override and the Workers process
/// (which has no tenant of its own) both read the row they asked for. Soft-deleted rows are excluded.
/// </summary>
internal sealed class TenantEmailSettingsRepository : ITenantEmailSettingsRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public TenantEmailSettingsRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<TenantEmailSettings?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => _dbContext.TenantEmailSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && !s.Deleted, cancellationToken);

    public async Task AddAsync(TenantEmailSettings settings, CancellationToken cancellationToken = default)
        => await _dbContext.TenantEmailSettings.AddAsync(settings, cancellationToken);

    public void Update(TenantEmailSettings settings) => _dbContext.TenantEmailSettings.Update(settings);
}
