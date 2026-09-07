using EmsPortal.Domain.Enums;
using EmsPortal.Application.Common;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>One soft-deleted record surfaced in the Deleted Records Management list.</summary>
/// <param name="EntityId">The deleted record's id.</param>
/// <param name="Identity">A human-readable identifier (e.g. request number, group name) used for display and the hard-delete confirmation token.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="DeletedById">The user who deleted it (resolved to a name by the controller).</param>
/// <param name="DeletedOnUtc">When it was soft-deleted.</param>
public sealed record DeletedRecordRow(Guid EntityId, string Identity, Guid TenantId, Guid? DeletedById, DateTime? DeletedOnUtc);

/// <summary>
/// Generic access to soft-deleted records across the entity types that support Deleted Records
/// Management.
/// </summary>
public interface IDeletedRecordsRepository
{
    /// <summary>Whether Deleted Records Management is implemented for the given entity type.</summary>
    bool IsSupported(EntityType entityType);

    /// <summary>Paginated soft-deleted records of an entity type, scoped to <paramref name="tenantId"/> when given (else the ambient tenant).</summary>
    Task<(IReadOnlyList<DeletedRecordRow> Items, int Total)> ListDeletedAsync(
        EntityType entityType, Guid? tenantId, SortRequest sort, int page, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The identity string of a soft-deleted record (for confirmation-token validation), or null when
    /// not found.
    /// </summary>
    Task<string?> GetDeletedIdentityAsync(EntityType entityType, Guid entityId, Guid? tenantId, CancellationToken cancellationToken = default);

    /// <summary>Restores a soft-deleted record and its soft-deleted UF rows.</summary>
    Task<bool> RestoreAsync(EntityType entityType, Guid entityId, Guid? tenantId, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a soft-deleted record and cascades all its UF rows.</summary>
    Task<bool> HardDeleteAsync(EntityType entityType, Guid entityId, Guid? tenantId, CancellationToken cancellationToken = default);

    /// <summary>Counts records past their retention period, keyed by supported entity type.</summary>
    Task<IReadOnlyDictionary<EntityType, int>> CountOverdueAsync(int retentionDays, Guid? tenantId, CancellationToken cancellationToken = default);
}
