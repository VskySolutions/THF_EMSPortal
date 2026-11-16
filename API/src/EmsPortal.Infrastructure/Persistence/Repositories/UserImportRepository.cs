using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class UserImportRepository : IUserImportRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public UserImportRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(UserImport import, CancellationToken cancellationToken = default)
        => await _dbContext.UserImports.AddAsync(import, cancellationToken);

    public async Task<(IReadOnlyList<UserImport> Items, int Total)> ListAsync(
        Guid tenantId, int page, int limit, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.UserImports.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.TenantId == tenantId && !i.Deleted);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(i => i.CreatedOnUtc)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<UserImport?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
        => _dbContext.UserImports.IgnoreQueryFilters().AsNoTracking()
            .Include(i => i.Rows.OrderBy(r => r.RowNumber))
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId && !i.Deleted, cancellationToken);
}
