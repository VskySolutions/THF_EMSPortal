using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>Data access for the self-service password-reset tokens.</summary>
public interface IPasswordResetTokenRepository
{
    /// <summary>The token with this hash, redeemed or not.</summary>
    Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task AddAsync(PasswordResetToken token, CancellationToken cancellationToken = default);

    void Update(PasswordResetToken token);

    /// <summary>Marks every unredeemed token for a user as used.</summary>
    Task InvalidateAllForUserAsync(Guid userId, DateTime onUtc, CancellationToken cancellationToken = default);
}
