namespace EmsPortal.Application.Abstractions.Email;

/// <summary>
/// Low-level SMTP client: opens a connection to the configured host/port, applies the requested
/// transport security and authentication scheme, sends a single message, and closes the connection.
/// </summary>
public interface ISmtpEmailSender
{
    Task<SmtpSendResult> SendAsync(SmtpAccountCredentials credentials, SmtpMessage message, CancellationToken cancellationToken = default);
}
