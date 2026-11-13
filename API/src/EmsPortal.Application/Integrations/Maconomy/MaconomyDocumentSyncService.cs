using System.Collections.Concurrent;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Storage;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Application.Integrations.Maconomy.Mapping;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmsPortal.Application.Integrations.Maconomy;

public sealed class MaconomyDocumentSyncService : IMaconomyDocumentSyncService
{
    private const int FailureReasonMax = 1000;

    // One upload run per request at a time, for the process: the customer sync and the job sync each queue
    // one, and two reading the same file as not yet uploaded would send it twice.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    private readonly IRemsRequestRepository _requests;
    private readonly IMaconomyConnectionRepository _connections;
    private readonly IMaconomyDocumentWriter _writer;
    // Null on a host that holds no file store (the Workers host): uploads then wait for the API.
    private readonly IFileStorage? _files;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityEventWriter _activity;
    private readonly IMaconomyDocumentSyncQueue _queue;
    private readonly ITenantContext _tenant;
    private readonly MaconomyOptions _options;
    private readonly ILogger<MaconomyDocumentSyncService> _logger;

    public MaconomyDocumentSyncService(
        IRemsRequestRepository requests,
        IMaconomyConnectionRepository connections,
        IMaconomyDocumentWriter writer,
        IUnitOfWork unitOfWork,
        IActivityEventWriter activity,
        IMaconomyDocumentSyncQueue queue,
        ITenantContext tenant,
        IOptions<MaconomyOptions> options,
        ILogger<MaconomyDocumentSyncService> logger,
        IFileStorage? files = null)
    {
        _requests = requests;
        _connections = connections;
        _writer = writer;
        _files = files;
        _unitOfWork = unitOfWork;
        _activity = activity;
        _queue = queue;
        _tenant = tenant;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MaconomyDocumentSyncContext> GetContextAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => ContextOf(await _connections.GetByTenantAsync(tenantId, cancellationToken));

    // Called right after a customer or a job was created: a queue that is down must not make that read as failed.
    public void Enqueue(Guid remsId, Guid tenantId)
    {
        try
        {
            _queue.Enqueue(remsId, tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The Maconomy document upload for request {RemsId} could not be queued; its files wait to be uploaded by hand.", remsId);
        }
    }

    public async Task<RemsRequest?> RunAsync(Guid remsId, Guid tenantId, CancellationToken cancellationToken = default)
        => (await UploadAsync(remsId, tenantId, actorId: null, cancellationToken)).Request;

    public async Task<RemsRequest> UploadNowAsync(Guid remsId, Guid tenantId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken);
        if (connection is null || !connection.IsEnabled)
        {
            throw new MaconomyDocumentSyncException(MaconomyCustomerSyncRefusal.NotConnected, MaconomyJobSyncRules.NotConnectedReason);
        }
        var (request, attempted, blocked) = await UploadAsync(remsId, tenantId, actorId, cancellationToken);
        if (request is null)
        {
            throw new MaconomyDocumentSyncException(MaconomyCustomerSyncRefusal.NotFound, "Request not found.");
        }
        if (attempted == 0)
        {
            throw new MaconomyDocumentSyncException(
                MaconomyCustomerSyncRefusal.NotEligible, blocked ?? "Every file of this request is already in Maconomy.");
        }
        return request;
    }

    public bool IsStale(RemsFile file)
        => file.MaconomyDocumentSyncStatus == RemsMaconomyDocumentSyncStatus.Syncing
            && file.UpdatedOnUtc < DateTime.UtcNow.AddMinutes(-Math.Max(1, _options.DocumentSyncStaleMinutes));

    private static MaconomyDocumentSyncContext ContextOf(MaconomyConnection? connection)
        => connection is { IsEnabled: true }
            ? new MaconomyDocumentSyncContext(true, connection.CustomerSyncEnabled, connection.JobSyncEnabled)
            : MaconomyDocumentSyncContext.None;

    /// <summary>One run over the request's files: how many were attempted, and what held back the rest when none was.</summary>
    private async Task<(RemsRequest? Request, int Attempted, string? Blocked)> UploadAsync(
        Guid remsId, Guid tenantId, Guid? actorId, CancellationToken cancellationToken)
    {
        if (!_tenant.IsResolved)
        {
            _tenant.Set(tenantId, string.Empty);
        }
        var gate = Gates.GetOrAdd(remsId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Read inside the gate: a run that waited sees what the one before it uploaded.
            var request = await _requests.GetForJobSyncAsync(remsId, cancellationToken);
            if (request is null || request.TenantId != tenantId)
            {
                _logger.LogWarning("Request {RemsId} for tenant {TenantId} no longer exists; no documents to upload to Maconomy.", remsId, tenantId);
                return (null, 0, null);
            }
            // The files are on the API's disk: a host without the store leaves them for the API to send.
            if (_files is null)
            {
                _logger.LogWarning("This host holds no file store; request {RemsId}'s files wait to be uploaded to Maconomy from the API.", remsId);
                return (request, 0, "This host cannot read the portal's files.");
            }
            var connection = await _connections.GetByTenantAsync(tenantId, cancellationToken);
            var context = ContextOf(connection);
            var attempted = 0;
            string? blocked = null;
            foreach (var file in request.Files.Where(f => !f.Deleted).OrderBy(f => f.CreatedOnUtc).ToList())
            {
                if (!MaconomyDocumentSyncRules.IsOutstanding(file, IsStale(file)))
                {
                    if (file.MaconomyDocumentSyncStatus == RemsMaconomyDocumentSyncStatus.Syncing)
                    {
                        blocked ??= "An upload is already under way. Give it a moment, then refresh.";
                    }
                    continue;
                }
                if (MaconomyDocumentSyncRules.WhyNotUploadable(request, file, context) is { } reason)
                {
                    blocked ??= reason;
                    continue;
                }
                attempted++;
                await AttemptAsync(request, file, connection!, actorId, cancellationToken);
            }
            return (request, attempted, blocked);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task AttemptAsync(RemsRequest request, RemsFile file, MaconomyConnection connection, Guid? actorId, CancellationToken cancellationToken)
    {
        // Syncing is committed BEFORE the file is sent, so a crash mid-upload leaves a trace rather than a second copy.
        file.MaconomyDocumentSyncStatus = RemsMaconomyDocumentSyncStatus.Syncing;
        file.MaconomyDocumentSyncError = null;
        _requests.UpdateFile(file);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var label = file.Media?.OriginalFileName ?? "The file";
        try
        {
            var target = MaconomyDocumentMapping.TargetOf(file.Kind);
            MaconomyStoredDocument stored;
            // A copied request carries its source's attachments: the customer already holds them.
            if (target == MaconomyDocumentTarget.Customer
                && await _requests.FindUploadedTwinAsync(request.ClientId, file.MediaId, file.Id, cancellationToken) is { MaconomyDocumentArchiveNumber: { } archive } twin)
            {
                stored = new MaconomyStoredDocument(archive, twin.MaconomyDocumentName, twin.MaconomyDocumentKey);
            }
            else
            {
                var document = await ReadAsync(file, cancellationToken);
                stored = target == MaconomyDocumentTarget.Customer
                    ? await _writer.AttachToCustomerAsync(connection, request.Client!.MaconomyCustomerNumber!, document, cancellationToken)
                    : await _writer.AttachToJobAsync(connection, request.MaconomyJobNumber!, document, cancellationToken);
            }
            file.MaconomyDocumentArchiveNumber = stored.ArchiveNumber;
            file.MaconomyDocumentName = stored.DocumentName;
            file.MaconomyDocumentKey = stored.DocumentKey;
            file.MaconomyDocumentSyncStatus = RemsMaconomyDocumentSyncStatus.Synced;
            file.MaconomyDocumentSyncedOnUtc = DateTime.UtcNow;
            file.MaconomyDocumentSyncError = null;
            _requests.UpdateFile(file);
            await WriteActivityAsync(
                request, ActivityEventTypes.RemsMaconomyDocumentSynced,
                $"Uploaded \"{stored.DocumentName ?? label}\" to Maconomy, in {stored.ArchiveNumber}", actorId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MaconomyException ex)
        {
            await FailAsync(request, file, label, ex.Message, actorId, cancellationToken);
        }
        catch (PortalRefusal ex)
        {
            await FailAsync(request, file, label, ex.Message, actorId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Uploading file {FileId} of request {RemsId} to Maconomy failed unexpectedly.", file.Id, request.Id);
            await FailAsync(request, file, label, $"The portal hit an error: {ex.Message}", actorId, cancellationToken);
        }
    }

    /// <summary>The file as it is sent: its own name and type, and its bytes out of the portal's store.</summary>
    private async Task<MaconomyFile> ReadAsync(RemsFile file, CancellationToken cancellationToken)
    {
        var media = file.Media ?? throw new PortalRefusal("The portal holds no record of the file.");
        await using var stream = await _files!.OpenAsync(media.RelativePath ?? string.Empty, cancellationToken)
            ?? throw new PortalRefusal("The file is no longer in the portal's store.");
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            throw new PortalRefusal("The file is empty.");
        }
        return new MaconomyFile(media.OriginalFileName, media.MimeType, buffer.ToArray());
    }

    private async Task FailAsync(RemsRequest request, RemsFile file, string label, string reason, Guid? actorId, CancellationToken cancellationToken)
    {
        file.MaconomyDocumentSyncStatus = RemsMaconomyDocumentSyncStatus.Failed;
        file.MaconomyDocumentSyncError = reason.Length <= FailureReasonMax ? reason : reason[..FailureReasonMax];
        _requests.UpdateFile(file);
        await WriteActivityAsync(
            request, ActivityEventTypes.RemsMaconomyDocumentSyncFailed, $"\"{label}\" was not uploaded to Maconomy: {file.MaconomyDocumentSyncError}",
            actorId, cancellationToken);
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

    private sealed class PortalRefusal : Exception
    {
        public PortalRefusal(string message) : base(message)
        {
        }
    }
}
