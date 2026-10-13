using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class TenantRepository : ITenantRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public TenantRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Tenant?> GetByIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
        => _dbContext.Tenants.FirstOrDefaultAsync(t => t.Identifier == identifier, cancellationToken);

    public Task<bool> IdentifierExistsAsync(string identifier, CancellationToken cancellationToken = default)
        => _dbContext.Tenants.AnyAsync(t => t.Identifier == identifier, cancellationToken);

    public async Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Tenants.OrderByDescending(t => t.UpdatedOnUtc).Take(_dbContext.MaxRows).ToListAsync(cancellationToken);

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
        => await _dbContext.Tenants.AddAsync(tenant, cancellationToken);

    public void Update(Tenant tenant)
        => _dbContext.Tenants.Update(tenant);
}
