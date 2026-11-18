using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>What the Email Log list is narrowed by; every part optional.</summary>
public sealed record EmailLogFilter(
    string? Search = null,
    EmailLogStatus? Status = null,
    EmailTemplateKey? TemplateKey = null,
    bool? IsOpened = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

/// <summary>
/// Data access for the email log. Scoped by an explicit tenant id: the send job and the tracking image
/// both run with no tenant resolved.
/// </summary>
public interface IEmailLogRepository
{
    /// <summary>Writes one row and saves it at once.</summary>
    Task AddAsync(EmailLog log, CancellationToken cancellationToken = default);

    /// <summary>Newest first, without the bodies.</summary>
    Task<(IReadOnlyList<EmailLog> Rows, int Total)> ListAsync(
        Guid tenantId, EmailLogFilter filter, int page, int limit, CancellationToken cancellationToken = default);

    Task<EmailLog?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Counts one load of the tracking image. Does nothing for an id that is not a sent email.</summary>
    Task MarkOpenedAsync(Guid id, DateTime openedOnUtc, CancellationToken cancellationToken = default);
}
