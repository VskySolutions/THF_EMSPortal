namespace EmsPortal.Api.Models.SmtpAccounts;

// ---- Requests ----

/// <summary>Create an SMTP account.</summary>
public sealed class CreateSmtpAccountRequest
{
    public Guid? TenantId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string EncryptionType { get; set; } = string.Empty;
    public string AuthType { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromName { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
}

/// <summary>Update an SMTP account.</summary>
public sealed class UpdateSmtpAccountRequest
{
    public string AccountName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string EncryptionType { get; set; } = string.Empty;
    public string AuthType { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromName { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
}

/// <summary>Send a diagnostic test email from an SMTP account.</summary>
public sealed class TestSmtpRequest
{
    public string RecipientEmail { get; set; } = string.Empty;
}

/// <summary>Turn the tenant's notification emails on or off, all of them at once.</summary>
public sealed class UpdateEmailSettingsRequest
{
    public bool NotificationsEnabled { get; set; }
}

// ---- Responses ----

/// <summary>
/// An SMTP account as returned to the UI. The password is never included — it is write-only (SMTP
/// Email Accounts system contract).
/// </summary>
public sealed record SmtpAccountSummaryResponse(
    Guid Id,
    string AccountName,
    string Host,
    int Port,
    string FromName,
    string FromEmail,
    string EncryptionType,
    string AuthType,
    string? Username,
    bool IsActive,
    string? CreatedByName,
    DateTime CreatedOnUtc,
    string? UpdatedByName,
    DateTime UpdatedOnUtc);

/// <summary>The outcome of a set-active operation.</summary>
public sealed record SmtpActivationResponse(Guid ActivatedId, Guid? DeactivatedId);

/// <summary>
/// The tenant's email settings. The audit pair says who last threw the switch; both are null while the
/// tenant has never touched it, which also means notifications are on.
/// </summary>
public sealed record EmailSettingsResponse(bool NotificationsEnabled, string? UpdatedByName, DateTime? UpdatedOnUtc);

/// <summary>The outcome of a test send.</summary>
public sealed record SmtpTestResultResponse(
    bool Success,
    DateTime? SentAtUtc,
    string? ServerResponse,
    string? ErrorCategory,
    string? ErrorDetail);
