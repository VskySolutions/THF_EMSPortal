namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// Commits all pending changes staged on the repositories sharing the current
/// scoped database context as a single atomic transaction. This is the seam that
/// lets an action and its audit-trail entry be written together (see
/// <see cref="IAuditTrailRepository"/>).
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Persists all staged changes in one transaction.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops everything staged but not saved, so a failed save is not retried by the next one.</summary>
    void DiscardChanges();

    /// <summary>
    /// Runs <paramref name="operation"/> inside an explicit database transaction and commits it on
    /// success (rolling back on failure). Use when an invariant requires multiple ordered
    /// <see cref="SaveChangesAsync"/> calls to commit atomically — e.g. the SMTP active-account swap,
    /// where the current active row must be deactivated before the target is activated to satisfy the
    /// "single active per tenant" unique index (SMTP Email Accounts ADR-003).
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}
