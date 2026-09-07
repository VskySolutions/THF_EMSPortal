namespace EmsPortal.Shared.Configuration;

/// <summary>
/// Authentication configuration for the Integration API. Platform-issued JWTs are
/// verified with RS256 against <see cref="PublicKeyPem"/>; a symmetric
/// <see cref="SigningKey"/> is supported as a development fallback. API key
/// auth covers machine-to-machine callers.
/// </summary>
public sealed class AuthenticationOptions
{
    /// <summary>Expected token issuer. Empty disables issuer validation.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Expected token audience. Empty disables audience validation.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// RSA public key (PEM) used to verify RS256 token signatures. Provisioned
    /// alongside the login endpoint that issues tokens (WO-39).
    /// </summary>
    public string PublicKeyPem { get; set; } = string.Empty;

    /// <summary>
    /// RSA private key (PEM) used to sign issued JWTs (RS256). When empty an ephemeral key
    /// is generated for the process lifetime (development only).
    /// </summary>
    public string PrivateKeyPem { get; set; } = string.Empty;

    /// <summary>
    /// Symmetric signing key, used only as a development fallback when no
    /// <see cref="PublicKeyPem"/> is configured. Injected via secrets at deploy time.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Access token lifetime in minutes (default 60).</summary>
    public int AccessTokenMinutes { get; set; } = DefaultAccessTokenMinutes;

    /// <summary>Refresh token lifetime in days — how long someone can stay away and still come back to a
    /// live session. Slides forward on every refresh.</summary>
    public int RefreshTokenDays { get; set; } = DefaultRefreshTokenDays;

    /// <summary>Fallback used when the configured value is missing or nonsensical (&lt;= 0).</summary>
    public const int DefaultAccessTokenMinutes = 60;

    /// <inheritdoc cref="DefaultAccessTokenMinutes"/>
    public const int DefaultRefreshTokenDays = 30;

    /// <summary>Name of the HTTP header carrying the API key.</summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    /// <summary>Allowed clock skew, in seconds, when validating token lifetime.</summary>
    public int ClockSkewSeconds { get; set; } = 30;
}
