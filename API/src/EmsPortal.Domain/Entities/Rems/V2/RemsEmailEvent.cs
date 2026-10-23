using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// An email the request sent and what became of it. Append-only. The subject and body are kept as sent,
/// because the template varies by department and the log must show what went out; the provider's
/// webhook payload is not kept.
/// </summary>
public class RemsEmailEvent : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid REMSId { get; set; }
    public Guid? EmailTemplateId { get; set; }
    public RemsFormEmailEventType EventType { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public string? ProviderMessageId { get; set; }
    public DateTime OccurredOnUtc { get; set; }
    public string? FailureReason { get; set; }

    public RemsRequest? Request { get; set; }
    public EmailTemplate? EmailTemplate { get; set; }
}
