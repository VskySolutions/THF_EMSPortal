using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>Data access for a user's <see cref="Pin"/>s (bookmarks).</summary>
public interface IPinRepository
{
    Task AddAsync(Pin pin, CancellationToken cancellationToken = default);

    Task<Pin?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    Task<Pin?> GetAsync(Guid userId, EntityType entityType, Guid entityId, CancellationToken cancellationToken = default);

    void Remove(Pin pin);

    Task<int> CountByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Number of records of a given type the user has pinned (per-type pin cap).</summary>
    Task<int> CountByUserAndTypeAsync(Guid userId, EntityType entityType, CancellationToken cancellationToken = default);

    /// <summary>
    /// The entity ids of the user's pinned records of a given type (e.g. to float them to the top of a
    /// list).
    /// </summary>
    Task<IReadOnlyList<Guid>> ListEntityIdsByUserAndTypeAsync(Guid userId, EntityType entityType, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Pin> Items, int Total)> ListByUserAsync(Guid userId, int page, int limit, CancellationToken cancellationToken = default);
}
