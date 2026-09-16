namespace EmsPortal.Api.Models.Maconomy;

// ---- Requests ----

/// <summary>Create or update the tenant's Maconomy connection. Omit the password to keep the stored one.</summary>
public sealed class SaveMaconomyConnectionRequest
{
    /// <summary>The API root, e.g. <c>https://host/maconomy-api</c>. HTTPS only.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The instance short name that appears in <c>/auth/{code}/login</c>.</summary>
    public string InstanceCode { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    /// <summary>Required the first time; afterwards blank keeps the stored password.</summary>
    public string? Password { get; set; }

    /// <summary>Kept for a later use; nothing reads it yet.</summary>
    public string? ContainerId { get; set; }

    /// <summary>Rows a customer search returns when the caller does not say.</summary>
    public int DefaultLimit { get; set; } = 25;

    public bool IsEnabled { get; set; } = true;
}

// ---- Responses ----

/// <summary>Whether a reconnect token is held, and for how long it will be used.</summary>
public sealed record MaconomyTokenStatus(bool Present, DateTime? IssuedOnUtc, DateTime? ExpiresOnUtc);

/// <summary>The connection as returned to the UI. Neither the password nor the token is ever included.</summary>
public sealed record MaconomyConnectionResponse(
    Guid Id,
    Guid TenantId,
    string BaseUrl,
    string InstanceCode,
    string UserName,
    bool HasPassword,
    string? ContainerId,
    int DefaultLimit,
    bool IsEnabled,
    MaconomyTokenStatus Token,
    string? LastLoginError,
    DateTime? LastLoginErrorUtc,
    string? CreatedByName,
    DateTime CreatedOnUtc,
    string? UpdatedByName,
    DateTime UpdatedOnUtc);

/// <summary>The outcome of a fresh login: when the token was issued, and when it stops being used.</summary>
public sealed record MaconomyLoginResponse(bool Connected, DateTime IssuedOnUtc, DateTime ExpiresOnUtc);

/// <summary>One customer as a dropdown option: <c>text</c> is "number - name", <c>value</c> the number.</summary>
public sealed record MaconomyCustomerOptionResponse(string Text, string Value);
