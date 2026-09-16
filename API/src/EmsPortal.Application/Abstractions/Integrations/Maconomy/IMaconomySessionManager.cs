using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>
/// Hands out a live reconnect token for a tenant's connection: the stored one while it is young enough,
/// a fresh login otherwise, and a login again when Maconomy refuses one anyway. What it obtains is
/// written back to the connection row, and what a login says when it fails is written there too.
/// </summary>
public interface IMaconomySessionManager
{
    /// <summary>Logs in with the stored credentials, whatever token is held, and stores the new one.</summary>
    Task<MaconomyLoginResult> LoginAsync(MaconomyConnection connection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="call"/> with a valid token, logging in first when there is none worth using
    /// and once more if Maconomy rejects the token mid-call.
    /// </summary>
    Task<T> InvokeAsync<T>(
        MaconomyConnection connection,
        Func<string, CancellationToken, Task<T>> call,
        CancellationToken cancellationToken = default);

    /// <summary>Drops the token held for the connection, in memory and on the row. The caller saves the row.</summary>
    void ForgetToken(MaconomyConnection connection);

    /// <summary>When a token issued at <paramref name="issuedOnUtc"/> stops being used.</summary>
    DateTime ExpiresOn(DateTime issuedOnUtc);
}
