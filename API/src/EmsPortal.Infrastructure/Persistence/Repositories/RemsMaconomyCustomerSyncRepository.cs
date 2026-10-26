using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IRemsMaconomyCustomerSyncRepository"/>
internal sealed class RemsMaconomyCustomerSyncRepository : IRemsMaconomyCustomerSyncRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public RemsMaconomyCustomerSyncRepository(EmsPortalDbContext dbContext) => _dbContext = dbContext;

    // Pinned to the tenant named rather than the ambient one: the job runs with whatever tenant Hangfire
    // restored, and a Super Admin works another tenant's rows through ?tenantId=.
    private IQueryable<REMSMaconomyCustomerSync> InTenant(Guid tenantId)
        => _dbContext.RemsMaconomyCustomerSyncs.IgnoreQueryFilters().Where(s => s.TenantId == tenantId && !s.Deleted);

    public Task<REMSMaconomyCustomerSync?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
        => InTenant(tenantId).FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<REMSMaconomyCustomerSync?> GetByRemsIdAsync(Guid remsId, Guid tenantId, CancellationToken cancellationToken = default)
        => InTenant(tenantId).OrderByDescending(s => s.CreatedOnUtc).FirstOrDefaultAsync(s => s.REMSId == remsId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, REMSMaconomyCustomerSync>> ListByRemsIdsAsync(
        IReadOnlyCollection<Guid> remsIds, CancellationToken cancellationToken = default)
    {
        if (remsIds.Count == 0)
        {
            return new Dictionary<Guid, REMSMaconomyCustomerSync>();
        }
        var rows = await _dbContext.RemsMaconomyCustomerSyncs
            .Where(s => remsIds.Contains(s.REMSId))
            .OrderByDescending(s => s.CreatedOnUtc)
            .ToListAsync(cancellationToken);
        var byRems = new Dictionary<Guid, REMSMaconomyCustomerSync>();
        foreach (var row in rows)
        {
            byRems.TryAdd(row.REMSId, row);
        }
        return byRems;
    }

    public async Task<IReadOnlyList<REMSMaconomyCustomerSync>> ListUnsyncedAsync(
        Guid tenantId, DateTime staleBeforeUtc, CancellationToken cancellationToken = default)
        => await InTenant(tenantId)
            .Include(s => s.Rems)
            .Where(s => s.Rems != null && !s.Rems.Deleted)
            .Where(s => s.Status == RemsMaconomyCustomerSyncStatus.Failed
                || s.Status == RemsMaconomyCustomerSyncStatus.NotSynced
                || (s.Status == RemsMaconomyCustomerSyncStatus.Syncing && s.LastAttemptOnUtc < staleBeforeUtc))
            .OrderByDescending(s => s.CreatedOnUtc)
            .ToListAsync(cancellationToken);

    public async Task<RemsMaconomyCustomerSyncCounts> CountAsync(
        Guid tenantId, DateTime staleBeforeUtc, CancellationToken cancellationToken = default)
    {
        var groups = await InTenant(tenantId)
            .GroupBy(s => new
            {
                s.Status,
                Stale = s.Status == RemsMaconomyCustomerSyncStatus.Syncing && s.LastAttemptOnUtc < staleBeforeUtc,
            })
            .Select(g => new { g.Key.Status, g.Key.Stale, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Of(RemsMaconomyCustomerSyncStatus status, bool? stale = null)
            => groups.Where(g => g.Status == status && (stale is null || g.Stale == stale)).Sum(g => g.Count);

        return new RemsMaconomyCustomerSyncCounts(
            Synced: Of(RemsMaconomyCustomerSyncStatus.Synced),
            Linked: Of(RemsMaconomyCustomerSyncStatus.Linked),
            Failed: Of(RemsMaconomyCustomerSyncStatus.Failed),
            NotSynced: Of(RemsMaconomyCustomerSyncStatus.NotSynced),
            Pending: Of(RemsMaconomyCustomerSyncStatus.Pending),
            Syncing: Of(RemsMaconomyCustomerSyncStatus.Syncing, stale: false),
            Stale: Of(RemsMaconomyCustomerSyncStatus.Syncing, stale: true));
    }

    // The root's IgnoreQueryFilters covers the sync subquery too; a request is "unrecorded" only while no
    // live row names it.
    private IQueryable<REMS> ApprovedUnrecorded(Guid tenantId)
        => _dbContext.Rems
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && !r.Deleted
                && r.Type != null && r.Type.Value == RemsRequestTypes.BrandNewClient
                && r.Status != null && r.Status.Value == RemsRequestStatuses.Approved
                && r.ClientPerson != null && r.ClientPerson.MaconomyCustomerNumber == null
                && !_dbContext.RemsMaconomyCustomerSyncs.Any(s => s.REMSId == r.Id && !s.Deleted));

    public async Task<IReadOnlyList<REMS>> ListApprovedUnrecordedAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await ApprovedUnrecorded(tenantId)
            .Include(r => r.Type)
            .Include(r => r.Status)
            .Include(r => r.ClientPerson)
            .OrderByDescending(r => r.UpdatedOnUtc)
            .ToListAsync(cancellationToken);

    public Task<int> CountApprovedUnrecordedAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => ApprovedUnrecorded(tenantId).CountAsync(cancellationToken);

    public async Task<RemsMaconomyCustomerSyncSubject?> GetSubjectAsync(
        Guid remsId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var rems = await _dbContext.Rems
            .IgnoreQueryFilters()
            .Include(r => r.Type)
            .Include(r => r.Status)
            .Include(r => r.ClientPerson)
            .FirstOrDefaultAsync(r => r.Id == remsId && r.TenantId == tenantId && !r.Deleted, cancellationToken);
        if (rems is null)
        {
            return null;
        }

        // The type the client was actually asked under: a sent form's, else the latest form's.
        var entityType = await _dbContext.RemsForms
            .IgnoreQueryFilters()
            .Where(f => f.REMSId == remsId && f.TenantId == tenantId && !f.Deleted)
            .OrderByDescending(f => f.SentOnUtc != null)
            .ThenByDescending(f => f.CreatedOnUtc)
            .Select(f => f.EntityType != null ? f.EntityType.Value : null)
            .FirstOrDefaultAsync(cancellationToken);

        var industry = await _dbContext.RemsEngagements
            .IgnoreQueryFilters()
            .Where(e => e.REMSId == remsId && e.TenantId == tenantId && !e.Deleted)
            .OrderByDescending(e => e.CreatedOnUtc)
            .Select(e => e.Industry != null ? e.Industry.Value : null)
            .FirstOrDefaultAsync(cancellationToken);

        return new RemsMaconomyCustomerSyncSubject(rems, entityType, industry);
    }

    public Task<bool> NumberHeldByAnotherAsync(
        Guid tenantId, string customerNumber, Guid exceptPersonId, CancellationToken cancellationToken = default)
        => _dbContext.Persons
            .IgnoreQueryFilters()
            .AnyAsync(
                p => p.TenantId == tenantId && !p.Deleted && p.Id != exceptPersonId && p.MaconomyCustomerNumber == customerNumber,
                cancellationToken);

    public async Task AddAsync(REMSMaconomyCustomerSync sync, CancellationToken cancellationToken = default)
        => await _dbContext.RemsMaconomyCustomerSyncs.AddAsync(sync, cancellationToken);

    public void Update(REMSMaconomyCustomerSync sync) => _dbContext.RemsMaconomyCustomerSyncs.Update(sync);
}
