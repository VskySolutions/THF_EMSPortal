using EmsPortal.Application.Abstractions.Email;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Email;

/// <summary>
/// Default <see cref="IRemsEmailNotifier"/>. Maps the typed REMS models onto the placeholder tokens the
/// seeded templates expect and enqueues them through <see cref="IEmailDispatcher"/> for best-effort
/// background delivery. Template resolution, the no-active-SMTP-account skip and failure swallowing all
/// happen downstream in the shared email pipeline (<c>EmailNotificationService</c>).
/// </summary>
internal sealed class RemsEmailNotifier : IRemsEmailNotifier
{
    private readonly IEmailDispatcher _dispatcher;

    public RemsEmailNotifier(IEmailDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void SendFormLink(Guid tenantId, string toEmail, RemsFormLinkEmail model, string? messageId = null)
        => _dispatcher.Enqueue(tenantId, EmailTemplateKey.RemsFormLink, toEmail, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ClientName"] = model.ClientName,
            ["FormLink"] = model.FormLink,
            ["RemsNumber"] = model.RemsNumber,
        }, messageId);

    public void SendComposedFormLink(
        Guid tenantId, string toEmail, RemsFormLinkEmail model, string? subject, string? body, string? messageId = null)
        => _dispatcher.EnqueueComposed(tenantId, EmailTemplateKey.RemsFormLink, toEmail, FormLinkModel(model),
            subject, body, messageId);

    public void SendComposedFormReminder(
        Guid tenantId, string toEmail, RemsFormLinkEmail model, string? subject, string? body, string? messageId = null)
        => _dispatcher.EnqueueComposed(tenantId, EmailTemplateKey.RemsFormReminder, toEmail, FormLinkModel(model),
            subject, body, messageId);

    private static Dictionary<string, string?> FormLinkModel(RemsFormLinkEmail model) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["ClientName"] = model.ClientName,
        ["FormLink"] = model.FormLink,
        ["RemsNumber"] = model.RemsNumber,
    };

    public void SendFormSubmitted(Guid tenantId, string toEmail, RemsFormSubmittedEmail model)
        => _dispatcher.Enqueue(tenantId, EmailTemplateKey.RemsFormSubmitted, toEmail, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ClientName"] = model.ClientName,
            ["RemsNumber"] = model.RemsNumber,
            ["RequestLink"] = model.RequestLink,
            ["SubmittedOn"] = model.SubmittedOn,
        });

    public void SendFormReceived(Guid tenantId, string toEmail, RemsFormReceivedEmail model)
        => _dispatcher.Enqueue(tenantId, EmailTemplateKey.RemsFormReceived, toEmail, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ClientName"] = model.ClientName,
            ["RemsNumber"] = model.RemsNumber,
        });
}
