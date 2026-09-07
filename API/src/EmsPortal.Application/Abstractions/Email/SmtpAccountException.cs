namespace EmsPortal.Application.Abstractions.Email;

/// <summary>
/// A business-rule violation in SMTP account management (e.g. a duplicate account name or an attempt
/// to delete the active account).
/// </summary>
public sealed class SmtpAccountException : Exception
{
    public SmtpAccountException(string code, string message) : base(message) => Code = code;

    /// <summary>Stable error code (see <c>SmtpAccountErrorCodes</c>).</summary>
    public string Code { get; }
}

/// <summary>Stable error codes raised by <see cref="SmtpAccountException"/>.</summary>
public static class SmtpAccountErrorCodes
{
    /// <summary>Another account in the tenant already uses the requested name.</summary>
    public const string DuplicateName = "DUPLICATE_IDENTIFIER";

    /// <summary>The account cannot be deleted because it is currently active.</summary>
    public const string ActiveAccountDelete = "ACTIVE_ACCOUNT_DELETE";
}
