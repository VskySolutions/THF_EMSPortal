using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Application.Integrations.Maconomy.Mapping;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmsPortal.Application.Integrations.Maconomy;

public sealed class MaconomyCustomerSyncService : IMaconomyCustomerSyncService
{
    private const int FailureReasonMax = 1000;

    private readonly IClientRepository _clients;
    private readonly IRemsRequestRepository _requests;
    private readonly IMaconomyConnectionRepository _connections;
    private readonly IMaconomyCustomerWriter _writer;
    private readonly IMaconomyCardWriter _cards;
    private readonly IMaconomyNumberMaps _numbers;
    private readonly IMaconomyClientDirectory _directory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityEventWriter _activity;
    private readonly IMaconomyCustomerSyncQueue _queue;
    private readonly ITenantContext _tenant;
    private readonly MaconomyOptions _options;
    private readonly ILogger<MaconomyCustomerSyncService> _logger;

    public MaconomyCustomerSyncService(
        IClientRepository clients,
        IRemsRequestRepository requests,
        IMaconomyConnectionRepository connections,
        IMaconomyCustomerWriter writer,
        IMaconomyCardWriter cards,
        IMaconomyNumberMaps numbers,
        IMaconomyClientDirectory directory,
        IUnitOfWork unitOfWork,
        IActivityEventWriter activity,
        IMaconomyCustomerSyncQueue queue,
        ITenantContext tenant,
        IOptions<MaconomyOptions> options,
        ILogger<MaconomyCustomerSyncService> logger)
    {
        _clients = clients;
        _requests = requests;
        _connections = connections;
        _writer = writer;
        _cards = cards;
        _numbers = numbers;
        _directory = directory;
        _unitOfWork = unitOfWork;
        _activity = activity;
        _queue = queue;
        _tenant = tenant;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Client?> RecordOnApprovalAsync(RemsRequest request, CancellationToken cancellationToken = default)
    {
        var client = request.Client ?? await _clients.GetByIdAsync(request.ClientId, cancellationToken);
        if (client is null || WhyNotPosted(request, client, requireApproved: false) is not null)
        {
            return null;
        }
        if (client.MaconomySyncStatus is RemsMaconomyCustomerSyncStatus.Synced or RemsMaconomyCustomerSyncStatus.Linked)
        {
            return null;
        }
        if (client.MaconomySyncStatus == RemsMaconomyCustomerSyncStatus.Syncing && !IsStale(client))
        {
            return null;
        }
        var enabled = await IsEnabledAsync(request.TenantId, cancellationToken);
        client.MaconomySyncStatus = enabled ? RemsMaconomyCustomerSyncStatus.Pending : RemsMaconomyCustomerSyncStatus.NotSynced;
        client.MaconomySyncError = null;
        _clients.Update(client);
        return enabled ? client : null;
    }

    public void Enqueue(Client client) => _queue.Enqueue(client.Id, client.TenantId);

    public async Task<Client?> RunAsync(Guid clientId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var client = await _clients.GetByIdUnscopedAsync(clientId, tenantId, cancellationToken);
        if (client is null)
        {
            _logger.LogWarning("Client {ClientId} for tenant {TenantId} no longer exists; nothing to post to Maconomy.", clientId, tenantId);
            return null;
        }
        if (client.MaconomySyncStatus is RemsMaconomyCustomerSyncStatus.Synced or RemsMaconomyCustomerSyncStatus.Linked)
        {
            return client;
        }
        if (client.MaconomySyncStatus == RemsMaconomyCustomerSyncStatus.Syncing && !IsStale(client))
        {
            _logger.LogInformation("Maconomy customer sync for client {ClientId} is already under way; the job leaves it alone.", clientId);
            return client;
        }
        await AttemptAsync(client, remsId: null, actorId: null, cancellationToken);
        return client;
    }

    public async Task<Client> SyncNowAsync(Guid remsId, Guid tenantId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken);
        if (connection is null || !connection.IsEnabled)
        {
            throw new MaconomyCustomerSyncException(MaconomyCustomerSyncRefusal.NotConnected, "Maconomy is not connected for this tenant.");
        }
        if (!connection.CustomerSyncEnabled)
        {
            throw new MaconomyCustomerSyncException(
                MaconomyCustomerSyncRefusal.SyncOff, "The customer sync is switched off. Start it on the Maconomy settings page, then sync again.");
        }
        var request = await _requests.GetByIdAsync(remsId, cancellationToken)
            ?? throw new MaconomyCustomerSyncException(MaconomyCustomerSyncRefusal.NotFound, "Request not found.");
        var client = await _clients.GetSyncSubjectAsync(remsId, tenantId, cancellationToken)
            ?? throw new MaconomyCustomerSyncException(MaconomyCustomerSyncRefusal.NotFound, "Request not found.");
        if (client.MaconomySyncStatus is RemsMaconomyCustomerSyncStatus.Synced or RemsMaconomyCustomerSyncStatus.Linked)
        {
            throw new MaconomyCustomerSyncException(
                MaconomyCustomerSyncRefusal.NotEligible, $"This client is already Maconomy customer {client.MaconomyCustomerNumber}.");
        }
        if (WhyNotPosted(request, client, requireApproved: true) is { } reason)
        {
            throw new MaconomyCustomerSyncException(MaconomyCustomerSyncRefusal.NotEligible, reason);
        }
        if (client.MaconomySyncStatus == RemsMaconomyCustomerSyncStatus.Syncing && !IsStale(client))
        {
            throw new MaconomyCustomerSyncException(
                MaconomyCustomerSyncRefusal.Running, "An attempt is already under way. Give it a moment, then refresh.");
        }
        await AttemptAsync(client, remsId, actorId, cancellationToken);
        return client;
    }

