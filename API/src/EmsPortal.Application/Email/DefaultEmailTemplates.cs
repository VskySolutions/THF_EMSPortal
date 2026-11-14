using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Email;

/// <summary>
/// The built-in default content and metadata for each <see cref="EmailTemplateKey"/>. Used to seed the
/// platform-wide defaults, to drive the management UI (display name, description, available
/// placeholders), and as the fallback when no row exists yet.
/// </summary>
public static class DefaultEmailTemplates
{
    /// <summary>Default content + metadata for one template key.</summary>
    public sealed record Definition(
        EmailTemplateKey Key,
        string DisplayName,
        string Description,
        string Subject,
        string Body,
        IReadOnlyList<string> Placeholders);

    private static readonly IReadOnlyDictionary<EmailTemplateKey, Definition> Map = new[]
    {
        new Definition(
            EmailTemplateKey.UserInvitation,
            "User Invitation",
            "Sent when a person is given a login account. Includes their temporary password and the login link.",
            "You've been invited to {{TenantName}}",
            """
            <p>Hello {{FullName}},</p>
            <p>An account has been created for you on <strong>{{TenantName}}</strong>.</p>
            <p>Sign in with the credentials below and you'll be asked to set a new password on first login:</p>
            <ul>
              <li><strong>Email:</strong> {{Email}}</li>
              <li><strong>Temporary password:</strong> {{TemporaryPassword}}</li>
            </ul>
            <p><a href="{{LoginUrl}}">Sign in to your account</a></p>
            <p>If you did not expect this email, please contact your administrator.</p>
            """,
            new[] { "FullName", "Email", "TemporaryPassword", "LoginUrl", "TenantName" }),

        new Definition(
            EmailTemplateKey.PasswordReset,
            "Password Reset",
            "Sent when an administrator resets a user's password. Includes the new temporary password.",
            "Your {{TenantName}} password has been reset",
            """
            <p>Hello {{FullName}},</p>
            <p>Your password for <strong>{{TenantName}}</strong> has been reset by an administrator.</p>
            <p>Sign in with the temporary password below; you'll be prompted to choose a new one:</p>
            <ul>
              <li><strong>Temporary password:</strong> {{TemporaryPassword}}</li>
            </ul>
            <p><a href="{{LoginUrl}}">Sign in to your account</a></p>
            <p>If you did not request this, please contact your administrator immediately.</p>
            """,
            new[] { "FullName", "TemporaryPassword", "LoginUrl", "TenantName" }),

        new Definition(
            EmailTemplateKey.PasswordResetLink,
            "Password Reset Link",
            "Sent when a user requests a password reset from the sign-in page. Carries a one-time link, never a password.",
            "Reset your {{TenantName}} password",
            """
            <p>Hello {{FullName}},</p>
            <p>We received a request to reset your password for <strong>{{TenantName}}</strong>.</p>
            <p><a href="{{ResetUrl}}">Choose a new password</a></p>
            <p>This link can be used once and expires in {{ExpiryMinutes}} minutes.</p>
            <p>If you did not request this, you can ignore this email — your password has not changed.</p>
            """,
            new[] { "FullName", "ResetUrl", "ExpiryMinutes", "TenantName" }),

        new Definition(
            EmailTemplateKey.PasswordChanged,
            "Password Changed",
            "Confirmation sent after a user changes their own password.",
            "Your {{TenantName}} password was changed",
            """
            <p>Hello {{FullName}},</p>
            <p>This is a confirmation that your password for <strong>{{TenantName}}</strong> was changed on {{ChangedAtUtc}} (UTC).</p>
            <p>If you did not make this change, please contact your administrator immediately.</p>
            """,
            new[] { "FullName", "TenantName", "ChangedAtUtc" }),

        new Definition(
            EmailTemplateKey.Welcome,
            "Welcome",
            "A general welcome message sent when an account becomes active.",
            "Welcome to {{TenantName}}",
            """
            <p>Hello {{FullName}},</p>
            <p>Welcome to <strong>{{TenantName}}</strong>! Your account is now active.</p>
            <p><a href="{{LoginUrl}}">Sign in to get started</a></p>
            """,
            new[] { "FullName", "TenantName", "LoginUrl" }),

        new Definition(
            EmailTemplateKey.MentionReceived,
            "Mention Received",
            "Sent when a user is @mentioned in a note on any record (Universal Features).",
            "{{Title}}",
            """
            <p>Hello {{FullName}},</p>
            <p>{{Title}}</p>
            <blockquote>{{Body}}</blockquote>
            <p><a href="{{LoginUrl}}">Open the record</a></p>
            """,
            new[] { "FullName", "Title", "Body", "TenantName", "LoginUrl" }),

        new Definition(
            EmailTemplateKey.ReminderDue,
            "Reminder Due",
            "Sent when a reminder a user set on a record reaches its due time (Universal Features).",
            "{{Title}}",
            """
            <p>Hello {{FullName}},</p>
            <p>{{Title}}</p>
            <blockquote>{{Body}}</blockquote>
            <p><a href="{{LoginUrl}}">Open the record</a></p>
            """,
            new[] { "FullName", "Title", "Body", "TenantName", "LoginUrl" }),

        // ---- COP form emails (WO-124) ----

        new Definition(
            EmailTemplateKey.RemsFormLink,
            "COP Form Link",
            "Sent to a client with the secure link to complete a COP form for a request.",
            "Welcome to THF | Complete Your Client Set-up",
            """
            <p>Hello {{ClientName}},</p>
            <p>Welcome to THF! We're excited for the opportunity to work with you.</p>
            <p>As a first step, please complete our online Client Set-Up Form. This will provide our team with the information needed to establish your client account.</p>
            <p>A few things to know before you begin:</p>
            <ul>
              <li>Time required: Approximately 5&ndash;10 minutes.</li>
              <li>Deadline: Please complete the form within five (5) business days.</li>
              <li>Important: The form must be completed in one sitting. It cannot be started by one person and finished by another, so please ensure the appropriate person in your organization is available to complete it.</li>
            </ul>
            <p><a href="{{FormLink}}" style="display:inline-block;padding:10px 20px;background-color:#1f4e79;color:#ffffff;text-decoration:none;font-weight:bold;border-radius:4px;">COMPLETE CLIENT SET-UP FORM</a></p>
            """,
            new[] { "ClientName", "RemsNumber", "FormLink", "TenantName" }),

        // Same placeholders as RemsFormLink so an admin can nudge without re-explaining, and so the two
        // stay interchangeable in the send dialog. The wording assumes the client has already had the
        // first email — that is the whole point of it being a separate template.
        new Definition(
            EmailTemplateKey.RemsFormReminder,
            "COP Form Reminder",
            "Sent to a client who has been emailed their COP form link but has not submitted it yet.",
            "A Quick Reminder | Your THF Client Set-up",
            """
            <p>Hello {{ClientName}},</p>
            <p>We're looking forward to working with you! As a reminder, your THF Client Set-Up Form is still awaiting completion.</p>
            <p>The form takes just 5&ndash;10 minutes and provides the information we need to establish your account.</p>
            <p>Please complete it within five business days of your original invitation.</p>
            <p><a href="{{FormLink}}" style="display:inline-block;padding:10px 20px;background-color:#1f4e79;color:#ffffff;text-decoration:none;font-weight:bold;border-radius:4px;">COMPLETE CLIENT SET-UP FORM</a></p>
            <p>Before you begin: The form must be completed in one sitting by the same person.</p>
            <p>If you have any questions or need assistance, please contact your THF representative.</p>
            <p>Thank you!</p>
            """,
            new[] { "ClientName", "RemsNumber", "FormLink", "TenantName" }),

        new Definition(
            EmailTemplateKey.RemsFormSubmitted,
            "COP Form Submitted",
            "Sent to the assigned Admin and CSE when a client submits their COP form for a request.",
            "COP form submitted for {{RemsNumber}}",
            """
            <p>{{ClientName}} has submitted their COP form for request <strong>{{RemsNumber}}</strong>.</p>
            <p><strong>Submitted on:</strong> {{SubmittedOn}}</p>
            <p><a href="{{RequestLink}}">Open the request</a></p>
            """,
            new[] { "ClientName", "RemsNumber", "SubmittedOn", "RequestLink" }),

        new Definition(
            EmailTemplateKey.RemsFormReceived,
            "COP Form Received",
            "Sent to the client as soon as they submit their COP form, confirming it has been received.",
            "Confirmed | Your THF Client Set-up Form Has Been Received",
            """
            <p>Hello {{ClientName}},</p>
            <p>Thank you!</p>
            <p>Your THF Client Set-Up Form has been successfully submitted. No further action is needed on this form.</p>
            <p><strong>What happens next?</strong></p>
            <p>Our team will review your information and complete the necessary steps to establish your client account.</p>
            <p>If we need additional information or clarification, a THF team member will reach out to you directly.</p>
            <p>Depending on the services you'll be receiving, you may also receive communications regarding engagement documents, tax organizers, client portal access, or other next steps.</p>
            <p>We appreciate your time and look forward to working with you.</p>
            """,
            new[] { "ClientName", "RemsNumber", "TenantName" }),

        new Definition(
            EmailTemplateKey.MaconomyConnectionDisabled,
            "Maconomy Connection Switched Off",
            "Sent to the developer emails named on the tenant when the portal switches its Maconomy connection off after repeated failed logins or timeouts.",
            "Maconomy connection switched off for {{TenantName}}",
            """
            <p>The portal has switched off the Maconomy connection for <strong>{{TenantName}}</strong> after {{FailureCount}} failed attempts in a row.</p>
            <p><strong>Last error:</strong> {{ErrorMessage}}</p>
            <ul>
              <li><strong>Instance:</strong> {{InstanceCode}} at {{BaseUrl}}</li>
              <li><strong>User:</strong> {{UserName}}</li>
              <li><strong>Switched off:</strong> {{DisabledOn}}</li>
            </ul>
            <p>The customer lookup, the customer sync and the job sync are stopped until the connection is switched on again. Check the credentials and the address, then switch it on under Settings &rsaquo; Maconomy and save.</p>
            <p><a href="{{SettingsLink}}">Open the Maconomy settings</a></p>
            """,
            new[] { "TenantName", "FailureCount", "ErrorMessage", "InstanceCode", "BaseUrl", "UserName", "DisabledOn", "SettingsLink" }),
    }.ToDictionary(d => d.Key);

    /// <summary>All template definitions, in key order.</summary>
    public static IReadOnlyList<Definition> All { get; } = Map.Values.OrderBy(d => d.Key).ToList();

    /// <summary>The default definition for a key.</summary>
    public static Definition For(EmailTemplateKey key) => Map[key];
}
