using EmsPortal.Application.Abstractions.Email;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace EmsPortal.Application.Email;

/// <summary>
/// Records a portal-side delivery failure against the request's email log, anchored on the Sent event that
/// carries the same message id. One Failed event per message; a second report is dropped.
/// </summary>
internal sealed class RemsEmailDeliveryFailureSink : IEmailDeliveryFailureSink
{
    private const int ReasonMax = 1000;

    private readonly IRemsRequestRepository _requests;
    private readonly ILogger<RemsEmailDeliveryFailureSink> _logger;

    public RemsEmailDeliveryFailureSink(IRemsRequestRepository requests, ILogger<RemsEmailDeliveryFailureSink> logger)
    {
        _requests = requests;
        _logger = logger;
    }

    public async Task RecordAsync(EmailDeliveryFailure failure, CancellationToken cancellationToken = default)
    {
        var messageId = NormalizeMessageId(failure.MessageId);
        if (messageId is null)
        {
            return;
        }
        try
        {
            var anchor = await _requests.GetSentEventByProviderMessageIdUnscopedAsync(messageId, cancellationToken);
            if (anchor is null)
            {
                return;
            }
            if (await _requests.EmailEventExistsAsync(anchor.TenantId, messageId, RemsFormEmailEventType.Failed, cancellationToken))
            {
                return;
            }
            var reason = BuildMessage(failure);
            var recorded = await _requests.TryAppendProviderEmailEventAsync(new RemsEmailEvent
            {
                Id = Guid.NewGuid(),
                REMSId = anchor.REMSId,
                TenantId = anchor.TenantId,
                EmailTemplateId = anchor.EmailTemplateId,
                ProviderMessageId = messageId,
                EventType = RemsFormEmailEventType.Failed,
                RecipientEmail = Normalize(failure.Recipient) ?? anchor.RecipientEmail,
                Subject = anchor.Subject,
                OccurredOnUtc = DateTime.UtcNow,
                FailureReason = reason.Length <= ReasonMax ? reason : reason[..ReasonMax],
            }, cancellationToken);
            if (recorded)
            {
                _logger.LogInformation(
                    "Recorded a Failed email event on request {RemsId} for message {MessageId}: {Reason}.",
                    anchor.REMSId, messageId, failure.Reason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record the Failed email event for message {MessageId}.", messageId);
        }
    }

    private static string BuildMessage(EmailDeliveryFailure failure)
    {
        var reason = failure.Reason switch
        {
            EmailDeliveryFailureReason.NoActiveSmtpAccount =>
                "No active SMTP account is configured for this tenant, so the email was never attempted.",
            EmailDeliveryFailureReason.NotificationsDisabled =>
                "Email notifications are turned off for this tenant, so the email was never attempted.",
            EmailDeliveryFailureReason.TemplateUnavailable =>
                "The email template could not be rendered.",
            EmailDeliveryFailureReason.SmtpSendFailed =>
                "The mail server refused the message.",
            _ => "An unexpected error stopped the send.",
        };
        return Normalize(failure.Detail) is { } detail ? $"{reason} {detail}" : reason;
    }

    private static string? NormalizeMessageId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('<', '>').Trim() is { Length: > 0 } id ? id : null;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
