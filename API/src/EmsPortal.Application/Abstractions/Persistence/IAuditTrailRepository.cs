using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>Append-only data access for <see cref="AuditTrailEntry"/> records.</summary>
public interface IAuditTrailRepository
{
    /// <summary>Stages an audit entry for insertion.</summary>
    Task AddAsync(AuditTrailEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Reads the audit entries for a single entity instance, newest first.</summary>
    Task<IReadOnlyList<AuditTrailEntry>> ListByEntityAsync(string entityName, string entityId, int limit = 100, CancellationToken cancellationToken = default);
}
