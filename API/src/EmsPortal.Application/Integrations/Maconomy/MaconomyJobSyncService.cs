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
    private readonly IMaconomyPickLists _pickLists;
    private readonly IMaconomyDocumentSyncService _documents;
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
        IMaconomyPickLists pickLists,
        IMaconomyDocumentSyncService documents,
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
        _pickLists = pickLists;
        _documents = documents;
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
        // Template and REMS Department option items (the template and location maps); the connection row
        // holds only the switch.
        var numbers = await _numbers.GetAsync(tenantId, cancellationToken);
        return new MaconomyJobSyncContext(
            Connected: true, connection.JobSyncEnabled,
            Clean(_options.JobCard.ProjectManagerNumber), Clean(_options.JobCard.DefaultTemplateJobNumber),
            numbers.TemplateMap, numbers.LocationMap, numbers.JobTypeValueMap);
    }

    public Task<RemsRequest?> GetAsync(Guid remsId, CancellationToken cancellationToken = default)
        => _requests.GetForJobSyncAsync(remsId, cancellationToken);

    public async Task<MaconomyJobLookups> GetLookupsAsync(RemsRequest request, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetByTenantAsync(request.TenantId, cancellationToken);
        if (connection is not { IsEnabled: true })
        {
            return new MaconomyJobLookups(null, null);
        }
        // Each list is read only when the request needs it, and a list that cannot be read is done without:
        // the month is optional on the card, and a template not found by name is asked for.
        var months = MaconomyJobDataMapping.FiscalYearEndOf(request) is null
            ? null
            : await ReadListAsync(() => _pickLists.GetAsync(connection, MaconomyJobDataMapping.FiscalMonthList, cancellationToken), request, "month");
        var templates = await ReadListAsync(() => _pickLists.GetJobTemplatesAsync(connection, cancellationToken), request, "template job");
        return new MaconomyJobLookups(months, templates);
    }

    public async Task<MaconomyPickList> GetTemplatesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken);
        if (connection is not { IsEnabled: true })
        {
            throw new MaconomyJobSyncException(MaconomyCustomerSyncRefusal.NotConnected, MaconomyJobSyncRules.NotConnectedReason);
        }
        return await _pickLists.GetJobTemplatesAsync(connection, cancellationToken);
    }

    private async Task<MaconomyPickList?> ReadListAsync(Func<Task<MaconomyPickList>> read, RemsRequest request, string what)
    {
        try
        {
            return await read();
        }
        catch (MaconomyException ex)
        {
            _logger.LogWarning("Maconomy's {What} list could not be read for request {RemsId}: {Message}", what, request.Id, ex.Message);
            return null;
        }
    }

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
        await AttemptAsync(request, connection!, context, actorId: null, cancellationToken);
        return request;
    }

    public async Task<RemsRequest> SyncNowAsync(
        Guid remsId, Guid tenantId, Guid actorId, string? templateJobNumber = null, CancellationToken cancellationToken = default)
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
        // A template job picked for this request: it has to be one of Maconomy's, and it is kept, so the
        // next attempt does not ask again.
        if (!string.IsNullOrWhiteSpace(templateJobNumber))
        {
            var templates = await _pickLists.GetJobTemplatesAsync(connection, cancellationToken);
            var picked = templates.Items.FirstOrDefault(t => string.Equals(t.Value.Trim(), templateJobNumber.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new MaconomyJobSyncException(
                    MaconomyCustomerSyncRefusal.NotEligible, $"\"{templateJobNumber.Trim()}\" is not a template job in Maconomy.");
            request.MaconomyTemplateJobNumber = picked.Value;
        }
        await AttemptAsync(request, connection, context, actorId, cancellationToken);
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
        RemsRequest request, MaconomyConnection connection, MaconomyJobSyncContext context, Guid? actorId, CancellationToken cancellationToken)
    {
        // The job is already in Maconomy and only its budget line is owed: that alone is tried again.
        if (MaconomyJobSyncRules.IsBudgetOwed(request))
        {
            await SetBudgetAsync(request, connection, actorId, cancellationToken);
            return;
        }
        var lookups = await GetLookupsAsync(request, cancellationToken);
        // No template job for the Service Line: the sync stops here, and Sync job now asks which one to use.
        if (MaconomyJobDataMapping.ResolveTemplate(request, context, lookups.Templates) is null)
        {
            await FailAsync(request, MaconomyJobSyncRules.NoTemplateReason(request), actorId, cancellationToken);
            return;
        }
        var write = MaconomyJobDataMapping.Map(request, context, lookups) with { Container = _options.JobCard.Container };
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
            // The fee estimate goes on the job's budget next; owed until it is there.
            request.IsMaconomyJobBudgetSyncPending = request.FirstYearFeeEstimate.HasValue;
            _requests.Update(request);
            await WriteActivityAsync(
                request, ActivityEventTypes.RemsMaconomyJobSynced, $"Created Maconomy job {created.Number} \"{created.Name}\"", actorId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            // The signed client acceptance form and the purchase order go under the job, now that there is one.
            _documents.Enqueue(request.Id, request.TenantId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MaconomyException ex)
        {
            // A refused job may be one Maconomy wants a person on: say whose Maconomy Number is missing.
            var advice = ex.Failure == MaconomyFailure.Rejected ? MaconomyJobSyncRules.MissingNumbersAdvice(request) : null;
            await FailAsync(request, ex.Message + advice, actorId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Maconomy job sync for request {RemsId} failed unexpectedly.", request.Id);
            await FailAsync(request, $"The portal hit an error: {ex.Message}", actorId, cancellationToken);
        }
        if (MaconomyJobSyncRules.IsBudgetOwed(request))
        {
            await SetBudgetAsync(request, connection, actorId, cancellationToken);
        }
    }

    // The First-Year Fee Estimate onto the created job's budget. A failure leaves the job as it is and
    // the line owed, so the next sync does only this.
    private async Task SetBudgetAsync(RemsRequest request, MaconomyConnection connection, Guid? actorId, CancellationToken cancellationToken)
    {
        var number = request.MaconomyJobNumber!.Trim();
        try
        {
            if (MaconomyJobBudgetDataMapping.Map(request, number) is { } line)
            {
                await _writer.SetTableRowAsync(connection, line, cancellationToken);
                await WriteActivityAsync(
                    request, ActivityEventTypes.RemsMaconomyJobSynced,
                    $"Put the First-Year Fee Estimate on the budget of Maconomy job {number}", actorId, cancellationToken);
            }
            request.IsMaconomyJobBudgetSyncPending = false;
            request.MaconomyJobSyncStatus = RemsMaconomyJobSyncStatus.Synced;
            request.MaconomyJobSyncError = null;
            _requests.Update(request);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ex is not MaconomyException)
            {
                _logger.LogError(ex, "Setting the budget of Maconomy job {JobNumber} for request {RemsId} failed unexpectedly.", number, request.Id);
            }
            await FailAsync(request, MaconomyJobSyncRules.BudgetOwedReason(number, ex.Message), actorId, cancellationToken);
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
