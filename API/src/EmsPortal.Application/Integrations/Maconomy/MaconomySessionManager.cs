using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using EmsPortal.Application.Abstractions.Auditing;
using EmsPortal.Application.Abstractions.Email;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
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
/// <para>
/// It also keeps count of Maconomy refusing the login or not answering. Enough of those in a row and the
/// connection is switched off, as an admin would switch it off, and the developers named on the tenant
/// are emailed — so a locked service account or a dead host is not hammered until somebody notices.
/// </para>
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
    private readonly ITenantRepository _tenants;
    private readonly IAuditTrailService _audit;
    private readonly IEmailDispatcher _email;
    private readonly MaconomyOptions _options;
    private readonly AppOptions _app;
    private readonly ILogger<MaconomySessionManager> _logger;

    public MaconomySessionManager(
        IMaconomyGateway gateway,
        IMaconomyConnectionRepository connections,
        IUnitOfWork unitOfWork,
        ICredentialEncryptionService encryption,
        IMemoryCache cache,
        ITenantRepository tenants,
        IAuditTrailService audit,
        IEmailDispatcher email,
        IOptions<MaconomyOptions> options,
        IOptions<AppOptions> app,
        ILogger<MaconomySessionManager> logger)
    {
        _gateway = gateway;
        _connections = connections;
        _unitOfWork = unitOfWork;
        _encryption = encryption;
        _cache = cache;
        _tenants = tenants;
        _audit = audit;
        _email = email;
        _options = options.Value;
        _app = app.Value;
        _logger = logger;
    }

    public DateTime ExpiresOn(DateTime issuedOnUtc) => issuedOnUtc.AddMinutes(_options.TokenLifetimeMinutes);

    public async Task<MaconomyLoginResult> LoginAsync(MaconomyConnection connection, CancellationToken cancellationToken = default)
    {
        var gate = GateFor(connection.TenantId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            // An admin testing the connection is asking for a real attempt, whatever the last one did.
            _cache.Remove(PauseKey(connection.TenantId));
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
        ThrowIfPaused(connection.TenantId);
        var token = await EnsureTokenAsync(connection, rejected: null, cancellationToken);
        try
        {
            return await AnsweredAsync(connection, call(token, cancellationToken), cancellationToken);
        }
        catch (MaconomyException ex) when (IsOutage(ex))
        {
            // Not answering a call is the same failure as not answering the login, and counts with it.
            await RecordFailureAsync(connection, ex, cancellationToken);
            Pause(connection.TenantId, ex);
            throw;
        }
        catch (MaconomyException ex) when (ex.Failure == MaconomyFailure.TokenRejected)
        {
            _logger.LogInformation("Maconomy refused tenant {TenantId}'s reconnect token; logging in again.", connection.TenantId);
            token = await EnsureTokenAsync(connection, rejected: token, cancellationToken);
            try
            {
                return await AnsweredAsync(connection, call(token, cancellationToken), cancellationToken);
            }
            catch (MaconomyException again) when (again.Failure == MaconomyFailure.TokenRejected)
            {
                // A token Maconomy has only just issued and already refuses is not a stale token; it is
                // the account, and an admin has to look at it.
                const string message = "Maconomy refused a reconnect token it had just issued. Check the user's access in Maconomy.";
                ForgetToken(connection);
                var refused = new MaconomyException(MaconomyFailure.AuthFailed, message, again);
                await RecordFailureAsync(connection, refused, cancellationToken);
                Pause(connection.TenantId, refused);
                throw refused;
            }
        }
    }

    /// <summary>The call's answer, once Maconomy gives one: an answer ends whatever run of failures there was.</summary>
    private async Task<T> AnsweredAsync<T>(MaconomyConnection connection, Task<T> call, CancellationToken cancellationToken)
    {
        var result = await call;
        if (connection.ConsecutiveFailureCount > 0)
        {
            connection.ConsecutiveFailureCount = 0;
            connection.LastLoginError = null;
            connection.LastLoginErrorUtc = null;
            _connections.Update(connection);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return result;
    }

    public void ForgetToken(MaconomyConnection connection)
    {
        _cache.Remove(CacheKey(connection.TenantId));
        _cache.Remove(PauseKey(connection.TenantId));
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
            await RecordFailureAsync(connection, ex, cancellationToken);
            if (IsRefusalOrOutage(ex))
            {
                Pause(connection.TenantId, ex);
            }
            throw;
        }

        var now = DateTime.UtcNow;
        var entry = new TokenEntry(token, now);
        connection.EncryptedReconnectToken = _encryption.Encrypt(token);
        connection.ReconnectTokenIssuedOnUtc = now;
        connection.LastLoginError = null;
        connection.LastLoginErrorUtc = null;
        connection.ConsecutiveFailureCount = 0;
        _connections.Update(connection);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _cache.Set(CacheKey(connection.TenantId), entry, ExpiresOn(now));
        _logger.LogInformation("Maconomy login succeeded for tenant {TenantId}.", connection.TenantId);
        return entry;
    }

    /// <summary>
    /// Writes what went wrong to the row. A refused login or a Maconomy that did not answer also counts
    /// towards the switch-off; the count reaching the limit is what switches the connection off.
    /// </summary>
    private async Task RecordFailureAsync(MaconomyConnection connection, MaconomyException failure, CancellationToken cancellationToken)
    {
        var error = failure.Message;
        connection.LastLoginError = error.Length <= ErrorMaxLength ? error : error[..ErrorMaxLength];
        connection.LastLoginErrorUtc = DateTime.UtcNow;
        if (IsRefusalOrOutage(failure))
        {
            connection.ConsecutiveFailureCount++;
        }
        if (connection.IsEnabled && _options.FailuresBeforeDisable > 0
            && connection.ConsecutiveFailureCount >= _options.FailuresBeforeDisable)
        {
            await SwitchOffAsync(connection, cancellationToken);
            return;
        }
        _connections.Update(connection);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Switches the connection off as an admin would, records why, and emails the developers named on the
    /// tenant. The switch-off is saved before the email is queued: an email about one that did not stick
    /// would be a lie. Nobody named means the log and the audit trail are the only record.
    /// </summary>
    private async Task SwitchOffAsync(MaconomyConnection connection, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        connection.IsEnabled = false;
        connection.AutoDisabledOnUtc = now;
        var tenant = await _tenants.GetByIdAsync(connection.TenantId, cancellationToken);
        var developers = TenantDeveloperEmails.Parse(tenant?.DeveloperEmails);
        var told = developers.Count == 0 ? "nobody named" : string.Join(", ", developers);
        _logger.LogError(
            "Maconomy connection for tenant {TenantId} switched off after {Count} failures in a row: {Error}. Developers emailed: {Developers}.",
            connection.TenantId, connection.ConsecutiveFailureCount, connection.LastLoginError, told);
        await _audit.AddAsync(nameof(MaconomyConnection), connection.Id.ToString(), "MaconomyConnectionAutoDisabled",
            details: $"failures={connection.ConsecutiveFailureCount}; error={connection.LastLoginError}; notified={told}",
            cancellationToken: cancellationToken);
        _connections.Update(connection);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // The template is HTML and the renderer substitutes as-is, so what Maconomy said is encoded here.
        var model = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["FailureCount"] = connection.ConsecutiveFailureCount.ToString(CultureInfo.InvariantCulture),
            ["ErrorMessage"] = WebUtility.HtmlEncode(connection.LastLoginError ?? string.Empty),
            ["InstanceCode"] = WebUtility.HtmlEncode(connection.InstanceCode),
            ["BaseUrl"] = WebUtility.HtmlEncode(connection.BaseUrl),
            ["UserName"] = WebUtility.HtmlEncode(connection.UserName),
            ["DisabledOn"] = now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC",
            ["SettingsLink"] = $"{_app.BaseUrl.TrimEnd('/')}/settings/maconomy",
        };
        foreach (var to in developers)
        {
            _email.Enqueue(connection.TenantId, EmailTemplateKey.MaconomyConnectionDisabled, to, model);
        }
    }

    // A refused login repeated on every search is how a service account gets locked, and an unreachable
    // Maconomy makes each caller wait out the full timeout. Both are left alone for a short while.
    private void Pause(Guid tenantId, MaconomyException failure)
    {
        if (_options.FailureCooldownSeconds > 0)
        {
            _cache.Set(
                PauseKey(tenantId), new PauseEntry(failure.Failure, failure.Message),
                TimeSpan.FromSeconds(_options.FailureCooldownSeconds));
        }
    }

    private void ThrowIfPaused(Guid tenantId)
    {
        if (_cache.TryGetValue(PauseKey(tenantId), out PauseEntry? pause) && pause is not null)
        {
            throw new MaconomyException(pause.Failure, pause.Message);
        }
    }

    /// <summary>Maconomy not answering at all, as opposed to answering with something the call has no use for.</summary>
    private static bool IsOutage(MaconomyException ex)
        => ex.Failure == MaconomyFailure.Unavailable
            && ex.InnerException is HttpRequestException or TaskCanceledException;

    /// <summary>The two failures that are paused and counted: refused credentials, and no answer at all.</summary>
    private static bool IsRefusalOrOutage(MaconomyException ex)
        => ex.Failure == MaconomyFailure.AuthFailed || IsOutage(ex);

    private static SemaphoreSlim GateFor(Guid tenantId) => Locks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));

    private static string CacheKey(Guid tenantId) => $"maconomy:token:{tenantId:N}";

    private static string PauseKey(Guid tenantId) => $"maconomy:pause:{tenantId:N}";

    private sealed record TokenEntry(string Token, DateTime IssuedOnUtc);

    private sealed record PauseEntry(MaconomyFailure Failure, string Message);
}
