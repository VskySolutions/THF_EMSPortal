using EmsPortal.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence;

// Numbers come from database sequences, so a deleted row never frees its number and two requests created at
// once cannot collide. The sequences are global, not per tenant.

internal sealed class RemsNumberGenerator : IRemsNumberGenerator
{
    private readonly EmsPortalDbContext _dbContext;

    public RemsNumberGenerator(EmsPortalDbContext dbContext) => _dbContext = dbContext;

    public async Task<string> GenerateAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var next = await _dbContext.Database
            .SqlQueryRaw<int>("SELECT NEXT VALUE FOR dbo.seq_RemsNumber AS [Value]")
            .FirstAsync(cancellationToken);
        return $"REMS-{next}";
    }
}

internal sealed class ClientNumberGenerator : IClientNumberGenerator
{
    private readonly EmsPortalDbContext _dbContext;

    public ClientNumberGenerator(EmsPortalDbContext dbContext) => _dbContext = dbContext;

    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var next = await _dbContext.Database
            .SqlQueryRaw<int>("SELECT NEXT VALUE FOR dbo.seq_ClientNumber AS [Value]")
            .FirstAsync(cancellationToken);
        return $"CL-{next}";
    }
}
