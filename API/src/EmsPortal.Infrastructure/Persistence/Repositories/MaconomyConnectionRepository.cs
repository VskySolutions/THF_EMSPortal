using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core data access for <see cref="MaconomyConnection"/>. Bypasses the ambient tenant filter and
/// filters by the explicit tenant id instead, so a Super Admin managing another tenant through the
/// <c>?tenantId=</c> override reads and writes that tenant's row. Soft-deleted rows are excluded.
/// </summary>
internal sealed class MaconomyConnectionRepository : IMaconomyConnectionRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public MaconomyConnectionRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<MaconomyConnection?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => _dbContext.MaconomyConnections
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && !c.Deleted, cancellationToken);

    public async Task AddAsync(MaconomyConnection connection, CancellationToken cancellationToken = default)
        => await _dbContext.MaconomyConnections.AddAsync(connection, cancellationToken);

    public void Update(MaconomyConnection connection) => _dbContext.MaconomyConnections.Update(connection);

    public void Remove(MaconomyConnection connection) => _dbContext.MaconomyConnections.Remove(connection);
}
