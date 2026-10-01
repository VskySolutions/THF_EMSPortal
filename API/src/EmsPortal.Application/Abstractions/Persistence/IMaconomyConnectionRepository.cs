using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// Data access for a tenant's Maconomy connection. Scoped by an explicit tenant id and bypassing the
/// ambient query filter, like the SMTP accounts, so a Super Admin can manage any tenant's through the
/// <c>?tenantId=</c> override. Soft-deleted rows are always excluded.
/// </summary>
public interface IMaconomyConnectionRepository
{
    /// <summary>The tenant's live connection, or null.</summary>
    Task<MaconomyConnection?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task AddAsync(MaconomyConnection connection, CancellationToken cancellationToken = default);

    void Update(MaconomyConnection connection);

    void Remove(MaconomyConnection connection);
}
