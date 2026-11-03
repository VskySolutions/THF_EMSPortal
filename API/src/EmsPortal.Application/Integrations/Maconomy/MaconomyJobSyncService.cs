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

public sealed class MaconomyJobSyncService : IMaconomyJobSyncService
{
    private const int FailureReasonMax = 1000;

    private readonly IRemsRequestRepository _requests;
    private readonly IMaconomyConnectionRepository _connections;
    private readonly IMaconomyNumberMaps _numbers;
    private readonly IMaconomyCardWriter _writer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityEventWriter _activity;
    private readonly IMaconomyJobSyncQueue _queue;
    private readonly ITenantContext _tenant;
    private readonly MaconomyOptions _options;
    private readonly ILogger<MaconomyJobSyncService> _logger;

    public MaconomyJobSyncService(
        IRemsRequestRepository requests,
        IMaconomyConnectionRepository connections,
        IMaconomyNumberMaps numbers,
        IMaconomyCardWriter writer,
        IUnitOfWork unitOfWork,
        IActivityEventWriter activity,
        IMaconomyJobSyncQueue queue,
        ITenantContext tenant,
        IOptions<MaconomyOptions> options,
        ILogger<MaconomyJobSyncService> logger)
    {
        _requests = requests;
        _connections = connections;
        _numbers = numbers;
        _writer = writer;
        _unitOfWork = unitOfWork;
        _activity = activity;
        _queue = queue;
        _tenant = tenant;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MaconomyJobSyncContext> GetContextAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken);
        if (connection is null || !connection.IsEnabled)
        {
            return MaconomyJobSyncContext.None;
        }
        // The numbers are configuration (project manager, default template) and the tenant's REMS Job
        // Template option items (the template map); the connection row holds only the switch.
        var numbers = await _numbers.GetAsync(tenantId, cancellationToken);
        return new MaconomyJobSyncContext(
            Connected: true, connection.JobSyncEnabled,
            Clean(_options.JobCard.ProjectManagerNumber), Clean(_options.JobCard.DefaultTemplateJobNumber),
            numbers.TemplateMap);
    }

    public Task<RemsRequest?> GetAsync(Guid remsId, CancellationToken cancellationToken = default)
        => _requests.GetForJobSyncAsync(remsId, cancellationToken);

    public async Task<RemsRequest?> RecordPendingAsync(RemsRequest request, bool requireApproved, CancellationToken cancellationToken = default)
    {
        if (request.MaconomyJobSyncStatus == RemsMaconomyJobSyncStatus.Synced
            || (request.MaconomyJobSyncStatus is RemsMaconomyJobSyncStatus.Pending or RemsMaconomyJobSyncStatus.Syncing && !IsStale(request)))
        {
            return null;
        }
        if (requireApproved && !string.Equals(request.Status?.Value, RemsRequestStatuses.Approved, StringComparison.Ordinal))
        {
            return null;
        }
        // Without a customer there is nothing to put the job under; the customer sync queues it once it has made one.
        if (request.Client is not { IsMaconomyClient: true })
        {
            return null;
        }
        var connection = await _connections.GetByTenantAsync(request.TenantId, cancellationToken);
        if (connection is not { IsEnabled: true, JobSyncEnabled: true })
        {
            return null;
        }
        request.MaconomyJobSyncStatus = RemsMaconomyJobSyncStatus.Pending;
        request.MaconomyJobSyncError = null;
        _requests.Update(request);
        return request;
    }

    public void Enqueue(RemsRequest request) => _queue.Enqueue(request.Id, request.TenantId);

    public async Task<RemsRequest?> RunAsync(Guid remsId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (!_tenant.IsResolved)
        {
            _tenant.Set(tenantId, string.Empty);
        }
        var request = await _requests.GetForJobSyncAsync(remsId, cancellationToken);
        if (request is null || request.TenantId != tenantId)
        {
            _logger.LogWarning("Request {RemsId} for tenant {TenantId} no longer exists; nothing to post to Maconomy as a job.", remsId, tenantId);
            return null;
        }
        if (request.MaconomyJobSyncStatus == RemsMaconomyJobSyncStatus.Synced)
        {
            return request;
        }
        if (request.MaconomyJobSyncStatus == RemsMaconomyJobSyncStatus.Syncing && !IsStale(request))
        {
            _logger.LogInformation("Maconomy job sync for request {RemsId} is already under way; the job leaves it alone.", remsId);
            return request;
        }
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken);
        var context = await GetContextAsync(tenantId, cancellationToken);
        if (context.Connected && !context.Enabled)
        {
            // Switched off since it was queued: back to waiting, for Sync job now once it is on again.
            if (request.MaconomyJobSyncStatus == RemsMaconomyJobSyncStatus.Pending)
            {
                request.MaconomyJobSyncStatus = RemsMaconomyJobSyncStatus.NotSynced;
                _requests.Update(request);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            return request;
        }
        // Not connected, or the request lacks something: recorded as the failure it is, for Sync job now.
        var reason = context.Connected ? MaconomyJobSyncRules.WhyNotPostable(request, context) : MaconomyJobSyncRules.NotConnectedReason;
        if (reason is not null)
        {
            await FailAsync(request, reason, actorId: null, cancellationToken);
            return request;
        }
        var write = MaconomyJobDataMapping.Map(request, context) with { Container = _options.JobCard.Container };
        await AttemptAsync(request, connection!, write, actorId: null, cancellationToken);
        return request;
    }

    public async Task<RemsRequest> SyncNowAsync(Guid remsId, Guid tenantId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken);
        if (connection is null || !connection.IsEnabled)
        {
            throw new MaconomyJobSyncException(MaconomyCustomerSyncRefusal.NotConnected, MaconomyJobSyncRules.NotConnectedReason);
        }
        if (!connection.JobSyncEnabled)
        {
            throw new MaconomyJobSyncException(
                MaconomyCustomerSyncRefusal.SyncOff, $"The job sync is switched off. Start it under {MaconomyJobSyncRules.SettingsPlace}, then sync again.");
        }
        var request = await _requests.GetForJobSyncAsync(remsId, cancellationToken);
        if (request is null || request.TenantId != tenantId)
        {
            throw new MaconomyJobSyncException(MaconomyCustomerSyncRefusal.NotFound, "Request not found.");
        }
        var context = await GetContextAsync(tenantId, cancellationToken);
        var stale = IsStale(request);
        if (MaconomyJobSyncRules.WhyNotEligible(request, context, stale) is { } reason)
        {
            var refusal = request.MaconomyJobSyncStatus is RemsMaconomyJobSyncStatus.Pending or RemsMaconomyJobSyncStatus.Syncing && !stale
                ? MaconomyCustomerSyncRefusal.Running
                : MaconomyCustomerSyncRefusal.NotEligible;
            throw new MaconomyJobSyncException(refusal, reason);
        }
        var write = MaconomyJobDataMapping.Map(request, context) with { Container = _options.JobCard.Container };
        await AttemptAsync(request, connection, write, actorId, cancellationToken);
        return request;
    }

    public bool IsStale(RemsRequest request)
        => request.MaconomyJobSyncStatus is RemsMaconomyJobSyncStatus.Pending or RemsMaconomyJobSyncStatus.Syncing
            && request.UpdatedOnUtc < StaleBefore();

    private DateTime StaleBefore() => DateTime.UtcNow.AddMinutes(-Math.Max(1, _options.JobSyncStaleMinutes));

    public async Task<MaconomyConnection> SetEnabledAsync(Guid tenantId, bool enabled, Guid actorId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken)
            ?? throw new MaconomyJobSyncException(MaconomyCustomerSyncRefusal.NotConnected, "Save a Maconomy connection first.");
        if (connection.JobSyncEnabled != enabled)
        {
            connection.JobSyncEnabled = enabled;
            connection.JobSyncChangedOnUtc = DateTime.UtcNow;
            connection.JobSyncChangedById = actorId;
            _connections.Update(connection);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return connection;
    }

    public async Task<MaconomyJobSyncOverview> GetOverviewAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken);
        var counts = await _requests.CountJobSyncAsync(StaleBefore(), cancellationToken);
        return new MaconomyJobSyncOverview(
            Connected: connection is { IsEnabled: true },
            Enabled: connection is { IsEnabled: true, JobSyncEnabled: true },
            connection?.JobSyncChangedOnUtc,
            connection?.JobSyncChangedById,
            Clean(_options.JobCard.ProjectManagerNumber),
            Clean(_options.JobCard.DefaultTemplateJobNumber),
            counts.Synced,
            counts.Failed,
            counts.Syncing,
            counts.Waiting);
    }

    public Task<IReadOnlyList<RemsRequest>> ListApprovedAsync(CancellationToken cancellationToken = default)
        => _requests.ListApprovedForJobSyncAsync(cancellationToken);

    private async Task AttemptAsync(
        RemsRequest request, MaconomyConnection connection, MaconomyCardWrite write, Guid? actorId, CancellationToken cancellationToken)
    {
        // Syncing is committed BEFORE the create, so a crash between the create and the record leaves a
        // trace rather than a second job on the next attempt.
        request.MaconomyJobSyncStatus = RemsMaconomyJobSyncStatus.Syncing;
        request.MaconomyJobSyncError = null;
        _requests.Update(request);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        try
        {
            var created = MaconomyJobDataMapping.FromCreated((await _writer.CreateAsync(connection, write, cancellationToken)).Data, write);
            request.MaconomyJobNumber = created.Number;
            request.MaconomyJobSyncStatus = RemsMaconomyJobSyncStatus.Synced;
            request.MaconomyJobSyncedById = actorId;
            request.MaconomyJobSyncedOnUtc = DateTime.UtcNow;
            request.MaconomyJobSyncError = null;
            _requests.Update(request);
            await WriteActivityAsync(
                request, ActivityEventTypes.RemsMaconomyJobSynced, $"Created Maconomy job {created.Number} \"{created.Name}\"", actorId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MaconomyException ex)
        {
            await FailAsync(request, ex.Message, actorId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Maconomy job sync for request {RemsId} failed unexpectedly.", request.Id);
            await FailAsync(request, $"The portal hit an error: {ex.Message}", actorId, cancellationToken);
        }
    }

    private async Task FailAsync(RemsRequest request, string reason, Guid? actorId, CancellationToken cancellationToken)
    {
        request.MaconomyJobSyncStatus = RemsMaconomyJobSyncStatus.Failed;
        request.MaconomyJobSyncError = reason.Length <= FailureReasonMax ? reason : reason[..FailureReasonMax];
        _requests.Update(request);
        await WriteActivityAsync(request, ActivityEventTypes.RemsMaconomyJobSyncFailed, request.MaconomyJobSyncError, actorId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task WriteActivityAsync(RemsRequest request, string eventType, string? detail, Guid? actorId, CancellationToken cancellationToken)
    {
        if (_tenant.IsResolved && _tenant.TenantId == request.TenantId)
        {
            await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, request.Id, eventType, null, detail, actorId), cancellationToken);
        }
        else
        {
            _logger.LogInformation("{EventType} for request {RemsId}: {Detail}", eventType, request.Id, detail);
        }
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
