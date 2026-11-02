namespace EmsPortal.Domain.Enums;

/// <summary>
/// The transactional email allowlist (WO-124, AC-ETPL-005.5). Only account-security and REMS external
/// templates may be delivered by email; every other <see cref="EmailTemplateKey"/> — mentions, reminders
/// and any future internal workflow alert — is in-app only. Enforced centrally by the email dispatch
/// pipeline so no caller can send email for a non-allowlisted type.
/// <para>
/// EVERY ACCOUNT-SECURITY KEY BELONGS HERE. The gate drops a non-allowlisted send with nothing but a
/// warning log, and the endpoints behind these keys deliberately answer the same either way — the
/// forgot-password flow returns its neutral "if that address exists we have emailed it" whether or not
/// anything was sent. So a key missing from this list is invisible from the outside: it does not fail,
/// it just never arrives. <see cref="EmailTemplateKey.PasswordResetLink"/> was missing and locked users
/// out of self-service reset entirely.
/// </para>
/// </summary>
public static class EmailSendPolicy
{
    private static readonly HashSet<EmailTemplateKey> EmailAllowed = new()
    {
        EmailTemplateKey.UserInvitation,    // account security — invitation
        EmailTemplateKey.PasswordReset,     // account security — admin-initiated password reset
        EmailTemplateKey.PasswordResetLink, // account security — self-service "forgot password" link
        EmailTemplateKey.PasswordChanged,   // account security — password-change confirmation
        EmailTemplateKey.Welcome,           // account security — account activated
        EmailTemplateKey.RemsFormLink,      // REMS external — client form link
        EmailTemplateKey.RemsFormReminder,  // REMS external — nudge a client who has not submitted yet
        EmailTemplateKey.RemsFormSubmitted, // REMS external — form submitted to Admin + CSE
        EmailTemplateKey.MaconomyConnectionDisabled, // operations — the tenant's developers, when the portal switches Maconomy off
    };

    /// <summary>True when the template key is permitted to be delivered by email.</summary>
    public static bool IsEmailAllowed(EmailTemplateKey key) => EmailAllowed.Contains(key);

    /// <summary>The template keys permitted to be delivered by email.</summary>
    public static IReadOnlyCollection<EmailTemplateKey> AllowedKeys => EmailAllowed;
}
