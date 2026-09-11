namespace EmsPortal.Application.Abstractions.Security;

/// <summary>
/// The Microsoft Entra ID side of "Login with Microsoft": where to send the browser, and how to turn the code it
/// comes back with into an identity that has been verified against Microsoft's signing keys.
/// </summary>
public interface IMicrosoftIdentityClient
{
    /// <summary>
    /// The Entra authorize URL for one sign-in attempt. <paramref name="state"/> comes back on the callback
    /// untouched; <paramref name="nonce"/> comes back inside the ID token; <paramref name="codeChallenge"/> is
    /// the PKCE (S256) challenge Microsoft binds the code to, so only the holder of its verifier can redeem it.
    /// </summary>
    string BuildAuthorizationUrl(string redirectUri, string state, string nonce, string codeChallenge);

    /// <summary>
    /// Redeems the authorization code (server-to-server, with the client secret and the PKCE
    /// <paramref name="codeVerifier"/>) and validates the returned ID token: signature, issuer, audience,
    /// lifetime and <paramref name="expectedNonce"/>.
    /// </summary>
    /// <exception cref="MicrosoftSsoException">Microsoft rejected the code, or the token failed validation.</exception>
    Task<MicrosoftIdentity> ExchangeCodeAsync(
        string code, string redirectUri, string codeVerifier, string expectedNonce, CancellationToken cancellationToken = default);
}

/// <summary>
/// Who Microsoft says signed in. <see cref="Emails"/> lists every address-like claim the token carried
/// (<c>email</c>, <c>preferred_username</c>, <c>upn</c>), most reliable first: directories differ in which of
/// them holds the address a person is known by here.
/// </summary>
public sealed record MicrosoftIdentity(string ObjectId, string TenantId, string? DisplayName, IReadOnlyList<string> Emails);

/// <summary>A sign-in attempt that Microsoft, or the token it returned, would not stand behind.</summary>
public sealed class MicrosoftSsoException : Exception
{
    public MicrosoftSsoException(string message) : base(message)
    {
    }

    public MicrosoftSsoException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
