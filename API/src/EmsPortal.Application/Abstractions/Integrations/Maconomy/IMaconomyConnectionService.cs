using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>Managing a tenant's Maconomy connection: the row, its secrets, and the login that proves it.</summary>
public interface IMaconomyConnectionService
{
    /// <summary>The tenant's connection, or null when none is configured.</summary>
    Task<MaconomyConnection?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates the tenant's connection. A password is required the first time and optional
    /// after, when a blank keeps the stored one. A change of URL, instance, user or password drops the
    /// token held, since it was issued to the old ones.
    /// </summary>
    Task<MaconomyConnection> SaveAsync(Guid tenantId, SaveMaconomyConnectionInput input, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the tenant's connection. False when there was none.</summary>
    Task<bool> DeleteAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs in afresh — the "Test connection" — and stores the token. Throws <see cref="MaconomyException"/>
    /// when Maconomy refuses or cannot be reached, after recording that on the row.
    /// </summary>
    Task<MaconomyLoginResult> LoginAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Drops the token so the next call logs in afresh. False when there is no connection.</summary>
    Task<bool> ForgetTokenAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
