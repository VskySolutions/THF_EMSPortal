using EmsPortal.Application.Abstractions.Email;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Application.Email;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmsPortal.Infrastructure.Email;

/// <summary>
/// Renders a tenant's effective template and dispatches it through the tenant's active SMTP account.
/// Best-effort: it never throws, logging and returning false when there is no active account or the
/// send fails, so the calling action (user creation, password reset, …) is never blocked.
/// <para>
/// Because nothing upstream sees that false, every failure path also reports to
/// <see cref="IEmailDeliveryFailureSink"/> when the send carries a Message-ID. That is what turns a
/// silently-swallowed failure into a visible Failed row on the originating record's delivery log.
/// </para>
/// </summary>
internal sealed class EmailNotificationService : IEmailNotificationService
{
    private readonly IEmailTemplateService _templates;
    private readonly ISmtpAccountRepository _smtpAccounts;
    private readonly ICredentialEncryptionService _encryption;
    private readonly ISmtpEmailSender _sender;
    private readonly ITenantRepository _tenants;
    private readonly IEmailDeliveryFailureSink _failures;
    private readonly AppOptions _appOptions;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(
        IEmailTemplateService templates,
        ISmtpAccountRepository smtpAccounts,
        ICredentialEncryptionService encryption,
        ISmtpEmailSender sender,
        ITenantRepository tenants,
        IEmailDeliveryFailureSink failures,
        IOptions<AppOptions> appOptions,
        ILogger<EmailNotificationService> logger)
    {
        _templates = templates;
        _smtpAccounts = smtpAccounts;
        _encryption = encryption;
        _sender = sender;
        _tenants = tenants;
        _failures = failures;
        _appOptions = appOptions.Value;
        _logger = logger;
    }

    public async Task<bool> HasActiveSenderAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await _smtpAccounts.GetActiveAsync(tenantId, cancellationToken) is not null;

    public async Task<bool> SendAsync(
        Guid tenantId,
        EmailTemplateKey key,
        string? toEmail,
        IReadOnlyDictionary<string, string?> model,
        string? messageId = null,
        string? subjectOverride = null,
        string? bodyOverride = null,
        CancellationToken cancellationToken = default)
    {
        // Central email allowlist (WO-124, AC-ETPL-005.5): only account-security + REMS external templates
        // may be emailed. Every send funnels through here, so no caller can bypass this gate.
        if (!EmailSendPolicy.IsEmailAllowed(key))
        {
            _logger.LogWarning("Blocked email for template {TemplateKey} (tenant {TenantId}): not on the email allowlist; this type is in-app only.", key, tenantId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(toEmail))
        {
            return false;
        }

        try
        {
            var account = await _smtpAccounts.GetActiveAsync(tenantId, cancellationToken);
            if (account is null)
            {
                _logger.LogInformation("Skipping {TemplateKey} email for tenant {TenantId}: no active SMTP account.", key, tenantId);
                await RecordFailureAsync(tenantId, key, toEmail, messageId,
                    EmailDeliveryFailureReason.NoActiveSmtpAccount, null, cancellationToken);
                return false;
            }

            // Common values every template can use; caller-supplied values take precedence.
            var tenant = await _tenants.GetByIdAsync(tenantId, cancellationToken);
            var merged = CommonEmailPlaceholders.Merge(model, _appOptions.BaseUrl, tenant?.Name);

            var rendered = await _templates.RenderEffectiveAsync(tenantId, key, merged, cancellationToken);
            if (rendered is null)
            {
                _logger.LogWarning("No effective {TemplateKey} template for tenant {TenantId}; nothing was sent.", key, tenantId);
                await RecordFailureAsync(tenantId, key, toEmail, messageId,
                    EmailDeliveryFailureReason.TemplateUnavailable, null, cancellationToken);
                return false;
            }

            var credentials = new SmtpAccountCredentials(
                account.Host,
                account.Port,
                account.EncryptionType,
                account.AuthType,
                account.Username,
                string.IsNullOrEmpty(account.EncryptedPassword) ? null : _encryption.Decrypt(account.EncryptedPassword),
                account.FromName,
                account.FromEmail);

            // An admin who edited the email before sending gets exactly what they wrote, with the same
            // placeholders filled in — a token left standing in their text goes out as its value, not as
            // {{TenantName}}. The template is still resolved and rendered above — it is what they were
            // shown, and its absence is still a reason not to send at all — but their edits win over it.
            var composed = _templates.Render(subjectOverride?.Trim() ?? string.Empty, bodyOverride ?? string.Empty, merged);
            var subject = string.IsNullOrWhiteSpace(subjectOverride) ? rendered.Subject : composed.Subject;
            var body = string.IsNullOrWhiteSpace(bodyOverride) ? rendered.Body : composed.Body;
            var message = new SmtpMessage(toEmail.Trim(), subject, body, IsHtml: true, MessageId: messageId);
            var result = await _sender.SendAsync(credentials, message, cancellationToken);
            if (!result.Success)
            {
                _logger.LogWarning("Failed to send {TemplateKey} email for tenant {TenantId}: {Category} {Error}",
                    key, tenantId, result.ErrorCategory, result.ErrorMessage);
                await RecordFailureAsync(tenantId, key, toEmail, messageId,
                    EmailDeliveryFailureReason.SmtpSendFailed, DescribeSmtpError(result), cancellationToken);
            }
            return result.Success;
        }
        catch (Exception ex)
        {
            // Notifications must never break the primary action.
            _logger.LogWarning(ex, "Error sending {TemplateKey} email for tenant {TenantId}.", key, tenantId);
            await RecordFailureAsync(tenantId, key, toEmail, messageId,
                EmailDeliveryFailureReason.UnexpectedError, ex.Message, cancellationToken);
            return false;
        }
    }

    /// <summary>
    /// Reports a swallowed failure to the sink. Only delivery-tracked sends carry a Message-ID, and it is
    /// what correlates the failure to the record whose log it belongs in — so a send without one has
    /// nowhere to report and is left to the application log alone.
    /// </summary>
    private async Task RecordFailureAsync(
        Guid tenantId,
        EmailTemplateKey key,
        string? toEmail,
        string? messageId,
        EmailDeliveryFailureReason reason,
        string? detail,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return;
        }

        await _failures.RecordAsync(
            new EmailDeliveryFailure(tenantId, key, messageId, toEmail?.Trim(), reason, detail), cancellationToken);
    }

    /// <summary>The SMTP error as a single readable clause; the category alone when the server gave no message.</summary>
    private static string DescribeSmtpError(SmtpSendResult result)
        => string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? $"({result.ErrorCategory})"
            : $"({result.ErrorCategory}) {result.ErrorMessage.Trim()}";
}