    public async Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await _connections.GetByTenantAsync(tenantId, cancellationToken) is { IsEnabled: true, CustomerSyncEnabled: true };

    public async Task<MaconomyConnection> SetEnabledAsync(
        Guid tenantId, bool enabled, Guid actorId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken)
            ?? throw new MaconomyCustomerSyncException(MaconomyCustomerSyncRefusal.NotConnected, "Save a Maconomy connection first.");
        if (connection.CustomerSyncEnabled != enabled)
        {
            connection.CustomerSyncEnabled = enabled;
            connection.CustomerSyncChangedOnUtc = DateTime.UtcNow;
            connection.CustomerSyncChangedById = actorId;
            _connections.Update(connection);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return connection;
    }

    public async Task<MaconomyCustomerSyncOverview> GetOverviewAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken);
        var counts = await _clients.CountSyncAsync(tenantId, StaleBefore(), cancellationToken);
        return new MaconomyCustomerSyncOverview(
            Connected: connection is { IsEnabled: true },
            Enabled: connection is { IsEnabled: true, CustomerSyncEnabled: true },
            connection?.CustomerSyncChangedOnUtc,
            connection?.CustomerSyncChangedById,
            counts.Synced,
            counts.Linked,
            counts.Failed + counts.Stale,
            counts.NotSynced,
            counts.Pending + counts.Syncing);
    }

    public async Task<IReadOnlyList<MaconomyUnsyncedClient>> ListUnsyncedAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var recorded = await _clients.ListUnsyncedAsync(tenantId, StaleBefore(), cancellationToken);
        var unrecorded = await _clients.ListApprovedUnrecordedAsync(tenantId, cancellationToken);
        var clients = recorded.Concat(unrecorded)
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .OrderByDescending(c => c.UpdatedOnUtc)
            .ToList();
        var rows = new List<MaconomyUnsyncedClient>(clients.Count);
        foreach (var client in clients)
        {
            rows.Add(new MaconomyUnsyncedClient(client, await SubjectRequestAsync(client, cancellationToken)));
        }
        return rows;
    }

    public bool IsStale(Client client)
        => client.MaconomySyncStatus == RemsMaconomyCustomerSyncStatus.Syncing && client.UpdatedOnUtc < StaleBefore();

    private DateTime StaleBefore() => DateTime.UtcNow.AddMinutes(-Math.Max(1, _options.CustomerSyncStaleMinutes));

    // The latest approved brand-new-client request for the client: where the activity is logged.
    private async Task<RemsRequest?> SubjectRequestAsync(Client client, CancellationToken cancellationToken)
    {
        if (!_tenant.IsResolved)
        {
            return null;
        }
        var requests = await _requests.ListByClientAsync(client.Id, cancellationToken);
        return requests.FirstOrDefault(r =>
                string.Equals(r.Type?.Value, RemsRequestTypes.BrandNewClient, StringComparison.Ordinal)
                && string.Equals(r.Status?.Value, RemsRequestStatuses.Approved, StringComparison.Ordinal))
            ?? requests.FirstOrDefault();
    }

    private async Task AttemptAsync(Client client, Guid? remsId, Guid? actorId, CancellationToken cancellationToken)
    {
        if (!_tenant.IsResolved)
        {
            _tenant.Set(client.TenantId, string.Empty);
        }
        remsId ??= (await SubjectRequestAsync(client, cancellationToken))?.Id;
        client.MaconomySyncStatus = RemsMaconomyCustomerSyncStatus.Syncing;
        client.MaconomySyncError = null;
        _clients.Update(client);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        try
        {
            if (client.IsMaconomyClient)
            {
                await SucceedAsync(client, remsId, client.MaconomyCustomerNumber!, RemsMaconomyCustomerSyncStatus.Linked, actorId, cancellationToken);
                return;
            }
            var connection = await _connections.GetByTenantAsync(client.TenantId, cancellationToken);
            if (connection is null || !connection.IsEnabled)
            {
                throw new PortalRefusal("Maconomy is not connected for this tenant.");
            }
            var email = (client.PrimaryEmail ?? client.Person?.PrimaryEmail)?.Trim();
            if (string.IsNullOrEmpty(email))
            {
                throw new PortalRefusal("The client has no email address, and the duplicate check needs one.");
            }
            if (MaconomyCustomerDataMapping.CustomerNameOf(client).Length == 0)
            {
                throw new PortalRefusal("The client has no name to post.");
            }
            var matches = await _writer.FindByEmailAsync(connection, email, cancellationToken);
            if (matches.Count > 1)
            {
                throw new PortalRefusal(
                    $"Maconomy already has {matches.Count} customers with the email {email} "
                    + $"({string.Join(", ", matches.Select(m => m.Number))}). Pick the right one in Maconomy, or link the client to it in the portal.");
            }
            if (matches.Count == 1)
            {
                await SucceedAsync(client, remsId, matches[0].Number, RemsMaconomyCustomerSyncStatus.Linked, actorId, cancellationToken);
                return;
            }
            // The mapping says what is sent; the card writer sends it; the container is the instance's own.
            var write = MaconomyCustomerDataMapping.Map(client, email, await _numbers.GetAsync(client.TenantId, cancellationToken)) with { Container = _options.CustomerCard.Container };
            var created = MaconomyCustomerDataMapping.FromCreated((await _cards.CreateAsync(connection, write, cancellationToken)).Data, write);
            await SucceedAsync(client, remsId, created.Number, RemsMaconomyCustomerSyncStatus.Synced, actorId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MaconomyException ex)
        {
            await FailAsync(client, remsId, ex.Message, actorId, cancellationToken);
        }
        catch (PortalRefusal ex)
        {
            await FailAsync(client, remsId, ex.Message, actorId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Maconomy customer sync for client {ClientId} failed unexpectedly.", client.Id);
            await FailAsync(client, remsId, $"The portal hit an error: {ex.Message}", actorId, cancellationToken);
        }
    }

    private async Task SucceedAsync(
        Client client, Guid? remsId, string customerNumber, RemsMaconomyCustomerSyncStatus status,
        Guid? actorId, CancellationToken cancellationToken)
    {
        if (!client.IsMaconomyClient
            && !await _clients.MaconomyNumberHeldByAnotherAsync(client.TenantId, customerNumber, client.Id, cancellationToken))
        {
            client.MaconomyCustomerNumber = customerNumber;
        }
        client.MaconomySyncStatus = status;
        client.MaconomySyncedOnUtc = DateTime.UtcNow;
        client.MaconomySyncError = null;
        _clients.Update(client);
        await WriteActivityAsync(
            client, remsId, ActivityEventTypes.RemsMaconomyCustomerSynced,
            status == RemsMaconomyCustomerSyncStatus.Linked
                ? $"Linked to Maconomy customer {customerNumber}"
                : $"Created Maconomy customer {customerNumber}",
            actorId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        try
        {
            await _directory.ReadAsync(client.TenantId, customerNumber, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Maconomy customer {CustomerNumber} could not be read back after the sync; the directory will pick it up later.", customerNumber);
        }
    }

    private async Task FailAsync(Client client, Guid? remsId, string reason, Guid? actorId, CancellationToken cancellationToken)
    {
        client.MaconomySyncStatus = RemsMaconomyCustomerSyncStatus.Failed;
        client.MaconomySyncError = reason.Length <= FailureReasonMax ? reason : reason[..FailureReasonMax];
        _clients.Update(client);
        await WriteActivityAsync(client, remsId, ActivityEventTypes.RemsMaconomyCustomerSyncFailed, client.MaconomySyncError, actorId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task WriteActivityAsync(
        Client client, Guid? remsId, string eventType, string? detail, Guid? actorId, CancellationToken cancellationToken)
    {
        if (remsId is { } id && _tenant.IsResolved && _tenant.TenantId == client.TenantId)
        {
            await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, id, eventType, null, detail, actorId), cancellationToken);
        }
        else
        {
            _logger.LogInformation("{EventType} for client {ClientId}: {Detail}", eventType, client.Id, detail);
        }
    }

    private static string? WhyNotPosted(RemsRequest request, Client client, bool requireApproved)
    {
        if (!string.Equals(request.Type?.Value, RemsRequestTypes.BrandNewClient, StringComparison.Ordinal))
        {
            return "Only a Brand New Client request posts its client to Maconomy.";
        }
        if (requireApproved && !string.Equals(request.Status?.Value, RemsRequestStatuses.Approved, StringComparison.Ordinal))
        {
            return "The request is not approved yet.";
        }
        if (client.IsMaconomyClient)
        {
            return $"The client is already Maconomy customer {client.MaconomyCustomerNumber}.";
        }
        return null;
    }

    private sealed class PortalRefusal : Exception
    {
        public PortalRefusal(string message) : base(message)
        {
        }
    }
}
