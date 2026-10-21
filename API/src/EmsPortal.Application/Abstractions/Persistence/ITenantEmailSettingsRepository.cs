using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// Data access for a tenant's email settings. Scoped by an explicit tenant id and bypassing the ambient
/// query filter, like the SMTP accounts: a Super Admin edits any tenant's from the Email Accounts screen,
/// and the Workers process reads them with no tenant resolved at all. Soft-deleted rows are always excluded.
/// </summary>
public interface ITenantEmailSettingsRepository
{
    /// <summary>The tenant's live settings row, or null while it has never changed them (notifications on).</summary>
    Task<TenantEmailSettings?> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task AddAsync(TenantEmailSettings settings, CancellationToken cancellationToken = default);

    void Update(TenantEmailSettings settings);
}
