using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class EmailLogRepository : IEmailLogRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public EmailLogRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(EmailLog log, CancellationToken cancellationToken = default)
    {
        await _dbContext.EmailLogs.AddAsync(log, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<EmailLog> Rows, int Total)> ListAsync(
        Guid tenantId, EmailLogFilter filter, int page, int limit, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.EmailLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.TenantId == tenantId && !l.Deleted);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(l => l.RecipientEmail.Contains(term) || l.Subject.Contains(term));
        }
        if (filter.Status is { } status)
        {
            query = query.Where(l => l.Status == status);
        }
        if (filter.TemplateKey is { } key)
        {
            query = query.Where(l => l.TemplateKey == key);
        }
        if (filter.IsOpened is { } opened)
        {
            query = query.Where(l => (l.OpenCount > 0) == opened);
        }
        if (filter.FromUtc is { } from)
        {
            query = query.Where(l => l.CreatedOnUtc >= from);
        }
        if (filter.ToUtc is { } to)
        {
            query = query.Where(l => l.CreatedOnUtc < to);
        }

        var total = await query.CountAsync(cancellationToken);
        // The bodies stay in the table: a page of them is megabytes nobody reads from the list.
        var rows = await query
            .OrderByDescending(l => l.CreatedOnUtc)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(l => new EmailLog
            {
                Id = l.Id,
                TenantId = l.TenantId,
                TemplateKey = l.TemplateKey,
                RecipientEmail = l.RecipientEmail,
                Subject = l.Subject,
                Status = l.Status,
                FailureReason = l.FailureReason,
                FirstOpenedOnUtc = l.FirstOpenedOnUtc,
                LastOpenedOnUtc = l.LastOpenedOnUtc,
                OpenCount = l.OpenCount,
                CreatedOnUtc = l.CreatedOnUtc,
            })
            .ToListAsync(cancellationToken);
        return (rows, total);
    }

    public Task<EmailLog?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
        => _dbContext.EmailLogs.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId && !l.Deleted, cancellationToken);

    public Task MarkOpenedAsync(Guid id, DateTime openedOnUtc, CancellationToken cancellationToken = default)
        => _dbContext.EmailLogs.IgnoreQueryFilters()
            .Where(l => l.Id == id && !l.Deleted && l.Status == EmailLogStatus.Sent)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(l => l.FirstOpenedOnUtc, l => l.FirstOpenedOnUtc ?? openedOnUtc)
                    .SetProperty(l => l.LastOpenedOnUtc, openedOnUtc)
                    .SetProperty(l => l.OpenCount, l => l.OpenCount + 1),
                cancellationToken);
}
