using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Email;

/// <summary>
/// Sends a transactional email for a tenant: resolves the effective template, renders it with the
/// supplied model (augmented with tenant name + app login URL).
/// </summary>
public interface IEmailNotificationService
{
    Task<bool> SendAsync(
        Guid tenantId,
        EmailTemplateKey key,
        string? toEmail,
        IReadOnlyDictionary<string, string?> model,
        string? messageId = null,
        // An already-composed subject / body that REPLACE the rendered template's — what an admin edited in a
        // send dialog before confirming.
        string? subjectOverride = null,
        string? bodyOverride = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fast check (no SMTP round-trip) of whether a queued email will actually be attempted: the tenant
    /// has not turned its notifications off, and it has an active SMTP account.
    /// </summary>
    Task<bool> HasActiveSenderAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
