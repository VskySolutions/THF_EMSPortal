using System.Security.Cryptography;
using System.Text;
using EmsPortal.Api.Models.Auth;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Options;
using AuthenticationOptions = EmsPortal.Shared.Configuration.AuthenticationOptions;

namespace EmsPortal.Api.Security;

/// <summary>
/// Turns an authenticated <see cref="User"/> into the token pair the SPA stores: an access token for the user's
/// first tenant plus a new refresh token (stored hashed). Password sign-in, Microsoft sign-in and refresh all
/// issue through here, so a session starts the same way whichever door it came in by. The caller saves.
/// </summary>
public sealed class SessionTokenIssuer
{
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly AuthenticationOptions _options;

    public SessionTokenIssuer(
        IJwtTokenService jwt,
        IRefreshTokenRepository refreshTokens,
        IOptions<AuthenticationOptions> options)
    {
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _options = options.Value;
    }

    /// <summary>Configured refresh-token lifetime in days, falling back to the documented default.</summary>
    public int RefreshTokenDays => _options.RefreshTokenDays <= 0
        ? AuthenticationOptions.DefaultRefreshTokenDays
        : _options.RefreshTokenDays;

    /// <summary>The same window in seconds, as returned to the client.</summary>
    public int RefreshExpiresInSeconds => (int)TimeSpan.FromDays(RefreshTokenDays).TotalSeconds;

    /// <summary>A fresh session for <paramref name="user"/>, scoped to their first tenant assignment.</summary>
    public async Task<LoginTokenResponse> IssueAsync(User user, bool mustChangePassword, CancellationToken cancellationToken)
    {
        var activeTenantId = user.TenantRoles.FirstOrDefault()?.TenantId ?? Guid.Empty;
        var access = _jwt.CreateAccessToken(user, activeTenantId);
        var refresh = await IssueRefreshTokenAsync(user.Id, cancellationToken);
        return new LoginTokenResponse(
            access.Token, access.ExpiresInSeconds, refresh, RefreshExpiresInSeconds, mustChangePassword);
    }

    /// <summary>Stores a new refresh token (hashed) and returns the plaintext the client keeps.</summary>
    public async Task<string> IssueRefreshTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var plaintext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await _refreshTokens.AddAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashToken(plaintext),
            ExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays),
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow,
        }, cancellationToken);
        return plaintext;
    }

    /// <summary>How opaque tokens (refresh, password reset) are stored: only their SHA-256.</summary>
    public static string HashToken(string token)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
