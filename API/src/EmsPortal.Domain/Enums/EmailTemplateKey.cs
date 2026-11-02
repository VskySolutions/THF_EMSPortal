namespace EmsPortal.Domain.Enums;

/// <summary>
/// Identifies a transactional email template. Each key has a seeded platform-default template
/// (global) which a tenant may override. Stored as a string for stability across releases.
/// </summary>
public enum EmailTemplateKey
{
    /// <summary>Sent when a Person is promoted to a user account; carries the temporary password and login link.</summary>
    UserInvitation = 0,

    /// <summary>Sent when an administrator resets a user's password; carries the new temporary password.</summary>
    PasswordReset = 1,

    /// <summary>Confirmation sent after a user changes their own password.</summary>
    PasswordChanged = 2,

    /// <summary>General welcome message sent when an account becomes active.</summary>
    Welcome = 3,

    /// <summary>
    /// A user was @mentioned in a note (Universal Features). In-app only since WO-124 — not on the email
    /// allowlist, so it is never dispatched by email. Template kept for compatibility.
    /// </summary>
    MentionReceived = 11,

    /// <summary>
    /// A reminder a user set has reached its due time (Universal Features). In-app only since WO-124 — not
    /// on the email allowlist, so it is never dispatched by email. Template kept for compatibility.
    /// </summary>
    ReminderDue = 12,

    /// <summary>
    /// REMS external email sent to a client with the secure link to complete an EMS form for a request
    /// (REMS, WO-124). On the email allowlist. Rendered/dispatched via <c>IRemsEmailNotifier</c>.
    /// </summary>
    RemsFormLink = 13,

    /// <summary>
    /// REMS external email sent to the assigned Admin + CSE confirming a client submitted their EMS form
    /// (REMS, WO-124). On the email allowlist. Rendered/dispatched via <c>IRemsEmailNotifier</c>.
    /// </summary>
    RemsFormSubmitted = 14,

    /// <summary>Self-service "forgot password": carries the one-time reset link, never a password.</summary>
    PasswordResetLink = 15,

    /// <summary>
    /// REMS external email nudging a client who has been sent their EMS form link but has not submitted
    /// it yet (REMS). Its own template rather than a second send of <see cref="RemsFormLink"/>: the third
    /// copy of "You have been asked to complete a form" reads as a system fault, not a reminder. On the
    /// email allowlist. Rendered/dispatched via <c>IRemsEmailNotifier</c>.
    /// </summary>
    RemsFormReminder = 16,

    /// <summary>
    /// The portal switched a tenant's Maconomy connection off after repeated refused logins or timeouts.
    /// Sent to the developer addresses named on the tenant, never to its users. On the email allowlist.
    /// </summary>
    MaconomyConnectionDisabled = 17,
}
