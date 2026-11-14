using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// One email the portal tried to send, whatever it was about: what went out, to whom, how the mail
/// server answered and whether it has been opened. Written by the send pipeline, never edited.
/// </summary>
public class EmailLog : AuditableEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public EmailTemplateKey TemplateKey { get; set; }

    public string RecipientEmail { get; set; } = string.Empty;

    /// <summary>Empty when the email was held back before it was written (notifications off, no account).</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The body as sent, a temporary password masked. Null when nothing was written.</summary>
    public string? Body { get; set; }

    public EmailLogStatus Status { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>From the tracking image: approximate, a mail client may block it or load it unasked.</summary>
    public DateTime? FirstOpenedOnUtc { get; set; }

    public DateTime? LastOpenedOnUtc { get; set; }

    public int OpenCount { get; set; }
}
