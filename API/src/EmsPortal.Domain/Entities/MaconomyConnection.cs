namespace EmsPortal.Domain.Entities;

/// <summary>
/// A tenant's connection to its Deltek Maconomy instance: where it is, which instance, and the service
/// user the portal signs in as. One row per tenant. The password and the reconnect token are stored
/// encrypted (via <c>ICredentialEncryptionService</c>) and never returned by the API. Inherits the
/// standard audit/soft-delete fields from <see cref="AuditableEntity"/>.
/// </summary>
public class MaconomyConnection : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant (tenant-scoped, one live row per tenant).</summary>
    public Guid TenantId { get; set; }

    /// <summary>The API root, e.g. <c>https://host/maconomy-api</c>, https only and without a trailing slash.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The instance short name in <c>/auth/{code}/login</c> and <c>/containers/{code}/…</c>.</summary>
    public string InstanceCode { get; set; } = string.Empty;

    /// <summary>The Maconomy user the portal signs in as.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>That user's password, encrypted. Write-only: never returned to callers.</summary>
    public string EncryptedPassword { get; set; } = string.Empty;

    /// <summary>
    /// The last <c>Maconomy-Reconnect</c> header the login produced, encrypted. Null until the first
    /// login, and again whenever the credentials change or Maconomy refuses it.
    /// </summary>
    public string? EncryptedReconnectToken { get; set; }

    /// <summary>When the held token was issued. The token's age is what decides whether it is still used.</summary>
    public DateTime? ReconnectTokenIssuedOnUtc { get; set; }

    /// <summary>What the last failed login said, so the settings page can show it without a trip to the logs.</summary>
    public string? LastLoginError { get; set; }

    /// <summary>When that failure happened.</summary>
    public DateTime? LastLoginErrorUtc { get; set; }

    /// <summary>Kept for a later use; nothing reads it yet. The customer search names its own container.</summary>
    public string? ContainerId { get; set; }

    /// <summary>Rows a customer search returns when the caller does not say.</summary>
    public int DefaultLimit { get; set; } = 25;

    /// <summary>Switches the lookup off without losing the credentials.</summary>
    public bool IsEnabled { get; set; } = true;
}
