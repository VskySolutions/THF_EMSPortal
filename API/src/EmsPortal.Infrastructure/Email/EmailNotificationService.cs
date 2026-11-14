using EmsPortal.Application.Abstractions.Email;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Application.Email;
using EmsPortal.Domain.Entities;
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
    private readonly ITenantEmailSettingsRepository _emailSettings;
    private readonly ICredentialEncryptionService _encryption;
    private readonly ISmtpEmailSender _sender;
    private readonly ITenantRepository _tenants;
    private readonly IEmailDeliveryFailureSink _failures;
    private readonly IEmailLogRepository _logs;
    private readonly AppOptions _appOptions;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(
        IEmailTemplateService templates,
        ISmtpAccountRepository smtpAccounts,
        ITenantEmailSettingsRepository emailSettings,
        ICredentialEncryptionService encryption,
        ISmtpEmailSender sender,
        ITenantRepository tenants,
        IEmailDeliveryFailureSink failures,
        IEmailLogRepository logs,
        IOptions<AppOptions> appOptions,
        ILogger<EmailNotificationService> logger)
    {
        _templates = templates;
        _smtpAccounts = smtpAccounts;
        _emailSettings = emailSettings;
        _encryption = encryption;
        _sender = sender;
        _tenants = tenants;
        _failures = failures;
        _logs = logs;
        _appOptions = appOptions.Value;
        _logger = logger;
    }

    public async Task<bool> HasActiveSenderAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await NotificationsEnabledAsync(tenantId, cancellationToken)
            && await _smtpAccounts.GetActiveAsync(tenantId, cancellationToken) is not null;

    /// <summary>The tenant-wide switch. No settings row means it was never touched, and the default is on.</summary>
    private async Task<bool> NotificationsEnabledAsync(Guid tenantId, CancellationToken cancellationToken)
        => (await _emailSettings.GetByTenantAsync(tenantId, cancellationToken))?.NotificationsEnabled ?? true;

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
            // The tenant-wide switch comes before the account: an admin who turned notifications off
            // expects silence even with a working account beneath it.
            if (!await NotificationsEnabledAsync(tenantId, cancellationToken))
            {
                _logger.LogInformation("Skipping {TemplateKey} email for tenant {TenantId}: email notifications are turned off.", key, tenantId);
                await RecordFailureAsync(tenantId, key, toEmail, messageId,
                    EmailDeliveryFailureReason.NotificationsDisabled, null, cancellationToken);
                await LogAsync(tenantId, key, toEmail, EmailLogStatus.Skipped, "Email notifications are turned off.", cancellationToken: cancellationToken);
                return false;
            }

            var account = await _smtpAccounts.GetActiveAsync(tenantId, cancellationToken);
            if (account is null)
            {
                _logger.LogInformation("Skipping {TemplateKey} email for tenant {TenantId}: no active SMTP account.", key, tenantId);
                await RecordFailureAsync(tenantId, key, toEmail, messageId,
                    EmailDeliveryFailureReason.NoActiveSmtpAccount, null, cancellationToken);
                await LogAsync(tenantId, key, toEmail, EmailLogStatus.Skipped, "The tenant has no active email account.", cancellationToken: cancellationToken);
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
                await LogAsync(tenantId, key, toEmail, EmailLogStatus.Failed, "No email template is available.", cancellationToken: cancellationToken);
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
            // The log row's id is fixed before the send: the tracking image inside the email names it.
            var logId = Guid.NewGuid();
            var message = new SmtpMessage(toEmail.Trim(), subject, body + TrackingImage(logId), IsHtml: true, MessageId: messageId);
            var result = await _sender.SendAsync(credentials, message, cancellationToken);
            await LogAsync(
                tenantId, key, toEmail, result.Success ? EmailLogStatus.Sent : EmailLogStatus.Failed,
                result.Success ? null : DescribeSmtpError(result), logId, subject, Masked(body, merged), cancellationToken);
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
            await LogAsync(tenantId, key, toEmail, EmailLogStatus.Failed, ex.Message, cancellationToken: cancellationToken);
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

    private const string TemporaryPasswordKey = "TemporaryPassword";
    private const int ReasonMax = 1000;
    private const int SubjectMax = 500;

    // A 1x1 image served by this API; loading it is what "opened" means in the Email Log.
    private string TrackingImage(Guid logId)
    {
        var api = _appOptions.ApiBaseUrl?.Trim().TrimEnd('/');
        return string.IsNullOrEmpty(api)
            ? string.Empty
            : $"<img src=\"{api}/api/email-logs/{logId}/open.gif\" width=\"1\" height=\"1\" alt=\"\" />";
    }

    // The log keeps what was sent, less a temporary password: that is for its reader alone.
    private static string Masked(string body, IReadOnlyDictionary<string, string?> model)
        => model.TryGetValue(TemporaryPasswordKey, out var password) && !string.IsNullOrEmpty(password)
            ? body.Replace(password, "********", StringComparison.Ordinal)
            : body;

    /// <summary>One row in the Email Log per attempt. Best-effort: a log that cannot be written never stops a send.</summary>
    private async Task LogAsync(
        Guid tenantId, EmailTemplateKey key, string toEmail, EmailLogStatus status, string? reason,
        Guid? id = null, string? subject = null, string? body = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await _logs.AddAsync(new EmailLog
            {
                Id = id ?? Guid.NewGuid(),
                TenantId = tenantId,
                TemplateKey = key,
                RecipientEmail = toEmail.Trim(),
                Subject = subject is null ? string.Empty : subject.Length <= SubjectMax ? subject : subject[..SubjectMax],
                Body = body,
                Status = status,
                FailureReason = reason is null ? null : reason.Length <= ReasonMax ? reason : reason[..ReasonMax],
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write the email log row for a {TemplateKey} email of tenant {TenantId}.", key, tenantId);
        }
    }

    /// <summary>The SMTP error as a single readable clause; the category alone when the server gave no message.</summary>
    private static string DescribeSmtpError(SmtpSendResult result)
        => string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? $"({result.ErrorCategory})"
            : $"({result.ErrorCategory}) {result.ErrorMessage.Trim()}";
}
