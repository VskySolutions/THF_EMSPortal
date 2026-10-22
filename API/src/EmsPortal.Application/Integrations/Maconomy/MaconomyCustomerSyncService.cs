using System.Text.Json;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmsPortal.Application.Integrations.Maconomy;

/// <summary>
/// Default <see cref="IMaconomyCustomerSyncService"/>. One code path runs the attempt whether the
/// Workers job or Sync now asked for it: Syncing is committed first, the duplicate check by email runs,
/// then the create with the number left to Maconomy, and the outcome is recorded on the row and on the
/// client. One attempt and no automatic retry — a failure waits for an admin.
/// </summary>
public sealed class MaconomyCustomerSyncService : IMaconomyCustomerSyncService
{
    private const int FailureReasonMax = 2000;
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private readonly IRemsMaconomyCustomerSyncRepository _syncs;
    private readonly IMaconomyConnectionRepository _connections;
    private readonly IMaconomyCustomerWriter _writer;
    private readonly IMaconomyClientDirectory _directory;
    private readonly IPersonRepository _persons;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityEventWriter _activity;
    private readonly IMaconomyCustomerSyncQueue _queue;
    private readonly ITenantContext _tenant;
    private readonly MaconomyOptions _options;
    private readonly ILogger<MaconomyCustomerSyncService> _logger;

