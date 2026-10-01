using EmsPortal.Application.Abstractions.Auditing;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Integrations.Maconomy;

/// <summary>
/// Default <see cref="IMaconomyConnectionService"/>. Encrypts the password the way the SMTP accounts do,
/// retires the token whenever the credentials it was issued to change, and audits every change to the row.
/// </summary>
public sealed class MaconomyConnectionService : IMaconomyConnectionService
{
    private readonly IMaconomyConnectionRepository _connections;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICredentialEncryptionService _encryption;
    private readonly IAuditTrailService _audit;
    private readonly IMaconomySessionManager _session;

    public MaconomyConnectionService(
        IMaconomyConnectionRepository connections,
        IUnitOfWork unitOfWork,
        ICredentialEncryptionService encryption,
        IAuditTrailService audit,
        IMaconomySessionManager session)
    {
        _connections = connections;
        _unitOfWork = unitOfWork;
        _encryption = encryption;
        _audit = audit;
        _session = session;
    }

    public Task<MaconomyConnection?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => _connections.GetByTenantAsync(tenantId, cancellationToken);

    public async Task<MaconomyConnection> SaveAsync(Guid tenantId, SaveMaconomyConnectionInput input, CancellationToken cancellationToken = default)
    {
        var baseUrl = input.BaseUrl.Trim().TrimEnd('/');
        var instance = input.InstanceCode.Trim();
        var user = input.UserName.Trim();
        var password = string.IsNullOrEmpty(input.Password) ? null : input.Password;

        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken);
        if (connection is null)
        {
            if (password is null)
            {
                throw new MaconomyConnectionException("A password is required to create the connection.");
            }

            connection = new MaconomyConnection
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BaseUrl = baseUrl,
                InstanceCode = instance,
                UserName = user,
                EncryptedPassword = _encryption.Encrypt(password),
            };
            Apply(connection, input);
            await _connections.AddAsync(connection, cancellationToken);
            await _audit.AddAsync(nameof(MaconomyConnection), connection.Id.ToString(), "MaconomyConnectionCreated",
                details: Describe(connection), cancellationToken: cancellationToken);
        }
        else
        {
            // The token was issued to the old credentials at the old address; a change to any of them
            // retires it, along with the error the old ones may have left behind.
            var credentialsChanged = password is not null
                || !string.Equals(connection.BaseUrl, baseUrl, StringComparison.Ordinal)
                || !string.Equals(connection.InstanceCode, instance, StringComparison.Ordinal)
                || !string.Equals(connection.UserName, user, StringComparison.Ordinal);

            connection.BaseUrl = baseUrl;
            connection.InstanceCode = instance;
            connection.UserName = user;
            if (password is not null)
            {
                connection.EncryptedPassword = _encryption.Encrypt(password);
            }
            Apply(connection, input);
            if (credentialsChanged)
            {
                _session.ForgetToken(connection);
                connection.LastLoginError = null;
                connection.LastLoginErrorUtc = null;
            }
            _connections.Update(connection);
            await _audit.AddAsync(nameof(MaconomyConnection), connection.Id.ToString(), "MaconomyConnectionUpdated",
                details: $"{Describe(connection)}; credentialsChanged={credentialsChanged}", cancellationToken: cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return connection;
    }

    public async Task<bool> DeleteAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken);
        if (connection is null)
        {
            return false;
        }

        _session.ForgetToken(connection);
        _connections.Remove(connection);
        await _audit.AddAsync(nameof(MaconomyConnection), connection.Id.ToString(), "MaconomyConnectionDeleted",
            details: Describe(connection), cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<MaconomyLoginResult> LoginAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken)
            ?? throw new MaconomyException(MaconomyFailure.NotConfigured, "Maconomy is not connected for this tenant.");
        return await _session.LoginAsync(connection, cancellationToken);
    }

    public async Task<bool> ForgetTokenAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken);
        if (connection is null)
        {
            return false;
        }

        _session.ForgetToken(connection);
        _connections.Update(connection);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void Apply(MaconomyConnection connection, SaveMaconomyConnectionInput input)
    {
        connection.ContainerId = string.IsNullOrWhiteSpace(input.ContainerId) ? null : input.ContainerId.Trim();
        connection.DefaultLimit = input.DefaultLimit;
        connection.IsEnabled = input.IsEnabled;
    }

    // What the audit trail records of a change: the address and the user, never the secrets.
    private static string Describe(MaconomyConnection connection)
        => $"baseUrl={connection.BaseUrl}; instance={connection.InstanceCode}; user={connection.UserName}; "
           + $"defaultLimit={connection.DefaultLimit}; enabled={connection.IsEnabled}";
}
