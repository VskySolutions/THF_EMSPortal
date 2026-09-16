using System.Collections.Concurrent;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmsPortal.Application.Integrations.Maconomy;

/// <summary>
/// Default <see cref="IMaconomySessionManager"/>. Maconomy's reconnect token lives about fifteen minutes
/// and its replies do not renew it, so a token is replaced by its age before a call and by a 401 after
/// one. The newest token per tenant is kept in the shared memory cache: the row is the record, but two
/// requests in flight each hold their own copy of it, and without the cache the second would log in
/// again over a token the first had just fetched.
/// </summary>
public sealed class MaconomySessionManager : IMaconomySessionManager
{
    // One lock per tenant, for the process: the manager itself is scoped, so the locks cannot live on it.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    private const int ErrorMaxLength = 500;

    private readonly IMaconomyGateway _gateway;
    private readonly IMaconomyConnectionRepository _connections;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICredentialEncryptionService _encryption;
    private readonly IMemoryCache _cache;
    private readonly MaconomyOptions _options;
    private readonly ILogger<MaconomySessionManager> _logger;

    public MaconomySessionManager(
        IMaconomyGateway gateway,
        IMaconomyConnectionRepository connections,
        IUnitOfWork unitOfWork,
        ICredentialEncryptionService encryption,
        IMemoryCache cache,
        IOptions<MaconomyOptions> options,
        ILogger<MaconomySessionManager> logger)
    {
        _gateway = gateway;
        _connections = connections;
        _unitOfWork = unitOfWork;
        _encryption = encryption;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public DateTime ExpiresOn(DateTime issuedOnUtc) => issuedOnUtc.AddMinutes(_options.TokenLifetimeMinutes);

    public async Task<MaconomyLoginResult> LoginAsync(MaconomyConnection connection, CancellationToken cancellationToken = default)
    {
        var gate = GateFor(connection.TenantId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var entry = await LoginCoreAsync(connection, cancellationToken);
            return new MaconomyLoginResult(entry.IssuedOnUtc, ExpiresOn(entry.IssuedOnUtc));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<T> InvokeAsync<T>(
        MaconomyConnection connection,
        Func<string, CancellationToken, Task<T>> call,
        CancellationToken cancellationToken = default)
    {
        var token = await EnsureTokenAsync(connection, rejected: null, cancellationToken);
        try
        {
            return await call(token, cancellationToken);
        }
        catch (MaconomyException ex) when (ex.Failure == MaconomyFailure.TokenRejected)
        {
            _logger.LogInformation("Maconomy refused tenant {TenantId}'s reconnect token; logging in again.", connection.TenantId);
            token = await EnsureTokenAsync(connection, rejected: token, cancellationToken);
            try
            {
                return await call(token, cancellationToken);
            }
            catch (MaconomyException again) when (again.Failure == MaconomyFailure.TokenRejected)
            {
                // A token Maconomy has only just issued and already refuses is not a stale token; it is
                // the account, and an admin has to look at it.
                const string message = "Maconomy refused a reconnect token it had just issued. Check the user's access in Maconomy.";
                ForgetToken(connection);
                await RecordFailureAsync(connection, message, cancellationToken);
                throw new MaconomyException(MaconomyFailure.AuthFailed, message, again);
            }
        }
    }

    public void ForgetToken(MaconomyConnection connection)
    {
        _cache.Remove(CacheKey(connection.TenantId));
        connection.EncryptedReconnectToken = null;
        connection.ReconnectTokenIssuedOnUtc = null;
    }

    /// <summary>The token to call with: the one held, unless it is too old or is the one just refused.</summary>
    private async Task<string> EnsureTokenAsync(MaconomyConnection connection, string? rejected, CancellationToken cancellationToken)
    {
        var gate = GateFor(connection.TenantId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var held = Held(connection);
            if (held is not null && !IsStale(held) && held.Token != rejected)
            {
                return held.Token;
            }
            return (await LoginCoreAsync(connection, cancellationToken)).Token;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The newest token known for the tenant: another request's, from the cache, else the row's.</summary>
    private TokenEntry? Held(MaconomyConnection connection)
    {
        if (_cache.TryGetValue(CacheKey(connection.TenantId), out TokenEntry? cached) && cached is not null)
        {
            return cached;
        }
        if (string.IsNullOrEmpty(connection.EncryptedReconnectToken) || connection.ReconnectTokenIssuedOnUtc is not { } issued)
        {
            return null;
        }
        return new TokenEntry(_encryption.Decrypt(connection.EncryptedReconnectToken), issued);
    }

    private bool IsStale(TokenEntry entry)
        => DateTime.UtcNow >= ExpiresOn(entry.IssuedOnUtc).AddSeconds(-_options.TokenRefreshMarginSeconds);

    /// <summary>Logs in and writes the outcome to the row either way: the token, or what went wrong.</summary>
    private async Task<TokenEntry> LoginCoreAsync(MaconomyConnection connection, CancellationToken cancellationToken)
    {
        var endpoint = new MaconomyEndpoint(connection.BaseUrl, connection.InstanceCode);
        string token;
        try
        {
            token = await _gateway.LoginAsync(
                endpoint, connection.UserName, _encryption.Decrypt(connection.EncryptedPassword), cancellationToken);
        }
        catch (MaconomyException ex)
        {
            _logger.LogWarning(
                "Maconomy login failed for tenant {TenantId} ({Failure}): {Message}", connection.TenantId, ex.Failure, ex.Message);
            // Refused credentials also retire whatever token was held: it was issued to the same login.
            if (ex.Failure == MaconomyFailure.AuthFailed)
            {
                ForgetToken(connection);
            }
            await RecordFailureAsync(connection, ex.Message, cancellationToken);
            throw;
        }

        var now = DateTime.UtcNow;
        var entry = new TokenEntry(token, now);
        connection.EncryptedReconnectToken = _encryption.Encrypt(token);
        connection.ReconnectTokenIssuedOnUtc = now;
        connection.LastLoginError = null;
        connection.LastLoginErrorUtc = null;
        _connections.Update(connection);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _cache.Set(CacheKey(connection.TenantId), entry, ExpiresOn(now));
        _logger.LogInformation("Maconomy login succeeded for tenant {TenantId}.", connection.TenantId);
        return entry;
    }

    private async Task RecordFailureAsync(MaconomyConnection connection, string error, CancellationToken cancellationToken)
    {
        connection.LastLoginError = error.Length <= ErrorMaxLength ? error : error[..ErrorMaxLength];
        connection.LastLoginErrorUtc = DateTime.UtcNow;
        _connections.Update(connection);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static SemaphoreSlim GateFor(Guid tenantId) => Locks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));

    private static string CacheKey(Guid tenantId) => $"maconomy:token:{tenantId:N}";

    private sealed record TokenEntry(string Token, DateTime IssuedOnUtc);
}
