using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Email;

/// <summary>
/// Encapsulates all business logic for SMTP account management: password encryption, the
/// auto-activate-first rule, the atomic active-swap (SMTP Email Accounts ADR-003).
/// </summary>
public interface ISmtpAccountService
{
    /// <summary>
    /// Creates an account: encrypts the password, persists it, and auto-activates it when it is the
    /// first account for the tenant.
    /// </summary>
    Task<SmtpAccount> CreateAsync(CreateSmtpAccountInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an account, preserving the existing encrypted password when no new password is supplied
    /// and re-encrypting it when one is.
    /// </summary>
    Task<SmtpAccount?> UpdateAsync(Guid id, Guid tenantId, UpdateSmtpAccountInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically deactivates the tenant's current active account (if any) and activates the target,
    /// in a single transaction.
    /// </summary>
    Task<SmtpActivationResult?> ActivateAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes an account.</summary>
    Task<bool?> DeleteAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts the account credentials and sends a diagnostic test message to <paramref name="recipientEmail"/>.
    /// Returns a structured result (success or categorised failure), or null when the account does not exist
    /// for the tenant; never throws on send failure and does not change the account's active status or write
    /// an audit entry.
    /// </summary>
    Task<SmtpTestResult?> TestSendAsync(Guid id, Guid tenantId, string recipientEmail, CancellationToken cancellationToken = default);
}

/// <summary>Creation input for an SMTP account, scoped to a resolved tenant.</summary>
public sealed record CreateSmtpAccountInput(
    Guid TenantId,
    string AccountName,
    string Host,
    int Port,
    SmtpEncryptionType EncryptionType,
    SmtpAuthType AuthType,
    string? Username,
    string? Password,
    string FromName,
    string FromEmail);

/// <summary>Update input for an SMTP account.</summary>
public sealed record UpdateSmtpAccountInput(
    string AccountName,
    string Host,
    int Port,
    SmtpEncryptionType EncryptionType,
    SmtpAuthType AuthType,
    string? Username,
    string? Password,
    string FromName,
    string FromEmail);

/// <summary>The outcome of an activation: the activated account and the one it deactivated (if any).</summary>
public sealed record SmtpActivationResult(Guid ActivatedId, Guid? DeactivatedId);
