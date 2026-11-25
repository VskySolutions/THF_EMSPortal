using EmsPortal.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class ApplicationLogRepository : IApplicationLogRepository
{
    private const int ListMessageLength = 300;

    private readonly EmsPortalDbContext _dbContext;

    public ApplicationLogRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(IReadOnlyList<ApplicationLogEntry> Rows, int Total)> ListAsync(
        Guid tenantId, bool includeSystem, ApplicationLogFilter filter, int page, int limit, CancellationToken cancellationToken = default)
    {
        var query = Scoped(tenantId, includeSystem);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(l => l.Message!.Contains(term) || l.Exception!.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(filter.Level))
        {
            query = query.Where(l => l.Level == filter.Level);
        }
        if (filter.FromUtc is { } from)
        {
            query = query.Where(l => l.TimeStamp >= from);
        }
        if (filter.ToUtc is { } to)
        {
            query = query.Where(l => l.TimeStamp < to);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(l => l.Id)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(l => new ApplicationLogEntry
            {
                Id = l.Id,
                TenantId = l.TenantId,
                Level = l.Level,
                TimeStamp = l.TimeStamp,
                Message = l.Message!.Substring(0, ListMessageLength),
            })
            .ToListAsync(cancellationToken);
        return (rows, total);
    }

    public Task<ApplicationLogEntry?> GetAsync(Guid tenantId, bool includeSystem, int id, CancellationToken cancellationToken = default)
        => Scoped(tenantId, includeSystem).FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    // The table is the logger's own and not in the model, so it is read as a query type.
    private IQueryable<ApplicationLogEntry> Scoped(Guid tenantId, bool includeSystem)
        => _dbContext.Database
            .SqlQuery<ApplicationLogEntry>(
                $"SELECT Id, TenantId, [Level], [TimeStamp], Message, Exception, Properties FROM dbo.Logs")
            .Where(l => l.TenantId == tenantId || (includeSystem && l.TenantId == null));
}
