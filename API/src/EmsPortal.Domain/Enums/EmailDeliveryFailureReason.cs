namespace EmsPortal.Domain.Enums;

/// <summary>
/// Why a queued transactional email never reached its recipient. The email pipeline is best-effort and
/// swallows delivery problems so the originating action is never blocked; this reason is what it reports
/// to <c>IEmailDeliveryFailureSink</c> so a delivery-tracked send (the REMS form-link email) can record a
/// <see cref="RemsFormEmailEventType.Failed"/> event instead of failing silently.
/// </summary>
public enum EmailDeliveryFailureReason
{
    /// <summary>The tenant has no active SMTP account, so no send was attempted.</summary>
    NoActiveSmtpAccount,

    /// <summary>The tenant has turned its email notifications off, so no send was attempted.</summary>
    NotificationsDisabled,

    /// <summary>The effective template could not be resolved or rendered.</summary>
    TemplateUnavailable,

    /// <summary>The SMTP server refused the message or could not be reached.</summary>
    SmtpSendFailed,

    /// <summary>An unexpected error aborted the send.</summary>
    UnexpectedError,
}
