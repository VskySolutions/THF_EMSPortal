using System.Text.Json;
using EmsPortal.Application.Abstractions.Email;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace EmsPortal.Application.Email;

/// <summary>
/// Turns a swallowed email-delivery failure into a visible <see cref="RemsFormEmailEventType.Failed"/>
/// row on the REMS form's email log. Without this, <c>RemsFormController.Send</c>'s optimistic
/// <c>Sent</c> event is the only thing the Email Log ever shows — so a form whose email was never
/// delivered (no active SMTP account, rejected credentials, unreachable host) reads as sent.
/// <para>
/// Correlation, idempotency and tenant stamping work exactly as they do for the WO-121 provider webhook:
/// the failure is matched to the anchoring <c>Sent</c> event by <c>ProviderMessageId</c> (resolved
/// unscoped, since a Hangfire worker has no ambient tenant of its own), the tenant comes from that
/// anchor, and the filtered unique index <c>(TenantId, ProviderMessageId, EventType)</c> keeps a retried
/// job from appending a second Failed row. A message id that matches nothing is not a REMS email and is
/// ignored.
/// </para>
/// </summary>
internal sealed class RemsEmailDeliveryFailureSink : IEmailDeliveryFailureSink
{
    /// <summary>Marks a payload as one this portal wrote, so the API never surfaces raw provider JSON as a reason.</summary>
    private const string PortalSource = "portal";

    private readonly IRemsFormRepository _forms;
    private readonly ILogger<RemsEmailDeliveryFailureSink> _logger;

    public RemsEmailDeliveryFailureSink(IRemsFormRepository forms, ILogger<RemsEmailDeliveryFailureSink> logger)
    {
        _forms = forms;
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
            var anchor = await _forms.GetSentEventByProviderMessageIdUnscopedAsync(messageId, cancellationToken);
            if (anchor is null)
            {
                // Not a REMS form-link email (or its Sent anchor is gone) — there is no log to append to.
                return;
            }

            if (await _forms.EmailEventExistsAsync(anchor.TenantId, messageId, RemsFormEmailEventType.Failed, cancellationToken))
            {
                return;
            }

            var recorded = await _forms.TryAppendProviderEmailEventAsync(new REMSFormEmailEvent
            {
                Id = Guid.NewGuid(),
                REMSFormId = anchor.REMSFormId,
                // Set explicitly from the anchor: a background job's DbContext may have no resolved tenant
                // to stamp, and this row must never land with Guid.Empty.
                TenantId = anchor.TenantId,
                ProviderMessageId = messageId,
                EventType = RemsFormEmailEventType.Failed,
                RecipientEmail = Normalize(failure.Recipient) ?? anchor.RecipientEmail,
                OccurredOnUtc = DateTime.UtcNow,
                ProviderPayload = BuildPayload(failure),
            }, cancellationToken);

            if (recorded)
            {
                _logger.LogInformation(
                    "Recorded a Failed email event on REMS form {FormId} for message {MessageId}: {Reason}.",
                    anchor.REMSFormId, messageId, failure.Reason);
            }
        }
        catch (Exception ex)
        {
            // This runs inside the best-effort email path: recording a failure must never become one.
            _logger.LogWarning(ex, "Could not record the Failed email event for message {MessageId}.", messageId);
        }
    }

    /// <summary>
    /// The stored payload. Shaped like a provider webhook body (the column's contract) but tagged with
    /// <see cref="PortalSource"/> so the Email Log can safely render <c>message</c> as the failure reason.
    /// </summary>
    private static string BuildPayload(EmailDeliveryFailure failure)
        => JsonSerializer.Serialize(new
        {
            source = PortalSource,
            reason = failure.Reason.ToString(),
            templateKey = failure.Key.ToString(),
            message = BuildMessage(failure),
        });

    /// <summary>A reason an Admin can act on, not an enum name.</summary>
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

    /// <summary>Trims whitespace and any RFC-5322 angle brackets, matching how the webhook stores the id.</summary>
    private static string? NormalizeMessageId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('<', '>').Trim() is { Length: > 0 } id ? id : null;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
