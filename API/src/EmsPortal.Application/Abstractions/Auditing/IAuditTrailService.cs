namespace EmsPortal.Application.Abstractions.Auditing;

/// <summary>Writes append-only audit entries.</summary>
public interface IAuditTrailService
{
    /// <summary>Stages an audit entry.</summary>
    Task AddAsync(
        string entityName,
        string entityId,
        string action,
        string? details = null,
        string? performedBy = null,
        CancellationToken cancellationToken = default);
}
