using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Email;

/// <summary>
/// Enqueues transactional emails for background delivery so the request thread is never blocked on the
/// SMTP round-trip.
/// </summary>
public interface IEmailDispatcher
{
    /// <summary>Queues a transactional email for best-effort delivery in the background (fire-and-forget).</summary>
    void Enqueue(Guid tenantId, EmailTemplateKey key, string? toEmail, IReadOnlyDictionary<string, string?> model, string? messageId = null);

    /// <summary>
    /// As <see cref="Enqueue"/>, but sends a subject / body the caller already composed — what an
    /// admin edited in a send dialog before confirming.
    /// </summary>
    void EnqueueComposed(
        Guid tenantId,
        EmailTemplateKey key,
        string? toEmail,
        IReadOnlyDictionary<string, string?> model,
        string? subject,
        string? body,
        string? messageId = null);
}