    public MaconomyCustomerSyncService(
        IRemsMaconomyCustomerSyncRepository syncs,
        IMaconomyConnectionRepository connections,
        IMaconomyCustomerWriter writer,
        IMaconomyClientDirectory directory,
        IPersonRepository persons,
        IUnitOfWork unitOfWork,
        IActivityEventWriter activity,
        IMaconomyCustomerSyncQueue queue,
        ITenantContext tenant,
        IOptions<MaconomyOptions> options,
        ILogger<MaconomyCustomerSyncService> logger)
    {
        _syncs = syncs;
        _connections = connections;
        _writer = writer;
        _directory = directory;
        _persons = persons;
        _unitOfWork = unitOfWork;
        _activity = activity;
        _queue = queue;
        _tenant = tenant;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<REMSMaconomyCustomerSync?> RecordOnApprovalAsync(REMS rems, CancellationToken cancellationToken = default)
    {
        // The status is not checked here: the caller IS the approval, and the request's Status
        // navigation still names the status it had a moment ago.
        if (WhyNotPosted(rems, requireApproved: false) is not null)
        {
            return null;
        }

        var existing = await _syncs.GetByRemsIdAsync(rems.Id, rems.TenantId, cancellationToken);
        if (existing is { Status: RemsMaconomyCustomerSyncStatus.Synced or RemsMaconomyCustomerSyncStatus.Linked })
        {
            return null;
        }
        if (existing is { Status: RemsMaconomyCustomerSyncStatus.Syncing } && !IsStale(existing))
        {
            return null;
        }

        var enabled = await IsEnabledAsync(rems.TenantId, cancellationToken);
        var status = enabled ? RemsMaconomyCustomerSyncStatus.Pending : RemsMaconomyCustomerSyncStatus.NotSynced;
        if (existing is null)
        {
            existing = NewRow(rems, status);
            await _syncs.AddAsync(existing, cancellationToken);
        }
        else
        {
            // Approved again after a round was declined: a fresh approval is a fresh post.
            existing.Status = status;
            existing.FailureReason = null;
            _syncs.Update(existing);
        }
        return enabled ? existing : null;
    }

    public void Enqueue(REMSMaconomyCustomerSync sync) => _queue.Enqueue(sync.Id, sync.TenantId);

    public async Task<REMSMaconomyCustomerSync?> RunAsync(Guid syncId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var sync = await _syncs.GetByIdAsync(syncId, tenantId, cancellationToken);
        if (sync is null)
        {
            _logger.LogWarning("Maconomy customer sync {SyncId} for tenant {TenantId} no longer exists; nothing to post.", syncId, tenantId);
            return null;
        }
        if (sync.Status is RemsMaconomyCustomerSyncStatus.Synced or RemsMaconomyCustomerSyncStatus.Linked)
        {
            return sync;
        }
        if (sync.Status == RemsMaconomyCustomerSyncStatus.Syncing && !IsStale(sync))
        {
            // Sync now got there first.
            _logger.LogInformation("Maconomy customer sync {SyncId} is already under way; the job leaves it alone.", syncId);
            return sync;
        }

        await AttemptAsync(sync, actorId: null, cancellationToken);
        return sync;
    }

    public async Task<REMSMaconomyCustomerSync> SyncNowAsync(
        Guid remsId, Guid tenantId, Guid actorId, CancellationToken cancellationToken = default)
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

        var sync = await _syncs.GetByRemsIdAsync(remsId, tenantId, cancellationToken);
        if (sync is null)
        {
            // A request approved before the sync existed, or while nothing recorded one: the row is made now.
            var subject = await _syncs.GetSubjectAsync(remsId, tenantId, cancellationToken)
                ?? throw new MaconomyCustomerSyncException(MaconomyCustomerSyncRefusal.NotFound, "Request not found.");
            if (WhyNotPosted(subject.Rems, requireApproved: true) is { } reason)
            {
                throw new MaconomyCustomerSyncException(MaconomyCustomerSyncRefusal.NotEligible, reason);
            }
            sync = NewRow(subject.Rems, RemsMaconomyCustomerSyncStatus.Pending);
            await _syncs.AddAsync(sync, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        else if (sync.Status is RemsMaconomyCustomerSyncStatus.Synced or RemsMaconomyCustomerSyncStatus.Linked)
        {
            throw new MaconomyCustomerSyncException(
                MaconomyCustomerSyncRefusal.NotEligible, $"This client is already Maconomy customer {sync.CustomerNumber}.");
        }
        else if (sync.Status == RemsMaconomyCustomerSyncStatus.Syncing && !IsStale(sync))
        {
            throw new MaconomyCustomerSyncException(
                MaconomyCustomerSyncRefusal.Running, "An attempt is already under way. Give it a moment, then refresh.");
        }

        await AttemptAsync(sync, actorId, cancellationToken);
        return sync;
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
        var counts = await _syncs.CountAsync(tenantId, StaleBefore(), cancellationToken);
        var unrecorded = await _syncs.CountApprovedUnrecordedAsync(tenantId, cancellationToken);
        return new MaconomyCustomerSyncOverview(
            Connected: connection is { IsEnabled: true },
            Enabled: connection is { IsEnabled: true, CustomerSyncEnabled: true },
            connection?.CustomerSyncChangedOnUtc,
            connection?.CustomerSyncChangedById,
            counts.Synced,
            counts.Linked,
            counts.Failed + counts.Stale,
            counts.NotSynced + unrecorded,
            counts.Pending + counts.Syncing);
    }

    public async Task<IReadOnlyList<REMSMaconomyCustomerSync>> ListUnsyncedAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var recorded = await _syncs.ListUnsyncedAsync(tenantId, StaleBefore(), cancellationToken);
        // Approved before the sync existed: no row yet. Read as Not synced, never saved — Sync now makes
        // the real row.
        var unrecorded = (await _syncs.ListApprovedUnrecordedAsync(tenantId, cancellationToken))
            .Select(r => new REMSMaconomyCustomerSync
            {
                Id = Guid.Empty,
                TenantId = tenantId,
                REMSId = r.Id,
                ClientPersonId = r.ClientPersonId ?? Guid.Empty,
                Status = RemsMaconomyCustomerSyncStatus.NotSynced,
                Rems = r,
                CreatedOnUtc = r.UpdatedOnUtc,
            });
        return recorded.Concat(unrecorded).OrderByDescending(s => s.CreatedOnUtc).ToList();
    }

    public bool IsStale(REMSMaconomyCustomerSync sync)
        => sync.Status == RemsMaconomyCustomerSyncStatus.Syncing
            && (sync.LastAttemptOnUtc ?? sync.UpdatedOnUtc) < StaleBefore();

    private DateTime StaleBefore() => DateTime.UtcNow.AddMinutes(-Math.Max(1, _options.CustomerSyncStaleMinutes));

    // ---- The attempt ----

    private async Task AttemptAsync(REMSMaconomyCustomerSync sync, Guid? actorId, CancellationToken cancellationToken)
    {
        if (!_tenant.IsResolved)
        {
            _tenant.Set(sync.TenantId, string.Empty);
        }

        // Syncing is COMMITTED before anything is sent. No REMS number goes to Maconomy, so this row is
        // the only trace of an attempt the portal may lose track of; the next attempt's email check then
        // finds the customer just made and links it instead of creating a second one.
        sync.Status = RemsMaconomyCustomerSyncStatus.Syncing;
        sync.Attempts++;
        sync.LastAttemptOnUtc = DateTime.UtcNow;
        sync.FailureReason = null;
        _syncs.Update(sync);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var subject = await _syncs.GetSubjectAsync(sync.REMSId, sync.TenantId, cancellationToken)
                ?? throw new PortalRefusal("The request no longer exists.");
            var client = subject.Rems.ClientPerson ?? throw new PortalRefusal("The request names no client.");
            if (client.IsMaconomyClient)
            {
                // Somebody linked them to a customer since the row was recorded.
                await SucceedAsync(sync, client, client.MaconomyCustomerNumber!, RemsMaconomyCustomerSyncStatus.Linked, actorId, cancellationToken);
                return;
            }

            var connection = await _connections.GetByTenantAsync(sync.TenantId, cancellationToken);
            if (connection is null || !connection.IsEnabled)
            {
                throw new PortalRefusal("Maconomy is not connected for this tenant.");
            }

            var email = client.PrimaryEmail?.Trim();
            if (string.IsNullOrEmpty(email))
            {
                throw new PortalRefusal("The client has no email address, and the duplicate check needs one.");
            }
            var payload = PayloadFor(client, subject, email);
            sync.PayloadJson = JsonSerializer.Serialize(payload, PayloadOptions);

            var matches = await _writer.FindByEmailAsync(connection, email, cancellationToken);
            if (matches.Count > 1)
            {
                throw new PortalRefusal(
                    $"Maconomy already has {matches.Count} customers with the email {email} "
                    + $"({string.Join(", ", matches.Select(m => m.Number))}). Pick the right one in Maconomy, or link the client to it in the portal.");
            }
            if (matches.Count == 1)
            {
                await SucceedAsync(sync, client, matches[0].Number, RemsMaconomyCustomerSyncStatus.Linked, actorId, cancellationToken);
                return;
            }

            var created = await _writer.CreateAsync(connection, payload, cancellationToken);
            await SucceedAsync(sync, client, created.Number, RemsMaconomyCustomerSyncStatus.Synced, actorId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MaconomyException ex)
        {
            await FailAsync(sync, ex.Message, actorId, cancellationToken);
        }
        catch (PortalRefusal ex)
        {
            await FailAsync(sync, ex.Message, actorId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Maconomy customer sync {SyncId} failed unexpectedly.", sync.Id);
            await FailAsync(sync, $"The portal hit an error: {ex.Message}", actorId, cancellationToken);
        }
    }

    private async Task SucceedAsync(
        REMSMaconomyCustomerSync sync, Person client, string customerNumber, RemsMaconomyCustomerSyncStatus status,
        Guid? actorId, CancellationToken cancellationToken)
    {
        // The number goes onto the client, so their next request is picked from Maconomy and no second
        // customer is ever created — unless another Person already holds it, when it stays on this row only.
        if (!client.IsMaconomyClient
            && !await _syncs.NumberHeldByAnotherAsync(sync.TenantId, customerNumber, client.Id, cancellationToken))
        {
            client.MaconomyCustomerNumber = customerNumber;
            client.LastProfileUpdatedOn = DateTime.UtcNow;
            _persons.Update(client);
        }

        sync.Status = status;
        sync.CustomerNumber = customerNumber;
        sync.SyncedOnUtc = DateTime.UtcNow;
        sync.FailureReason = null;
        _syncs.Update(sync);
        await WriteActivityAsync(
            sync, ActivityEventTypes.RemsMaconomyCustomerSynced,
            status == RemsMaconomyCustomerSyncStatus.Linked
                ? $"Linked to Maconomy customer {customerNumber}"
                : $"Created Maconomy customer {customerNumber}",
            actorId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Reading the customer back seeds the in-memory directory, so the lists name them at once.
        try
        {
            await _directory.ReadAsync(sync.TenantId, customerNumber, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Maconomy customer {CustomerNumber} could not be read back after the sync; the directory will pick it up later.", customerNumber);
        }
    }

    private async Task FailAsync(REMSMaconomyCustomerSync sync, string reason, Guid? actorId, CancellationToken cancellationToken)
    {
        sync.Status = RemsMaconomyCustomerSyncStatus.Failed;
        sync.FailureReason = reason.Length <= FailureReasonMax ? reason : reason[..FailureReasonMax];
        _syncs.Update(sync);
        await WriteActivityAsync(sync, ActivityEventTypes.RemsMaconomyCustomerSyncFailed, sync.FailureReason, actorId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // The event is stamped with the AMBIENT tenant, so it is written only when that is the row's tenant:
    // a Super Admin syncing another tenant's row from their own session gets a log line instead.
    private async Task WriteActivityAsync(
        REMSMaconomyCustomerSync sync, string eventType, string? detail, Guid? actorId, CancellationToken cancellationToken)
    {
        if (_tenant.IsResolved && _tenant.TenantId == sync.TenantId)
        {
            await _activity.WriteAsync(
                new CreateActivityEventDto(EntityType.Rems, sync.REMSId, eventType, null, detail, actorId), cancellationToken);
        }
        else
        {
            _logger.LogInformation("{EventType} for request {RemsId}: {Detail}", eventType, sync.REMSId, detail);
        }
    }

    // ---- What is posted ----

    private static MaconomyNewCustomer PayloadFor(Person client, RemsMaconomyCustomerSyncSubject subject, string email)
    {
        var name = CustomerNameOf(client);
        if (name.Length == 0)
        {
            throw new PortalRefusal("The client has no name to post.");
        }
        return new MaconomyNewCustomer(
            name,
            email,
            string.IsNullOrWhiteSpace(client.MobileNumber) ? null : client.MobileNumber.Trim(),
            MaconomyCustomerService.EntityTypeNumberOf(subject.EntityTypeCode),
            MaconomyCustomerService.IndustryNumberOf(subject.IndustryCode));
    }

    /// <summary>
    /// The name as Maconomy files it: the corporate name for an organisation; "Smith, John Jr." for a
    /// person — surname, comma, given name, then the generational particle — as typed, not upper-cased.
    /// </summary>
    public static string CustomerNameOf(Person client)
    {
        if (client.IsOrganisation && !string.IsNullOrWhiteSpace(client.CorporateName))
        {
            return client.CorporateName.Trim();
        }
        var last = client.LastName?.Trim();
        if (!string.IsNullOrEmpty(last))
        {
            var given = string.Join(" ", new[] { client.FirstName?.Trim(), client.Suffix?.Trim() }.Where(s => !string.IsNullOrEmpty(s)));
            return given.Length > 0 ? $"{last}, {given}" : last;
        }
        var fallback = client.ClientDisplayName.Length > 0 ? client.ClientDisplayName : client.DisplayName;
        return fallback.Trim();
    }

    /// <summary>Why the request's client is not one that is posted, or null when it is.</summary>
    private static string? WhyNotPosted(REMS rems, bool requireApproved)
    {
        if (!string.Equals(rems.Type?.Value, RemsRequestTypes.BrandNewClient, StringComparison.Ordinal))
        {
            return "Only a Brand New Client request posts its client to Maconomy.";
        }
        if (requireApproved && !string.Equals(rems.Status?.Value, RemsRequestStatuses.Approved, StringComparison.Ordinal))
        {
            return "The request is not approved yet.";
        }
        if (rems.ClientPerson is null)
        {
            return "The request names no client.";
        }
        if (rems.ClientPerson.IsMaconomyClient)
        {
            return $"The client is already Maconomy customer {rems.ClientPerson.MaconomyCustomerNumber}.";
        }
        return null;
    }

    private static REMSMaconomyCustomerSync NewRow(REMS rems, RemsMaconomyCustomerSyncStatus status) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = rems.TenantId,
        REMSId = rems.Id,
        ClientPersonId = rems.ClientPersonId!.Value,
        Status = status,
    };

    /// <summary>A reason the portal itself has for not posting: recorded as the failure, never retried.</summary>
    private sealed class PortalRefusal : Exception
    {
        public PortalRefusal(string message) : base(message)
        {
        }
    }
}
