using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Email;

/// <summary>Decrypted SMTP connection settings handed to <see cref="ISmtpEmailSender"/>.</summary>
public sealed record SmtpAccountCredentials(
    string Host,
    int Port,
    SmtpEncryptionType EncryptionType,
    SmtpAuthType AuthType,
    string? Username,
    string? Password,
    string FromName,
    string FromEmail);

/// <summary>A message to be dispatched via <see cref="ISmtpEmailSender"/>.</summary>
public sealed record SmtpMessage(
    string ToEmail,
    string Subject,
    string Body,
    bool IsHtml = false,
    string? MessageId = null);

/// <summary>Low-level outcome of a single SMTP send attempt.</summary>
public sealed record SmtpSendResult(
    bool Success,
    string? ServerResponse,
    string? ErrorMessage,
    SmtpErrorCategory? ErrorCategory)
{
    public static SmtpSendResult Ok(string? serverResponse)
        => new(true, serverResponse, null, null);

    public static SmtpSendResult Failure(SmtpErrorCategory category, string errorMessage)
        => new(false, null, errorMessage, category);
}

/// <summary>
/// Result of a test send, as returned by <c>ISmtpAccountService.TestSendAsync</c> and the API.
/// Failures are reported as a result (never an exception) so the caller always gets structured detail.
/// </summary>
public sealed record SmtpTestResult(
    bool Success,
    DateTime? SentAtUtc,
    string? ServerResponse,
    SmtpErrorCategory? ErrorCategory,
    string? ErrorDetail);
