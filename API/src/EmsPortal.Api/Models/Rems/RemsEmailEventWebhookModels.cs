using System.Text.Json;

namespace EmsPortal.Api.Models.Rems;

/// <summary>Inbound provider email-event webhook payload (WO-121).</summary>
public sealed class RemsEmailEventWebhookRequest
{
    /// <summary>A batch of events.</summary>
    public List<RemsEmailEventNotification?>? Events { get; set; }

    // ---- Single-event form (fields flattened onto the root object) ----

    /// <summary>
    /// Provider message id echoed from the outbound Message-ID; correlates to the anchoring Sent
    /// event.
    /// </summary>
    public string? ProviderMessageId { get; set; }

    /// <summary>delivered | opened | failed | sent (case-insensitive).</summary>
    public string? EventType { get; set; }

    /// <summary>Recipient email address the event concerns.</summary>
    public string? RecipientEmail { get; set; }

    /// <summary>When the event occurred (interpreted as UTC).</summary>
    public DateTime? OccurredOnUtc { get; set; }

    /// <summary>Optional raw provider detail, stored verbatim as JSON on the event.</summary>
    public JsonElement? ProviderPayload { get; set; }

    /// <summary>Normalizes the request to a flat list of events (the batch, else the single top-level event).</summary>
    public IReadOnlyList<RemsEmailEventNotification> ToEvents()
    {
        if (Events is { Count: > 0 })
        {
            return Events.Where(e => e is not null).Select(e => e!).ToList();
        }

        // A single flattened event only counts when at least one field was actually supplied — an empty
        // object yields no events, which the caller treats as a malformed body.
        if (ProviderMessageId is null && EventType is null && RecipientEmail is null
            && OccurredOnUtc is null && ProviderPayload is null)
        {
            return Array.Empty<RemsEmailEventNotification>();
        }

        return new[]
        {
            new RemsEmailEventNotification
            {
                ProviderMessageId = ProviderMessageId,
                EventType = EventType,
                RecipientEmail = RecipientEmail,
                OccurredOnUtc = OccurredOnUtc,
                ProviderPayload = ProviderPayload,
            },
        };
    }
}

/// <summary>One provider-reported email delivery event (WO-121).</summary>
public sealed class RemsEmailEventNotification
{
    /// <summary>
    /// Provider message id echoed from the outbound Message-ID; correlates to the anchoring Sent
    /// event.
    /// </summary>
    public string? ProviderMessageId { get; set; }

    /// <summary>delivered | opened | failed | sent (case-insensitive).</summary>
    public string? EventType { get; set; }

    /// <summary>Recipient email address the event concerns.</summary>
    public string? RecipientEmail { get; set; }

    /// <summary>When the event occurred (interpreted as UTC).</summary>
    public DateTime? OccurredOnUtc { get; set; }

    /// <summary>Optional raw provider detail, stored verbatim as JSON on the event.</summary>
    public JsonElement? ProviderPayload { get; set; }

    /// <summary>The provider payload's raw JSON text, or null when absent/empty.</summary>
    public string? RawProviderPayload()
        => ProviderPayload is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } element
            ? element.GetRawText()
            : null;
}

/// <summary>
/// Webhook ingestion outcome (WO-121): per-request counts, returned with 200 so providers never
/// retry-storm.
/// </summary>
public sealed record RemsEmailEventIngestResult(int Processed, int Duplicates, int Ignored);
