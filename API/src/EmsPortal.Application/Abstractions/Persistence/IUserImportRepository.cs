using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>Data access for the user import log, scoped by an explicit tenant id.</summary>
public interface IUserImportRepository
{
    /// <summary>Stages the import with its rows.</summary>
    Task AddAsync(UserImport import, CancellationToken cancellationToken = default);

    /// <summary>Newest first, without the rows.</summary>
    Task<(IReadOnlyList<UserImport> Items, int Total)> ListAsync(
        Guid tenantId, int page, int limit, CancellationToken cancellationToken = default);

    /// <summary>One import with its rows in file order.</summary>
    Task<UserImport?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
}
