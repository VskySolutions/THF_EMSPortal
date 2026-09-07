using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Email;

/// <summary>A delivery-tracked email that never reached its recipient.</summary>
public sealed record EmailDeliveryFailure(
    Guid TenantId,
    EmailTemplateKey Key,
    string MessageId,
    string? Recipient,
    EmailDeliveryFailureReason Reason,
    string? Detail);

/// <summary>Receives the failures the best-effort email pipeline would otherwise only write to the log.</summary>
public interface IEmailDeliveryFailureSink
{
    Task RecordAsync(EmailDeliveryFailure failure, CancellationToken cancellationToken = default);
}
