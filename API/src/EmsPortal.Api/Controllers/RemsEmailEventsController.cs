using System.Security.Cryptography;
using System.Text;
using EmsPortal.Api.Models.Rems;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Configuration;
using EmsPortal.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Ingestion endpoint for provider-reported EMS email delivery events (WO-121): the delivered / opened
/// / failed / sent callbacks a mail-delivery provider posts back for a REMS form-link email.
/// </summary>
[ApiController]
[Route("api/rems/email-events")]
[AllowAnonymous]
[Produces("application/json")]
[Tags("REMS Email Events")]
public sealed class RemsEmailEventsController : ControllerBase
{
    /// <summary>Request header carrying the shared webhook secret.</summary>
    public const string SecretHeaderName = "X-Rems-Webhook-Secret";

    private readonly IRemsFormRepository _forms;
    private readonly string _webhookSecret;
    private readonly ILogger<RemsEmailEventsController> _logger;

    public RemsEmailEventsController(
        IRemsFormRepository forms,
        IOptions<RemsWebhookOptions> webhookOptions,
        ILogger<RemsEmailEventsController> logger)
    {
        _forms = forms;
        _webhookSecret = webhookOptions.Value.Secret ?? string.Empty;
        _logger = logger;
    }

    /// <summary>Ingest a single event or a batch.</summary>
    [HttpPost]
    [ProducesResponseType<ApiResponse<RemsEmailEventIngestResult>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Ingest(
        [FromBody] RemsEmailEventWebhookRequest? request,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("Invalid or missing webhook secret."));
        }

        if (request is null)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Malformed webhook body.", "The request body could not be parsed."));
        }

        var events = request.ToEvents();
        if (events.Count == 0)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Malformed webhook body.", "No email events were supplied."));
        }

        var processed = 0;
        var duplicates = 0;
        var ignored = 0;

        foreach (var evt in events)
        {
            switch (await ProcessAsync(evt, cancellationToken))
            {
                case IngestOutcome.Processed: processed++; break;
                case IngestOutcome.Duplicate: duplicates++; break;
                default: ignored++; break;
            }
        }

        _logger.LogInformation(
            "REMS email-event webhook processed a batch of {Count}: {Processed} processed, {Duplicates} duplicates, {Ignored} ignored.",
            events.Count, processed, duplicates, ignored);

        return Ok(ApiResponseFactory.Success(
            new RemsEmailEventIngestResult(processed, duplicates, ignored),
            "REMS email events ingested."));
    }

    /// <summary>
    /// Resolves one event: skip (ignore) anything malformed or unmatched; skip (duplicate) anything
    /// already recorded; otherwise append a new email event stamped with the anchor's tenant + form.
    /// </summary>
    private async Task<IngestOutcome> ProcessAsync(RemsEmailEventNotification evt, CancellationToken cancellationToken)
    {
        // Validate the event shape; anything malformed is skipped (ignored) with no state change.
        var providerMessageId = NormalizeMessageId(evt.ProviderMessageId);
        if (providerMessageId is null
            || string.IsNullOrWhiteSpace(evt.RecipientEmail)
            || evt.OccurredOnUtc is not { } occurredOn
            || !TryMapEventType(evt.EventType, out var eventType))
        {
            return IngestOutcome.Ignored;
        }

        // Correlate to the anchoring Sent event (unscoped — no tenant context on a webhook) to resolve the
        // owning tenant + form. An unmatched message id is skipped without changing REMS state.
        var anchor = await _forms.GetSentEventByProviderMessageIdUnscopedAsync(providerMessageId, cancellationToken);
        if (anchor is null)
        {
            return IngestOutcome.Ignored;
        }

        // Idempotency: the filtered unique index (TenantId, ProviderMessageId, EventType) is the guard.
        if (await _forms.EmailEventExistsAsync(anchor.TenantId, providerMessageId, eventType, cancellationToken))
        {
            return IngestOutcome.Duplicate;
        }

        var emailEvent = new REMSFormEmailEvent
        {
            Id = Guid.NewGuid(),
            REMSFormId = anchor.REMSFormId,
            // Inserted from an unauthenticated context: the DbContext StampTenant is a no-op with no resolved
            // tenant, so the tenant is set EXPLICITLY from the anchor and is therefore never Guid.Empty.
            TenantId = anchor.TenantId,
            ProviderMessageId = providerMessageId,
            EventType = eventType,
            RecipientEmail = evt.RecipientEmail.Trim(),
            OccurredOnUtc = ToUtc(occurredOn),
            ProviderPayload = evt.RawProviderPayload(),
        };

        var inserted = await _forms.TryAppendProviderEmailEventAsync(emailEvent, cancellationToken);
        return inserted ? IngestOutcome.Processed : IngestOutcome.Duplicate;
    }

    /// <summary>
    /// Fail-closed, constant-time secret check: rejects unless a non-empty configured secret matches
    /// the header exactly.
    /// </summary>
    private bool IsAuthorized()
    {
        if (string.IsNullOrEmpty(_webhookSecret))
        {
            return false; // Fail closed: no secret configured → reject every call.
        }

        if (!Request.Headers.TryGetValue(SecretHeaderName, out var presented) || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(_webhookSecret);
        var presentedBytes = Encoding.UTF8.GetBytes(presented.ToString());
        return CryptographicOperations.FixedTimeEquals(expectedBytes, presentedBytes);
    }

    /// <summary>
    /// Trims whitespace and any RFC-5322 angle brackets so a bracketed provider echo still matches the
    /// stored id.
    /// </summary>
    private static string? NormalizeMessageId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('<', '>').Trim() is { Length: > 0 } id ? id : null;

    private static bool TryMapEventType(string? value, out RemsFormEmailEventType eventType)
        => Enum.TryParse(value, ignoreCase: true, out eventType) && Enum.IsDefined(eventType);

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private enum IngestOutcome
    {
        Processed,
        Duplicate,
        Ignored,
    }
}
