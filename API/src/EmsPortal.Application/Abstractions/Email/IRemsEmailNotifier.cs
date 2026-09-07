using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Email;

/// <summary>Background dispatch of the two REMS external emails (WO-124).</summary>
public interface IRemsEmailNotifier
{
    /// <summary>Queues the "complete your form" email to a client for a REMS request.</summary>
    void SendFormLink(Guid tenantId, string toEmail, RemsFormLinkEmail model, string? messageId = null);

    /// <summary>
    /// As <see cref="SendFormLink"/>, but delivering a subject / body the sending admin composed in
    /// the send dialog rather than the template's own.
    /// </summary>
    void SendComposedFormLink(
        Guid tenantId, string toEmail, RemsFormLinkEmail model, string? subject, string? body, string? messageId = null);

    /// <summary>Queues a reminder to a client who has their form link but has not submitted yet.</summary>
    void SendComposedFormReminder(
        Guid tenantId, string toEmail, RemsFormLinkEmail model, string? subject, string? body, string? messageId = null);

    /// <summary>Queues the "form submitted" email to the assigned Admin + CSE for a REMS request.</summary>
    void SendFormSubmitted(Guid tenantId, string toEmail, RemsFormSubmittedEmail model);
}

/// <summary>Placeholder values for <see cref="EmailTemplateKey.RemsFormLink"/>.</summary>
public sealed record RemsFormLinkEmail(string ClientName, string FormLink, string RemsNumber);

/// <summary>Placeholder values for <see cref="EmailTemplateKey.RemsFormSubmitted"/>.</summary>
public sealed record RemsFormSubmittedEmail(string ClientName, string RemsNumber, string RequestLink, string SubmittedOn);
