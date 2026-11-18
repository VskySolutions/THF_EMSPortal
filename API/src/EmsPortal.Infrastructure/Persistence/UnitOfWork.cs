using EmsPortal.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence;

/// <summary>
/// EF Core unit of work. Delegates to the scoped <see cref="EmsPortalDbContext"/>,
/// so every repository sharing that context commits in a single transaction.
/// </summary>
internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly EmsPortalDbContext _dbContext;

    public UnitOfWork(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _dbContext.SaveChangesAsync(cancellationToken);

    public void DiscardChanges() => _dbContext.ChangeTracker.Clear();

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        // Use an execution strategy so the transaction is retried as a unit under the connection
        // resiliency policy. The in-memory provider used by unit tests treats this as a no-op.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
