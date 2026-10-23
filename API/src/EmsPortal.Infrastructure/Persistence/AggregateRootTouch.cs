using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence;

/// <summary>
/// Restamps an aggregate root's audit fields by marking it modified on the current unit of work; the
/// DbContext's existing audit stamping then writes Updated By / Updated On as part of the same commit.
/// <para>
/// Only entity types whose LIST shows a parent whose children are edited elsewhere need this. For most of
/// the application the list IS the root — a role, a tag, a tenant — and editing it already restamps it.
/// REMS is the exception: its lists show requests while the work happens on forms, clients, entities,
/// engagements and approval rounds hanging beneath them. Add a case here as other aggregates grow the
/// same shape.
/// </para>
/// </summary>
internal sealed class AggregateRootTouch : IAggregateRootTouch
{
    private readonly EmsPortalDbContext _dbContext;

    public AggregateRootTouch(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task TouchAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        if (entityType != EntityType.Rems)
        {
            return;
        }

        // Usually already tracked — the caller just loaded or edited this request — in which case Find
        // returns it without a round trip. The tenant filter is not bypassed: a request outside the
        // caller's tenant resolves to null and is left alone.
        var rems = await _dbContext.RemsRequests.FindAsync(new object?[] { entityId }, cancellationToken);
        if (rems is null)
        {
            return;
        }

        var entry = _dbContext.Entry(rems);
        if (entry.State == EntityState.Unchanged)
        {
            // Nudge a single scalar rather than Update(), which would mark every property modified and
            // rewrite the whole row. StampAudit overwrites this with the real timestamp on save; what
            // matters here is moving the entry out of Unchanged so it is stamped at all.
            entry.Property(r => r.UpdatedOnUtc).IsModified = true;
        }
    }
}
