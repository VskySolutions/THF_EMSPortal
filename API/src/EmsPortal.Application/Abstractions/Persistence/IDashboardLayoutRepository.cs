using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>Data access for a user's personalised <see cref="DashboardLayout"/>.</summary>
public interface IDashboardLayoutRepository
{
    /// <summary>The active layout for a user, or null when none has been saved.</summary>
    Task<DashboardLayout?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new layout when none exists for the user, otherwise updates the existing row's JSON
    /// columns.
    /// </summary>
    Task UpsertAsync(DashboardLayout layout, CancellationToken cancellationToken = default);
}
