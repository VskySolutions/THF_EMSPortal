using EmsPortal.Api.Filters;
using EmsPortal.Api.Models;
using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.Security;
using EmsPortal.Api.Validators.Rems;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.OptionSets;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Configuration;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;
using EmsPortal.Api.RateLimiting;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// REMS request lifecycle backend (WO-111): the partner dashboard, Admin Pool, and the create/edit/
/// assign/delete actions on a REMS request. The request is one row with its client on the client master;
/// the shapes handed out are unchanged.
/// </summary>
[ApiController]
[LoadMaconomyClients]
[Route("api/rems/requests")]
[Produces("application/json")]
[Tags("REMS Requests")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class RemsRequestsController : ControllerBase
{
    private const string CodeNotDeletable = "REMS_REQUEST_NOT_DELETABLE";
    private const string CodeDuplicateEmail = "REMS_DUPLICATE_CLIENT_EMAIL";
    private const string CodeClientLocked = "REMS_CLIENT_LOCKED";
    private const string CodeMaconomyCustomerNotFound = "REMS_MACONOMY_CUSTOMER_NOT_FOUND";

    /// <summary>How many clients one search returns. The picker says so when it is reached.</summary>
    private const int ClientLookupLimit = 20;

    private readonly IRemsRequestRepository _rems;
    private readonly IClientRepository _clients;
    private readonly IClientNumberGenerator _clientNumbers;
    private readonly IRemsApprovalTaskRepository _approvals;
    private readonly IRemsDelegationRepository _delegations;
    private readonly IRemsNumberGenerator _numberGenerator;
    private readonly IUserRepository _users;
    private readonly IPersonRepository _persons;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityEventWriter _activity;
    private readonly INotificationDispatcher _notifications;
    private readonly IOptionCodeResolver _codes;
    private readonly IMaconomyClientDirectory _maconomy;
    private readonly IMaconomyCustomerSyncService _customerSync;
    /// <summary>Where the SPA is served from — the front half of the client's public form link.</summary>
    private readonly string _baseUrl;

    public RemsRequestsController(
        IRemsRequestRepository rems,
        IClientRepository clients,
        IClientNumberGenerator clientNumbers,
        IRemsApprovalTaskRepository approvals,
        IRemsDelegationRepository delegations,
        IRemsNumberGenerator numberGenerator,
        IUserRepository users,
        IPersonRepository persons,
        IUnitOfWork unitOfWork,
        IActivityEventWriter activity,
        INotificationDispatcher notifications,
        IOptionCodeResolver codes,
        IMaconomyClientDirectory maconomy,
        IMaconomyCustomerSyncService customerSync,
        IOptions<AppOptions> appOptions)
    {
        _rems = rems;
        _clients = clients;
        _clientNumbers = clientNumbers;
        _approvals = approvals;
        _delegations = delegations;
        _numberGenerator = numberGenerator;
        _users = users;
        _persons = persons;
        _unitOfWork = unitOfWork;
        _activity = activity;
        _notifications = notifications;
        _codes = codes;
        _maconomy = maconomy;
        _customerSync = customerSync;
        _baseUrl = appOptions.Value.BaseUrl;
    }

    // -------------------- Dashboard list --------------------

    // Open to every authenticated caller, like the approvals inbox.
    [HttpGet]
    [Authorize]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsRequestRow>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? clientName = null,
        [FromQuery] string? contact = null,
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        [FromQuery] Guid? assignedAdminUserId = null,
        [FromQuery] Guid? cseUserId = null,
        // "NotStarted", or a form status name (Sent, Submitted, Cancelled): the EMS State column's values.
        [FromQuery] string? emsFormState = null,
        // A REMS.EntityType code.
        [FromQuery] string? entityType = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        [FromQuery] DateTime? updatedFrom = null,
        [FromQuery] DateTime? updatedTo = null,
        // The Client column's dropdown — any of the chosen clients (client ids).
        [FromQuery] Guid[]? clientPersonIds = null,
        // A REMS.ClientSubmissionState code: the Client Submission column's values.
        [FromQuery] string? clientSubmissionState = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? poolScope = null,
        // "mine" or "all" (the default), the My Requests toggle.
        [FromQuery] string? ownership = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var privileged = IsPrivileged();

        var options = new RemsRequestListOptions(
            me, privileged, clientName, contact, status, type, assignedAdminUserId,
            createdFrom, createdTo, ParseScope(scope), ParsePoolFilter(poolScope),
            ParseOwnership(ownership), new SortRequest(sortBy, descending), page, limit,
            cseUserId, emsFormState, entityType, clientPersonIds, clientSubmissionState, updatedFrom, updatedTo);
        var (items, total) = await _rems.ListRequestsAsync(options, cancellationToken);

        var names = await _users.GetFullNamesAsync(
            items.SelectMany(r => new[] { r.AdminAssignedToId, r.CSEId, r.CreatedById, r.UpdatedById })
                .Where(id => id.HasValue).Select(id => id!.Value),
            cancellationToken);
        foreach (var item in items)
        {
            await _maconomy.HydrateAsync(item.Client, cancellationToken);
        }
        var syncEnabled = await CustomerSyncEnabledAsync(cancellationToken);
        var canSync = User.HasPermission(Permissions.RemsEngagementsManage);

        var rows = items.Select(r => ToRow(r, me, privileged, names, syncEnabled, canSync)).ToList();
        return Ok(ApiResponseFactory.Paginated(rows, "REMS requests retrieved.", page, limit, total));
    }

    /// <summary>
    /// The clients across the requests the caller may see under the given scope, for the Client column's
    /// dropdown — every client the list could narrow to, so the field filters are not applied.
    /// </summary>
    [HttpGet("clients")]
    [Authorize]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsClientChoice>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Clients(
        [FromQuery] string? scope = null,
        [FromQuery] string? poolScope = null,
        [FromQuery] string? ownership = null,
        CancellationToken cancellationToken = default)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var options = new RemsRequestListOptions(
            me, IsPrivileged(), null, null, null, null, null, null, null,
            ParseScope(scope), ParsePoolFilter(poolScope), ParseOwnership(ownership),
            SortRequest.Default, Page: 1, Limit: 1);
        var clients = await _rems.ListRequestClientsAsync(options, cancellationToken);
        return Ok(ApiResponseFactory.Success(clients, "REMS request clients retrieved."));
    }

    /// <summary>The quick-filter counts for the list above, under the same visibility, scope and filters.</summary>
    [HttpGet("quick-counts")]
    [Authorize]
    [ProducesResponseType<ApiResponse<RemsRequestQuickCounts>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> QuickCounts(
        [FromQuery] string? clientName = null,
        [FromQuery] string? contact = null,
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        [FromQuery] Guid? assignedAdminUserId = null,
        [FromQuery] Guid? cseUserId = null,
        [FromQuery] string? emsFormState = null,
        [FromQuery] string? entityType = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        [FromQuery] DateTime? updatedFrom = null,
        [FromQuery] DateTime? updatedTo = null,
        // The Client column's dropdown — any of the chosen clients (client ids).
        [FromQuery] Guid[]? clientPersonIds = null,
        // A REMS.ClientSubmissionState code: the Client Submission column's values.
        [FromQuery] string? clientSubmissionState = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? poolScope = null,
        [FromQuery] string? ownership = null,
        CancellationToken cancellationToken = default)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var options = new RemsRequestListOptions(
            me, IsPrivileged(), clientName, contact, status, type, assignedAdminUserId,
            createdFrom, createdTo, ParseScope(scope), ParsePoolFilter(poolScope),
            ParseOwnership(ownership), SortRequest.Default, Page: 1, Limit: 1,
            cseUserId, emsFormState, entityType, clientPersonIds, clientSubmissionState, updatedFrom, updatedTo);
        var counts = await _rems.CountQuickFiltersAsync(options, cancellationToken);
        return Ok(ApiResponseFactory.Success(counts, "REMS request quick-filter counts retrieved."));
    }

    // Ungated with the list that leads here, and for the same reason. CanSee below is the real boundary:
    // a request that is not the caller's to read is a 403 whatever permissions they hold.
    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType<ApiResponse<RemsRequestDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var rems = await LoadAsync(id, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        var privileged = IsPrivileged();
        // An approver is the one reader the list rule cannot name: they are not the initiator.
        if (!CanSee(rems, me, privileged)
            && !await _approvals.IsApproverOnRequestAsync(rems.Id, me, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("Not permitted to view this request."));
        }

        var detail = await BuildDetailAsync(rems, me, privileged, cancellationToken);
        return Ok(ApiResponseFactory.Success(detail, "REMS request retrieved."));
    }

    // -------------------- Mutations --------------------

    [HttpPost]
    [RequirePermission(Permissions.RemsRequestsCreate)]
    [ProducesResponseType<ApiResponse<RemsRequestDetail>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateRemsRequestRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me || User.GetActiveTenantId() is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("No active tenant/user context."));
        }

        var type = request.Type;

        // Raised from a related party the client declared: that party is a client row already, so the
        // request is for them and nothing typed about them is read.
        if (request.FromAdditionalEntityId is { } relationshipId
            && await _clients.GetRelationshipAsync(relationshipId, cancellationToken) is { } relationship)
        {
            request.ExistingClientReferenceId = relationship.RelatedClientId;
            request.MaconomyCustomerNumber = null;
            if (type == RemsRequestTypes.BrandNewClient) type = RemsRequestTypes.ExistingClient;
        }

        // A Maconomy customer is named by number, and is by definition somebody THF already has. Read
        // before anything is staged: nothing typed about them is checked, because none of it is kept.
        MaconomyPick? maconomyClient = null;
        if (Normalize(request.MaconomyCustomerNumber) is { } customerNumber)
        {
            var (pick, refusal) = await PickMaconomyClientAsync(customerNumber, tenantId, cancellationToken);
            if (refusal is not null)
            {
                return refusal;
            }

            maconomyClient = pick;
            request.ExistingClientReferenceId = null;
            if (type == RemsRequestTypes.BrandNewClient) type = RemsRequestTypes.ExistingClient;
        }
        else
        {
            // A supplied client reference must resolve to a client.
            if (await RejectUnknownClientReferenceAsync(request.ExistingClientReferenceId, cancellationToken) is { } badClient)
            {
                return badClient;
            }

            // Same name, same client: a request naming somebody already on file is linked to them instead of
            // being filed as new.
            if (request.ExistingClientReferenceId is null
                && await FindSoleClientByExactNameAsync(
                    request.ClientName,
                    KindOf(request.ClientCorporateName, request.ClientFirstName, request.ClientLastName),
                    excludingClientId: null, cancellationToken) is { } matchedClient)
            {
                request.ExistingClientReferenceId = matchedClient.Id;
                if (type == RemsRequestTypes.BrandNewClient) type = RemsRequestTypes.ExistingClient;
            }

            if (await RejectDuplicateClientEmailAsync(
                    request.ExistingClientReferenceId, request.CustomerEmail, null, cancellationToken) is { } emailClash)
            {
                return emailClash;
            }
        }

        // Whose request this is.
        var seat = await RemsActingAs.ResolveAsync(this, _delegations, me, cancellationToken);
        if (seat is { CanPrepare: false })
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("Your delegation does not allow preparing requests."));
        }

        // Always a draft, and always UNASSIGNED. The initiator fills the whole request — client details and
        // engagement setup — and then sends the intake link to the client themselves. The engagement is
        // columns on this row now, so nothing else is created for it.
        var rems = new RemsRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            // Both are foreign keys to their option items. A request is always one of the two types and
            // always starts as a draft, so both resolve or the list has been tampered with.
            TypeId = await _codes.RequireRemsIdAsync(RemsOptionSetKeys.Type, type, cancellationToken),
            StatusId = await _codes.RequireRemsIdAsync(
                RemsOptionSetKeys.Status, RemsRequestStatuses.Draft, cancellationToken),
            CSEId = request.CSEId,
            OnBehalfOfUserId = seat?.PrincipalUserId,
        };
        var details = new ClientDetails(
            request.ClientName, request.ClientNameSuffix,
            request.CustomerEmail, request.CustomerMobileNumber,
            request.ClientFirstName, request.ClientLastName, request.ClientCorporateName);

        // Allocate the REMS number and stage the row + client + activity + attachment atomically. The client
        // is staged first: the request's FK points at them.
        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            rems.RemsNumber = await _numberGenerator.GenerateAsync(tenantId, ct);
            rems.ClientId = await ResolveClientAsync(
                rems, details, tenantId, ct, maconomyClient, request.ExistingClientReferenceId, ownClient: null);
            await _rems.AddAsync(rems, ct);

            await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, rems.Id, ActivityEventTypes.RemsCreated), ct);
            if (request.MediaId is { } mediaId)
            {
                await _rems.AddFileAsync(NewAttachment(rems, mediaId), ct);
            }
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        var created = await LoadAsync(rems.Id, cancellationToken) ?? rems;
        var detail = await BuildDetailAsync(created, me, IsPrivileged(), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiResponseFactory.Success(detail, "REMS request created."));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsRequestDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRemsRequestRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var rems = await LoadAsync(id, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        var privileged = IsPrivileged();
        if (!CanAct(rems, me, privileged))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("Not permitted to edit this request."));
        }

        var current = rems.Client;

        // The client as a Maconomy customer. Naming the one the request already has changes nothing and
        // asks Maconomy nothing, which is what every auto-save of a request for one does.
        var customerNumber = Normalize(request.MaconomyCustomerNumber);
        var sameMaconomyClient = customerNumber is not null && string.Equals(
            current?.MaconomyCustomerNumber, customerNumber, StringComparison.OrdinalIgnoreCase);
        MaconomyPick? maconomyClient = null;
        if (customerNumber is not null)
        {
            if (!sameMaconomyClient)
            {
                if (!IsDraft(rems))
                {
                    return ClientLocked();
                }

                var (pick, refusal) = await PickMaconomyClientAsync(customerNumber, rems.TenantId, cancellationToken);
                if (refusal is not null)
                {
                    return refusal;
                }
                maconomyClient = pick;
            }

            // Nothing typed about a Maconomy customer is kept, so nothing typed is read.
            request.ExistingClientReferenceId = null;
            request.ClearExistingClientReference = false;
            request.ClientName = request.ClientFirstName = request.ClientLastName = null;
            request.ClientCorporateName = request.ClientNameSuffix = null;
            request.CustomerEmail = request.CustomerMobileNumber = null;
            // A Maconomy customer is somebody THF already has, whatever the request said before.
            if ((request.Type ?? rems.Type!.Value) == RemsRequestTypes.BrandNewClient)
            {
                request.Type = RemsRequestTypes.ExistingClient;
            }
        }

        // A supplied client reference must name a client — as on create, and for the same reason: an edit
        // is the other way a reference reaches the request.
        if (await RejectUnknownClientReferenceAsync(request.ExistingClientReferenceId, cancellationToken) is { } badClient)
        {
            return badClient;
        }

        // Whether the request names a client THF already had. The reference column is gone: a request is
        // linked when its type says so, or when its client is a Maconomy customer.
        var linked = IsLinked(rems);

        // A null reference means "leave the link alone", so taking it off is asked for by its own flag.
        var unlink = request.ClearExistingClientReference && request.ExistingClientReferenceId is null && linked;
        if (unlink && !IsDraft(rems))
        {
            return ClientLocked();
        }
        // Nor may a sent request be re-pointed at a different client on file: the intake wrote the client's rows.
        if (request.ExistingClientReferenceId is { } wanted && wanted != rems.ClientId && !IsDraft(rems))
        {
            return ClientLocked();
        }
        var staysLinked = linked && !unlink;
        // A request no longer linked to anybody on file is a request for a brand-new client: with no reference
        // column, the type is what says which it is.
        if (unlink && maconomyClient is null && request.Type is null)
        {
            request.Type = RemsRequestTypes.BrandNewClient;
        }

        // The client as this save leaves them. An omitted field keeps what is already on the record —
        // which for a Maconomy customer is nothing typed.
        var details = new ClientDetails(
            request.ClientName ?? current?.DisplayName,
            request.ClientNameSuffix ?? current?.Person?.Suffix,
            request.CustomerEmail ?? current?.PrimaryEmail,
            request.CustomerMobileNumber ?? current?.PrimaryPhone,
            request.ClientFirstName ?? current?.Person?.FirstName,
            request.ClientLastName ?? current?.Person?.LastName,
            request.ClientCorporateName ?? current?.LegalName);

        // The client this request itself filed, who is not a match for their own name. A client the request
        // was linked to is somebody else's record — including one it is now leaving, who stays as they are.
        var ownClient = linked || current is null || current.IsMaconomyClient ? null : current;

        // Same name, same client — as on create. Confined to a request that is not linked, so an edit can
        // never re-point an existing reference at somebody the name happens to match.
        if (!staysLinked
            && request.ExistingClientReferenceId is null
            && request.ClientName is not null
            && await FindSoleClientByExactNameAsync(
                request.ClientName,
                KindOf(details.CorporateName, details.FirstName, details.LastName),
                ownClient?.Id, cancellationToken) is { } matchedClient)
        {
            request.ExistingClientReferenceId = matchedClient.Id;
            if ((request.Type ?? rems.Type!.Value) == RemsRequestTypes.BrandNewClient)
            {
                request.Type = RemsRequestTypes.ExistingClient;
            }
        }

        // Nor is it a duplicate of its own email — while it stays this request's client. Once somebody
        // else shares the record a changed name files a SECOND one, and that one would be the duplicate.
        var keepsOwnClient = ownClient is not null
            && (await IsOwnToRewriteAsync(rems, ownClient, cancellationToken)
                || ClientShape.Of(details).Describes(ownClient));
        var referenceId = request.ExistingClientReferenceId ?? (staysLinked ? rems.ClientId : null);
        if (await RejectDuplicateClientEmailAsync(
                referenceId, details.Email, keepsOwnClient ? ownClient!.Id : null, cancellationToken) is { } emailClash)
        {
            return emailClash;
        }

        // Description is no longer held on the request; the field is accepted and ignored.
        if (request.Type is not null)
        {
            rems.TypeId = await _codes.RequireRemsIdAsync(RemsOptionSetKeys.Type, request.Type, cancellationToken);
        }
        if (request.CSEId.HasValue) rems.CSEId = request.CSEId;

        // The client IS the master record now, so this is where an edited name, suffix, email or mobile
        // actually lands.
        if (!sameMaconomyClient)
        {
            rems.ClientId = await ResolveClientAsync(
                rems, details, rems.TenantId, cancellationToken, maconomyClient, referenceId, ownClient);
        }

        // Editing never moves a request along any more. A draft leaves draft only by being sent to the
        // client, which is its own action.
        _rems.Update(rems);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await LoadAsync(rems.Id, cancellationToken) ?? rems;
        var detail = await BuildDetailAsync(refreshed, me, privileged, cancellationToken);
        return Ok(ApiResponseFactory.Success(detail, "REMS request updated."));
    }

    /// <summary>Attach previously-uploaded media (POST /api/media) to a request.</summary>
    [HttpPost("{id:guid}/files")]
    [RequirePermission(Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsRequestDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddFiles(Guid id, [FromBody] AddRemsFilesRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var rems = await LoadAsync(id, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        var privileged = IsPrivileged();
        if (!CanAct(rems, me, privileged))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("Not permitted to edit this request."));
        }

        var existing = Attachments(rems).Select(f => f.MediaId).ToHashSet();
        foreach (var mediaId in request.MediaIds.Distinct().Where(m => m != Guid.Empty && !existing.Contains(m)))
        {
            await _rems.AddFileAsync(NewAttachment(rems, mediaId), cancellationToken);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await LoadAsync(rems.Id, cancellationToken) ?? rems;
        var detail = await BuildDetailAsync(refreshed, me, privileged, cancellationToken);
        return Ok(ApiResponseFactory.Success(detail, "REMS request attachments added."));
    }

    /// <summary>Takes one attached file off a request.</summary>
    [HttpDelete("{id:guid}/files/{fileId:guid}")]
    [RequirePermission(Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsRequestDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveFile(Guid id, Guid fileId, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var rems = await LoadAsync(id, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        var privileged = IsPrivileged();
        if (!CanAct(rems, me, privileged))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("Not permitted to edit this request."));
        }

        // Attachments only: the acceptance form and purchase order are the engagement setup's to manage.
        var file = Attachments(rems).FirstOrDefault(f => f.Id == fileId);
        if (file is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Attachment not found on this request."));
        }

        // The DbContext converts the delete into a soft-delete (Deleted flag).
        _rems.RemoveFile(file);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await LoadAsync(rems.Id, cancellationToken) ?? rems;
        var detail = await BuildDetailAsync(refreshed, me, privileged, cancellationToken);
        return Ok(ApiResponseFactory.Success(detail, "REMS request attachment removed."));
    }

    // -------------------- Pick up / hand back --------------------

    /// <summary>The calling admin claims this request as its reviewing admin.</summary>
    [HttpPost("{id:guid}/pick-up")]
    [RequirePermission(Permissions.RemsRequestsAssign)]
    [ProducesResponseType<ApiResponse<RemsRequestDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PickUp(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        if (RejectNonReviewer() is { } notReviewer)
        {
            return notReviewer;
        }

        var rems = await LoadAsync(id, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        // A draft has not been submitted to anybody yet — it is still its initiator's private working copy,
        // and there is nothing on it for an admin to take over.
        if (IsDraft(rems))
        {
            return Conflict(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Cannot pick this request up.",
                "This request is still a draft — its initiator has not sent it to the client yet."));
        }

        if (rems.AdminAssignedToId is { } holder && holder != me)
        {
            var holderNames = await _users.GetFullNamesAsync(new[] { holder }, cancellationToken);
            return Conflict(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Cannot pick this request up.",
                $"{NameOf(holderNames, holder) ?? "Another admin"} picked this request up already."));
        }

        var privileged = IsPrivileged();
        if (rems.AdminAssignedToId is null)
        {
            // Who holds the request changes; its status does not, so there is no history row to write.
            rems.AdminAssignedToId = me;
            _rems.Update(rems);
            await _activity.WriteAsync(new CreateActivityEventDto(
                EntityType.Rems, rems.Id, ActivityEventTypes.RemsAssigned, null, me.ToString()), cancellationToken);
            // Nobody tells the picker what they just did. The person waiting on the request is the one who
            // raised it, and until now their submission has been met with silence.
            await NotifyRequesterOfPickUpAsync(rems, me, me, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var refreshed = await LoadAsync(rems.Id, cancellationToken) ?? rems;
        var detail = await BuildDetailAsync(refreshed, me, privileged, cancellationToken);
        return Ok(ApiResponseFactory.Success(detail, "REMS request picked up."));
    }

    /// <summary>
    /// The holding admin returns the request to the pool, so it reads "Waiting for pickup" again and
    /// any admin may take it.
    /// </summary>
    [HttpPost("{id:guid}/hand-back")]
    [RequirePermission(Permissions.RemsRequestsAssign)]
    [ProducesResponseType<ApiResponse<RemsRequestDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> HandBack(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        if (RejectNonReviewer() is { } notReviewer)
        {
            return notReviewer;
        }

        var rems = await LoadAsync(id, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        // Yours to give back, or an elevated caller's to prise loose — the remedy when the admin holding a
        // request is away and somebody else has to work it.
        if (rems.AdminAssignedToId is { } holder && holder != me && !RemsSetupAccess.IsElevated(User))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("Only the admin holding this request can hand it back."));
        }

        var privileged = IsPrivileged();
        if (rems.AdminAssignedToId is { } previous)
        {
            rems.AdminAssignedToId = null;
            _rems.Update(rems);
            await _activity.WriteAsync(new CreateActivityEventDto(
                EntityType.Rems, rems.Id, ActivityEventTypes.RemsAssigned, previous.ToString(), null), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var refreshed = await LoadAsync(rems.Id, cancellationToken) ?? rems;
        var detail = await BuildDetailAsync(refreshed, me, privileged, cancellationToken);
        return Ok(ApiResponseFactory.Success(detail, "REMS request handed back."));
    }

    // -------------------- The admin ↔ initiator rework loop --------------------

    /// <summary>
    /// The Admin returns a request to its initiator because the Engagement Setup needs work, with a
    /// mandatory reason.
    /// </summary>
    [HttpPost("{id:guid}/send-back")]
    [EnableRateLimiting(RateLimitPolicies.Email)]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<RemsRequestDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendBack(Guid id, [FromBody] SendBackRemsRequestRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var rems = await LoadAsync(id, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        // Only from a stage the Admin actually holds. A request out with the client, already with the
        // approvers, or already returned is not theirs to send back.
        if (rems.Status!.Value is not (RemsRequestStatuses.AdminReview or RemsRequestStatuses.AwaitingAdminConfirmation))
        {
            return Conflict(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Cannot send back.",
                "Only a request under admin review can be sent back to its initiator."));
        }

        // Who the admin is handing it to.
        var toCse = string.Equals(request.ReturnTo, RemsSendBackTargets.Cse, StringComparison.OrdinalIgnoreCase);
        if (toCse && rems.CSEId is null)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Cannot send back.",
                "This request has no CSE named on it, so there is nobody to hand the rework to. Send it to the initiator instead."));
        }
        if (toCse && !await RemsSetupAccess.InitiatorHasCoverAsync(_delegations, rems, cancellationToken))
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Cannot send back.",
                "The person who raised this request has not named a REMS delegate, so their rework cannot be handed to the CSE. Send it to the initiator instead."));
        }
        var returnedTo = toCse ? rems.CSEId : rems.CreatedById;

        // The send-back IS the status change: a history row to returned_to_initiator with the reason and
        // who it went to, open while it is the latest row.
        var reason = request.Reason.Trim();
        await MoveToAsync(rems, RemsRequestStatuses.ReturnedToInitiator, me, reason, returnedTo, cancellationToken);
        _rems.Update(rems);
        await _activity.WriteAsync(new CreateActivityEventDto(
            EntityType.Rems, rems.Id, ActivityEventTypes.RemsSentBack, null, reason), cancellationToken);

        // Both are told either way: the one being asked, and the other so they are not working a request
        // that has moved under them. Only the wording differs, so nobody has to guess whose turn it is.
        var ownerName = returnedTo is { } owner
            ? (await _users.GetFullNamesAsync(new[] { owner }, cancellationToken))
                .TryGetValue(owner, out var name) ? name : null
            : null;
        foreach (var userId in Recipients(rems.CreatedById, rems.CSEId))
        {
            var forMe = userId == returnedTo;
            await _notifications.DispatchAsync(new CreateNotificationDto(
                userId, NotificationType.RemsRequestSubmitted,
                forMe
                    ? "A REMS request was sent back to you for engagement setup"
                    : $"A REMS request was sent back for engagement setup{(ownerName is null ? "" : $" — to {ownerName}")}",
                $"{rems.RemsNumber} — {ClientNameOf(rems)}: {reason}", EntityType.Rems, rems.Id), cancellationToken);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await LoadAsync(rems.Id, cancellationToken) ?? rems;
        var detail = await BuildDetailAsync(refreshed, me, IsPrivileged(), cancellationToken);
        return Ok(ApiResponseFactory.Success(detail, "Request sent back to its initiator."));
    }

    /// <summary>The initiator hands the revised Engagement Setup back to the Admin to confirm.</summary>
    [HttpPost("{id:guid}/return-to-admin")]
    [RequirePermission(Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsRequestDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReturnToAdmin(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var rems = await LoadAsync(id, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }
        if (rems.Status!.Value is not (RemsRequestStatuses.ReturnedToInitiator or RemsRequestStatuses.ChangesRequested))
        {
            return Conflict(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Cannot return to admin.",
                "This request is not currently with its initiator for rework."));
        }
        if (!CanAct(rems, me, IsPrivileged()))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("Not permitted to act on this request."));
        }

        // There is no open return to close: a send-back is open while it is the latest history row, so
        // the row this move appends is what resolves it.
        await MoveToAsync(rems, RemsRequestStatuses.AwaitingAdminConfirmation, me, null, null, cancellationToken);
        _rems.Update(rems);
        await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, rems.Id, ActivityEventTypes.RemsReturnedToAdmin), cancellationToken);

        foreach (var userId in Recipients(rems.AdminAssignedToId))
        {
            await _notifications.DispatchAsync(new CreateNotificationDto(
                userId, NotificationType.RemsRequestPickedUp,                "A REMS engagement setup was revised",
                $"{rems.RemsNumber} — {ClientNameOf(rems)}", EntityType.Rems, rems.Id), cancellationToken);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await LoadAsync(rems.Id, cancellationToken) ?? rems;
        var detail = await BuildDetailAsync(refreshed, me, IsPrivileged(), cancellationToken);
        return Ok(ApiResponseFactory.Success(detail, "Revised setup returned to the admin."));
    }

    /// <summary>Every time this request was returned to its initiator, oldest first, with the reason given.</summary>
    [HttpGet("{id:guid}/send-backs")]
    [RequirePermission(Permissions.RemsRequestsRead)]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsSendBackView>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendBacks(Guid id, CancellationToken cancellationToken)
    {
        if (await _rems.GetByIdAsync(id, cancellationToken) is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        var rows = await _rems.ListSendBacksAsync(id, cancellationToken);
        // A send-back is resolved by whatever status change came after it, so the whole history is read.
        var history = await _rems.ListStatusHistoryAsync(id, cancellationToken);
        // One lookup covering both who returned it and who it was handed to.
        var names = await _users.GetFullNamesAsync(
            rows.SelectMany(r => new[] { r.ChangedByUserId ?? r.CreatedById, r.ReturnedToUserId })
                .Where(x => x.HasValue).Select(x => x!.Value).Distinct(),
            cancellationToken);
        var views = rows
            .Select(r => new RemsSendBackView(
                r.Id, r.Reason ?? string.Empty,
                NameOf(names, r.ChangedByUserId ?? r.CreatedById),
                r.ChangedOnUtc, ResolvedOn(history, r),
                NameOf(names, r.ReturnedToUserId)))
            .ToList();
        return Ok(ApiResponseFactory.Success(views, "REMS send-backs retrieved."));
    }

    /// <summary>When the history row after a send-back was written, or null while the send-back is the latest.</summary>
    private static DateTime? ResolvedOn(IReadOnlyList<RemsStatusHistory> history, RemsStatusHistory sendBack)
    {
        for (var i = 0; i < history.Count; i++)
        {
            if (history[i].Id == sendBack.Id)
            {
                return i + 1 < history.Count ? history[i + 1].ChangedOnUtc : null;
            }
        }
        return null;
    }

    /// <summary>Distinct, non-empty notification recipients — the same person may hold two of these seats.</summary>
    private static IEnumerable<Guid> Recipients(params Guid?[] candidates)
        => candidates.Where(c => c.HasValue).Select(c => c!.Value).Distinct();

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.RemsRequestsDelete)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var rems = await _rems.GetByIdAsync(id, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        if (!CanAct(rems, me, IsPrivileged()))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("Not permitted to delete this request."));
        }

        // Hiding the action in the UI is not enough — the same window applies to a direct call.
        if (!IsDeletable(rems))
        {
            return StatusCode(StatusCodes.Status409Conflict, ApiResponseFactory.Error(
                CodeNotDeletable,
                "This request can no longer be deleted.",
                "A request can only be deleted while it is a draft, or submitted and not yet assigned to an admin."));
        }

        // The DbContext converts the delete into a soft-delete (Deleted flag). The status does not change,
        // so no history is written; the client the draft filed stays on file.
        _rems.Remove(rems);
        await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, rems.Id, ActivityEventTypes.RemsDeleted), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(new { id }, "REMS request deleted."));
    }

    // -------------------- Pickers --------------------

    /// <summary>Search of the clients on file (by name, email, phone) for the client picker.</summary>
    [HttpGet("/api/rems/clients/lookup")]
    [RequirePermission(Permissions.RemsRequestsCreate)]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsClientLookupItem>>>(StatusCodes.Status200OK)]
    /// <param name="entityType">
    /// The REMS.EntityType code the request is being raised under. Only clients whose record holds it are
    /// offered. Omitted, every client is — which is what a caller who has not answered the entity type yet
    /// should see, rather than an empty list they cannot explain.
    /// </param>
    /// <param name="excludingRemsId">
    /// Accepted for the contract's sake. The entity type is the client's own record now rather than a
    /// reading off the requests that name them, so there is no request to leave out of the answer.
    /// </param>
    public async Task<IActionResult> ClientLookup(
        [FromQuery] string? q, [FromQuery] string? entityType, [FromQuery] Guid? excludingRemsId,
        CancellationToken cancellationToken)
    {
        var term = q?.Trim() ?? string.Empty;
        if (term.Length == 0)
        {
            return Ok(ApiResponseFactory.Success(
                Array.Empty<RemsClientLookupItem>(), "Enter a name, email or phone number to search."));
        }

        // Clients that are Maconomy customers are not among them: their details are searched in Maconomy.
        // The ambient tenant filter pins the search to the caller's active tenant.
        var items = await _clients.LookupAsync(term, entityType, ClientLookupLimit, cancellationToken);

        var results = items.Select(ToLookupItem).ToList();
        return Ok(ApiResponseFactory.Success(results, "Clients retrieved."));
    }

    /// <summary>One client on file as the picker shows them — what a request already linked to them reads its lock from.</summary>
    [HttpGet("/api/rems/clients/{id:guid}")]
    [RequirePermission(Permissions.RemsRequestsCreate)]
    [ProducesResponseType<ApiResponse<RemsClientLookupItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Client(
        Guid id, [FromQuery] Guid? excludingRemsId, CancellationToken cancellationToken)
    {
        if (await _clients.GetByIdAsync(id, cancellationToken) is not { } client)
        {
            return NotFound(ApiResponseFactory.NotFound("Client not found."));
        }

        await _maconomy.HydrateAsync(client, cancellationToken);
        return Ok(ApiResponseFactory.Success(ToLookupItem(client), "Client retrieved."));
    }

    /// <summary>
    /// Whether a NEW client's name or email already belongs to a client on file: the two checks the save
    /// makes, asked first so the form can say so beside the field.
    /// </summary>
    /// <param name="remsId">The request being edited, whose own client is not a duplicate of itself.</param>
    [HttpGet("/api/rems/clients/on-file")]
    [RequirePermission(Permissions.RemsRequestsCreate)]
    [ProducesResponseType<ApiResponse<RemsClientOnFile>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClientOnFile(
        [FromQuery] string? name, [FromQuery] string? email, [FromQuery] Guid? remsId,
        CancellationToken cancellationToken)
    {
        var rems = remsId is { } id ? await _rems.GetByIdAsync(id, cancellationToken) : null;
        var ownClientId = rems is { Client: { } own } && !IsLinked(rems) && !own.IsMaconomyClient ? own.Id : (Guid?)null;

        // Only an organisation is linked by its name — two people called John Smith are two clients.
        var byName = await FindSoleClientByExactNameAsync(name, organisation: true, ownClientId, cancellationToken);
        var byEmail = Normalize(email) is { } address
            ? (await _clients.FindByEmailAsync(address, ownClientId, cancellationToken)).FirstOrDefault()
            : null;

        var onFile = new RemsClientOnFile(
            byName is null ? null : await ToLookupItemAsync(byName, cancellationToken),
            byEmail is null ? null : await ToLookupItemAsync(byEmail, cancellationToken));
        return Ok(ApiResponseFactory.Success(onFile, "Checked against the clients on file."));
    }

    /// <summary>
    /// The Maconomy half of the client search: customers whose name, number, email or telephone contains
    /// <paramref name="q"/>, in the picker's shape. One a request already names carries that client's id.
    /// Always answers 200 — a Maconomy that cannot be searched is a note beside the results the portal
    /// does have, not a failed search.
    /// </summary>
    [HttpGet("/api/rems/clients/maconomy")]
    [EnableRateLimiting(RateLimitPolicies.MaconomySearch)]
    [RequirePermission(Permissions.RemsRequestsCreate)]
    [ProducesResponseType<ApiResponse<RemsMaconomyLookup>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MaconomyLookup(
        [FromQuery] string? q, [FromQuery] Guid? excludingRemsId, CancellationToken cancellationToken)
    {
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return Ok(ApiResponseFactory.Success(
                new RemsMaconomyLookup(RemsMaconomyLookupStatuses.NotConnected, null, false, []), "No active tenant."));
        }

        try
        {
            var found = await _maconomy.SearchAsync(tenantId, q, cancellationToken);
            var items = new List<RemsClientLookupItem>(found.Customers.Count);
            foreach (var customer in found.Customers)
            {
                // TODO(rems-v2): one read per customer — IClientRepository has no batch lookup by Maconomy number.
                var onFile = await _clients.FindByMaconomyNumberAsync(customer.Number, cancellationToken);
                items.Add(ToLookupItem(customer, onFile?.Id, onFile?.EntityType?.Value));
            }
            return Ok(ApiResponseFactory.Success(
                new RemsMaconomyLookup(RemsMaconomyLookupStatuses.Ok, null, found.More, items),
                "Maconomy customers retrieved."));
        }
        catch (MaconomyException ex)
        {
            var notConnected = ex.Failure == MaconomyFailure.NotConfigured;
            return Ok(ApiResponseFactory.Success(
                new RemsMaconomyLookup(
                    notConnected ? RemsMaconomyLookupStatuses.NotConnected : RemsMaconomyLookupStatuses.Unavailable,
                    notConnected ? null : MaconomyNote(ex), false, []),
                "Maconomy could not be searched."));
        }
    }

    /// <summary>
    /// One Maconomy customer, read from Maconomy NOW rather than from what is held — what a pick shows,
    /// and what "Refresh" re-reads after the customer has been corrected in Maconomy.
    /// </summary>
    [HttpGet("/api/rems/clients/maconomy/customer")]
    [EnableRateLimiting(RateLimitPolicies.MaconomySearch)]
    [RequirePermission(Permissions.RemsRequestsCreate)]
    [ProducesResponseType<ApiResponse<RemsClientLookupItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> MaconomyCustomer(
        [FromQuery] string? number, [FromQuery] Guid? excludingRemsId, CancellationToken cancellationToken)
    {
        if (User.GetActiveTenantId() is not { } tenantId || Normalize(number) is not { } customerNumber)
        {
            return NotFound(ApiResponseFactory.NotFound("Maconomy customer not found."));
        }

        MaconomyClientDetails? customer;
        try
        {
            customer = await _maconomy.ReadAsync(tenantId, customerNumber, cancellationToken);
        }
        catch (MaconomyException ex)
        {
            return MaconomyRefusal(ex);
        }
        if (customer is null)
        {
            return NotFound(ApiResponseFactory.NotFound($"Maconomy has no customer {customerNumber}."));
        }

        var client = await _clients.FindByMaconomyNumberAsync(customer.Number, cancellationToken);
        return Ok(ApiResponseFactory.Success(
            ToLookupItem(customer, client?.Id, client?.EntityType?.Value), "Maconomy customer retrieved."));
    }

    /// <summary>The tenant's Admin and Super Admin users.</summary>
    [HttpGet("/api/rems/admins")]
    // Gated on READING requests rather than on the assign right: what this feeds is the CSE and
    // engagement people-pickers every initiator fills in, none of whom pick anything up.
    [RequirePermission(Permissions.RemsRequestsRead)]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsAdminOption>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Admins([FromQuery] string? role, CancellationToken cancellationToken)
    {
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return Ok(ApiResponseFactory.Success(Array.Empty<RemsAdminOption>(), "No active tenant."));
        }

        IReadOnlyList<User> candidates;
        if (!string.IsNullOrWhiteSpace(role))
        {
            candidates = await _users.ListByTenantRolesAsync(tenantId, new[] { role.Trim() }, cancellationToken);
        }
        else
        {
            candidates = await _users.ListByTenantRolesAsync(tenantId, new[] { Roles.Admin, Roles.SuperAdmin }, cancellationToken);
        }

        var names = await _users.GetFullNamesAsync(candidates.Select(u => u.Id), cancellationToken);
        var options = candidates
            .Select(u => new RemsAdminOption(u.Id, names.TryGetValue(u.Id, out var n) ? n : u.DisplayName, u.Email))
            .OrderBy(o => o.Name)
            .ToList();
        return Ok(ApiResponseFactory.Success(options, "Admins retrieved."));
    }

    // -------------------- Helpers --------------------

    /// <summary>
    /// The request with its client fit to read: a Maconomy customer's card is put on the client here,
    /// since the row holds only the number.
    /// </summary>
    private async Task<RemsRequest?> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var rems = await _rems.GetByIdAsync(id, cancellationToken);
        if (rems is not null)
        {
            await _maconomy.HydrateAsync(rems.Client, cancellationToken);
        }
        return rems;
    }

    /// <summary>
    /// The refusal for a caller who may claim work but does not work the EMS Review queue, or null to
    /// carry on.
    /// </summary>
    private IActionResult? RejectNonReviewer()
        => User.HasPermission(Permissions.RemsEngagementsManage)
            ? null
            : StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("Only an admin who reviews REMS requests can pick one up."));

    /// <summary>An Admin-role or Super Admin caller (sees the whole tenant; everyone else is record-scoped).</summary>
    private bool IsPrivileged() => RemsSetupAccess.IsRemsAdmin(User);

    /// <summary>
    /// Record-level VISIBILITY: privileged callers see the tenant, drafts included; everyone else sees
    /// their own drafts and the non-drafts they created or are involved in.
    /// </summary>
    private static bool CanSee(RemsRequest r, Guid me, bool privileged)
        => IsDraft(r)
            ? privileged || IsMine(r, me)
            : privileged || IsMine(r, me) || r.AdminAssignedToId == me || r.CSEId == me;

    /// <summary>
    /// Whose request this is: the person who created it, or the principal they created it FOR. A
    /// delegate preparing a request for a shareholder produces the shareholder's work.
    /// </summary>
    private static bool IsMine(RemsRequest r, Guid me) => r.CreatedById == me || r.OnBehalfOfUserId == me;

    /// <summary>Record-level ACT (edit/delete): the creator or a privileged caller.</summary>
    private static bool CanAct(RemsRequest r, Guid me, bool privileged)
        => privileged || IsMine(r, me);

    private static bool IsDraft(RemsRequest r) => r.Status is null || r.Status.Value == RemsRequestStatuses.Draft;

    /// <summary>
    /// Whether the request names a client THF already had — picked from the records, matched by name, or
    /// a Maconomy customer — rather than one it filed itself. The old reference column is gone: the form
    /// never offers Brand New Client for a linked record, and never Existing Client without one, so the
    /// type carries the answer.
    /// </summary>
    private static bool IsLinked(RemsRequest r)
        => string.Equals(r.Type?.Value, RemsRequestTypes.ExistingClient, StringComparison.Ordinal)
            || r.Client is { IsMaconomyClient: true };

    private static string ClientNameOf(RemsRequest r) => r.Client?.ClientName ?? string.Empty;

    /// <summary>The client already on file under this exact name, if there is exactly one.</summary>
    /// <param name="organisation">Matches only clients of this kind, so a company is never linked to a person of the same name. Null matches either.</param>
    /// <param name="excludingClientId">The asking request's own client, who is not a match for themselves.</param>
    private async Task<Client?> FindSoleClientByExactNameAsync(
        string? clientName, bool? organisation, Guid? excludingClientId, CancellationToken cancellationToken)
    {
        var name = clientName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length < 2)
        {
            return null;
        }

        var candidates = new List<Client>();
        foreach (var kind in organisation is { } wanted ? new[] { wanted } : new[] { true, false })
        {
            candidates.AddRange(await _clients.FindByExactNameAsync(name, kind, excludingClientId, cancellationToken));
        }
        // A Maconomy customer's name is Maconomy's, not a typed one; an inactive client is not offered.
        var matches = candidates
            .Where(c => !c.IsMaconomyClient && c.Status != ClientStatus.Inactive)
            .DistinctBy(c => c.Id)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Which kind of client the submitted name describes (true for an organisation), or null where it came as one string.</summary>
    private static bool? KindOf(string? corporateName, string? firstName, string? lastName)
    {
        if (Normalize(corporateName) is not null)
        {
            return true;
        }

        return Normalize(firstName) is not null || Normalize(lastName) is not null ? false : null;
    }

    /// <summary>The picker's row for a client found by a partial read, re-read whole so its option items are on it.</summary>
    private async Task<RemsClientLookupItem> ToLookupItemAsync(Client client, CancellationToken cancellationToken)
    {
        var whole = await _clients.GetByIdAsync(client.Id, cancellationToken) ?? client;
        await _maconomy.HydrateAsync(whole, cancellationToken);
        return ToLookupItem(whole);
    }

    /// <summary>The name in PARTS, not one joined string, so the form never has to guess where it splits.</summary>
    private static RemsClientLookupItem ToLookupItem(Client client)
    {
        if (client.IsMaconomyClient)
        {
            // Read off Maconomy, or the stand-in name alone while it cannot be read.
            var customer = client.Maconomy
                ?? new MaconomyClientDetails(client.MaconomyCustomerNumber!, client.ClientName, null, null, null);
            return ToLookupItem(customer, client.Id, client.EntityType?.Value);
        }

        var person = client.Person;
        return new(
            client.Id, client.ClientName, client.ClientEmail, client.ClientPhone, person?.Suffix,
            person?.FirstName ?? string.Empty,
            person?.LastName ?? string.Empty,
            client.LegalName,
            client.IsOrganisation,
            EntityTypeOf(client),
            null,
            client.Industry?.Value);
    }

    /// <summary>
    /// The entity type a client's record holds: a person is always an Individual; an organisation holds
    /// what it was filed under, unless that says Individual, which contradicts its own record.
    /// </summary>
    private static string? EntityTypeOf(Client client)
    {
        if (!client.IsOrganisation)
        {
            return RemsFormPayloadValidator.Individual;
        }
        var filed = client.EntityType?.Value;
        return RemsClientEntityType.IsIndividual(filed) ? null : filed;
    }

    /// <summary>A Maconomy customer in the picker's shape. The name is Maconomy's one string: it has no parts and no suffix.</summary>
    private static RemsClientLookupItem ToLookupItem(MaconomyClientDetails customer, Guid? clientId, string? lastFiledUnder)
    {
        var entityType = customer.EntityType ?? lastFiledUnder;
        return new(
            clientId, customer.Name, customer.Email, customer.Phone, null, string.Empty, string.Empty, null,
            entityType is not null && !RemsClientEntityType.IsIndividual(entityType),
            entityType, customer.Number, customer.Industry);
    }

    /// <summary>A Maconomy customer as a request's client: the record that names them, and whether this save is what creates it.</summary>
    private sealed record MaconomyPick(Client Client, bool IsNew);

    /// <summary>
    /// The client record for a Maconomy customer: the one already naming them, or a new one holding the
    /// number. Reads the customer from Maconomy, so a number that is not a customer — or a Maconomy that
    /// cannot say — is refused before anything is written.
    /// </summary>
    private async Task<(MaconomyPick? Pick, IActionResult? Refusal)> PickMaconomyClientAsync(
        string customerNumber, Guid tenantId, CancellationToken cancellationToken)
    {
        MaconomyClientDetails? customer;
        try
        {
            customer = await _maconomy.ReadAsync(tenantId, customerNumber, cancellationToken);
        }
        catch (MaconomyException ex)
        {
            return (null, MaconomyRefusal(ex));
        }
        if (customer is null)
        {
            return (null, BadRequest(ApiResponseFactory.Error(
                CodeMaconomyCustomerNotFound, "That customer is not in Maconomy.",
                $"Maconomy has no customer {customerNumber}. Search for the client again and pick them from the results.")));
        }

        if (await _clients.FindByMaconomyNumberAsync(customer.Number, cancellationToken) is { } onFile)
        {
            onFile.Maconomy = customer;
            return (new MaconomyPick(onFile, IsNew: false), null);
        }

        var client = new Client
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Status = ClientStatus.Prospect,
            // The number is what the portal reads them by. A client row must name its party, and a Maconomy
            // customer has no person here, so the legal name is Maconomy's — or the stand-in while it says none.
            MaconomyCustomerNumber = customer.Number,
            LegalName = Normalize(customer.Name) ?? EmsPortal.Domain.Entities.Client.MaconomyPlaceholderName(customer.Number),
            EntityTypeId = await _codes.RemsIdAsync(RemsOptionSetKeys.EntityType, customer.EntityType, cancellationToken),
            IndustryId = await _codes.RemsIdAsync(RemsOptionSetKeys.Industry, customer.Industry, cancellationToken),
            Maconomy = customer,
        };
        return (new MaconomyPick(client, IsNew: true), null);
    }

    /// <summary>Maconomy could not be asked: not set up is a conflict, anything else a bad gateway.</summary>
    private IActionResult MaconomyRefusal(MaconomyException ex)
        => ex.Failure == MaconomyFailure.NotConfigured
            ? Conflict(ApiResponseFactory.Error(
                ApiErrorCodes.MaconomyNotConfigured, "Maconomy is not connected.", MaconomyNote(ex)))
            : StatusCode(StatusCodes.Status502BadGateway, ApiResponseFactory.Error(
                ex.Failure is MaconomyFailure.AuthFailed or MaconomyFailure.TokenRejected
                    ? ApiErrorCodes.MaconomyAuthFailed
                    : ApiErrorCodes.MaconomyUnavailable,
                "Maconomy could not be read.", MaconomyNote(ex)));

    /// <summary>What to tell somebody who is not the administrator: what happened, without the connection's details.</summary>
    private static string MaconomyNote(MaconomyException ex) => ex.Failure switch
    {
        MaconomyFailure.NotConfigured => "Maconomy is not connected for this tenant.",
        MaconomyFailure.AuthFailed or MaconomyFailure.TokenRejected =>
            "Maconomy refused the portal's sign-in. An administrator needs to check the connection under Tenant Settings.",
        _ => "Maconomy could not be reached just now. Try again in a moment.",
    };

    private IActionResult ClientLocked()
        => StatusCode(StatusCodes.Status409Conflict, ApiResponseFactory.Error(
            CodeClientLocked,
            "The client can no longer be changed.",
            "This request's intake form has been sent, so the client it names is fixed."));

    /// <summary>
    /// Puts the request's client on the client master and returns who they are: the Maconomy customer it
    /// names, the client on file it is linked to, its own client rewritten in place, or a new one.
    /// </summary>
    /// <param name="details"> The client's details AS SUBMITTED. Taken as a parameter rather than read off the request, because the request holds none of them: they live on the very client this method is about to write. </param>
    /// <param name="maconomyClient">The Maconomy customer the request now names, when it names one: they are the client, and nothing in <paramref name="details"/> is read.</param>
    /// <param name="referenceId">The client on file the request is for, when it is for one.</param>
    /// <param name="ownClient">The client this request filed itself and still has, or null.</param>
    private async Task<Guid> ResolveClientAsync(
        RemsRequest rems, ClientDetails details, Guid tenantId, CancellationToken cancellationToken,
        MaconomyPick? maconomyClient, Guid? referenceId, Client? ownClient)
    {
        var shape = ClientShape.Of(details);

        // TODO(rems-v2): the client a draft filed stays on file as a Prospect once the request moves off it
        // to a client on file or a Maconomy customer — IClientRepository has no Remove, where the old
        // model soft-deleted the orphaned person so its email was freed.
        if (maconomyClient is not null)
        {
            var customer = maconomyClient.Client;
            if (maconomyClient.IsNew)
            {
                customer.ClientNumber = await _clientNumbers.GenerateAsync(cancellationToken);
                await _clients.AddAsync(customer, cancellationToken);
            }
            return customer.Id;
        }

        // Linked to a client on file. A reference that no longer resolves (client deleted, or another
        // tenant's) falls through and is treated as a client we do not have.
        if (referenceId is { } id
            && (id == rems.ClientId && rems.Client is { } loaded ? loaded : await _clients.GetByIdAsync(id, cancellationToken)) is { } matched)
        {
            // Maconomy holds this client's details; their record takes nothing a request types.
            if (matched.IsMaconomyClient)
            {
                return matched.Id;
            }

            var filled = false;
            if (shape.Email is not null && string.IsNullOrWhiteSpace(matched.PrimaryEmail))
            {
                matched.PrimaryEmail = shape.Email;
                filled = true;
            }
            if (shape.Phone is not null && string.IsNullOrWhiteSpace(matched.PrimaryPhone))
            {
                matched.PrimaryPhone = shape.Phone;
                filled = true;
            }
            if (filled)
            {
                _clients.Update(matched);
            }
            // Only into a blank, like the two above: this request's particle is an answer about the
            // client, but a particle already on their record was put there deliberately and is theirs.
            if (shape.Suffix is not null && matched.Person is { } person && string.IsNullOrWhiteSpace(person.Suffix))
            {
                person.Suffix = shape.Suffix;
                person.LastProfileUpdatedOn = DateTime.UtcNow;
                _persons.Update(person);
            }
            return matched.Id;
        }

        if (ownClient is not null)
        {
            if (await IsOwnToRewriteAsync(rems, ownClient, cancellationToken))
            {
                await WriteOwnClientAsync(shape, ownClient, rems, tenantId, cancellationToken);
                return ownClient.Id;
            }

            // Its own client, since shared or made a user: no longer this request's to rewrite, and still
            // its client while nothing about them is being changed.
            if (shape.Describes(ownClient))
            {
                return ownClient.Id;
            }
        }

        var client = new Client
        {
            Id = Guid.NewGuid(),
            // Set explicitly rather than left to ambient stamping: on create the request itself has no
            // tenant yet (it is stamped on save), so the caller's tenant is the only one that is known.
            TenantId = tenantId,
            ClientNumber = await _clientNumbers.GenerateAsync(cancellationToken),
            Status = ClientStatus.Prospect,
            PrimaryEmail = shape.Email,
            PrimaryPhone = shape.Phone,
        };
        if (shape.IsOrganisation)
        {
            client.LegalName = shape.Corporate;
        }
        else
        {
            var person = NewPerson(rems, tenantId);
            shape.WriteTo(person);
            await _persons.AddAsync(person, cancellationToken);
            client.PersonId = person.Id;
            client.Person = person;
        }
        await _clients.AddAsync(client, cancellationToken);
        return client.Id;
    }

    /// <summary>
    /// Rewrites the client this request filed with what it now says, which may change which kind of
    /// party they are: a company has no person row, a person has one.
    /// </summary>
    private async Task WriteOwnClientAsync(ClientShape shape, Client client, RemsRequest rems, Guid tenantId, CancellationToken cancellationToken)
    {
        client.PrimaryEmail = shape.Email;
        client.PrimaryPhone = shape.Phone;
        if (shape.IsOrganisation)
        {
            client.LegalName = shape.Corporate;
            if (client.Person is { } person)
            {
                // The person this request minted is nobody's once its client is a company.
                client.PersonId = null;
                client.Person = null;
                _persons.Remove(person);
            }
        }
        else
        {
            client.LegalName = null;
            if (client.Person is { } person)
            {
                shape.WriteTo(person);
                person.LastProfileUpdatedOn = DateTime.UtcNow;
                _persons.Update(person);
            }
            else
            {
                var minted = NewPerson(rems, tenantId);
                shape.WriteTo(minted);
                await _persons.AddAsync(minted, cancellationToken);
                client.PersonId = minted.Id;
                client.Person = minted;
            }
        }
        _clients.Update(client);
    }

    /// <summary>A person row for a request's individual client, provenance on it so the picker and the lists can tell where it came from.</summary>
    private static Person NewPerson(RemsRequest rems, Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        // Globally unique by construction (the filtered unique index on PersonCode), so no pre-check.
        PersonCode = "PER-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
        TenantId = tenantId,
        IsActive = true,
        LastProfileUpdatedOn = DateTime.UtcNow,
        SourceEntityType = EntityType.Rems,
        SourceEntityId = rems.Id,
    };

    private static RemsFile NewAttachment(RemsRequest rems, Guid mediaId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = rems.TenantId,
        REMSId = rems.Id,
        MediaId = mediaId,
        Kind = RemsFileKind.Attachment,
    };

    private static IEnumerable<RemsFile> Attachments(RemsRequest rems)
        => rems.Files.Where(f => !f.Deleted && f.Kind == RemsFileKind.Attachment);

    /// <summary>
    /// Whether the client this request filed is still its to rewrite: named by no other request, and not
    /// since made a user — a profile is its owner's from that point on, not a by-product of a request.
    /// </summary>
    private async Task<bool> IsOwnToRewriteAsync(RemsRequest rems, Client ownClient, CancellationToken cancellationToken)
        => ownClient.Person?.UserId is null
            && !await _rems.IsClientSharedAsync(ownClient.Id, rems.Id, cancellationToken);

    /// <summary>A client's submitted details, in the shape the client master stores them.</summary>
    private sealed record ClientShape(
        bool IsOrganisation, string? Corporate, string First, string Last, string? Suffix,
        string? Email, string? Phone)
    {
        public static ClientShape Of(ClientDetails client)
        {
            var corporate = Normalize(client.CorporateName);
            var isOrganisation = corporate is not null;
            var firstName = Normalize(client.FirstName);
            var lastName = Normalize(client.LastName);

            // The PARTS where the form sent them, the guessed split only where it did not.
            var (first, last) = isOrganisation
                ? (string.Empty, string.Empty)
                : firstName is not null || lastName is not null
                    ? (firstName ?? string.Empty, lastName ?? string.Empty)
                    : SplitName(client.Name);

            return new ClientShape(
                isOrganisation, corporate, first, last,
                isOrganisation ? null : Normalize(client.Suffix),
                Normalize(client.Email), Normalize(client.Phone));
        }

        /// <summary>Whether this is what the record already says, so that saving it changes nothing.</summary>
        public bool Describes(Client client)
            => client.IsOrganisation == IsOrganisation
                && (IsOrganisation
                    ? Normalize(client.LegalName) == Corporate
                    : client.Person is { } person
                        && person.FirstName.Trim() == First
                        && person.LastName.Trim() == Last
                        && Normalize(person.Suffix) == Suffix)
                && string.Equals(Normalize(client.PrimaryEmail), Email, StringComparison.OrdinalIgnoreCase)
                && Normalize(client.PrimaryPhone) == Phone;

        /// <summary>
        /// The person's half. The display name is the parts joined, without the suffix: it is what the
        /// exact-name match reads, and the suffix is handed out beside the name wherever it is shown.
        /// </summary>
        public void WriteTo(Person person)
        {
            person.FirstName = First;
            person.LastName = Last;
            person.Suffix = Suffix;
            person.DisplayName = string.Join(" ", new[] { First, Last }.Where(p => p.Length > 0));
            person.PrimaryEmail = Email;
            person.MobileNumber = Phone;
        }
    }

    /// <summary>First word is the given name, the rest the family name.</summary>
    private static (string First, string Last) SplitName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        var space = trimmed.IndexOf(' ');
        return space < 0 ? (trimmed, string.Empty) : (trimmed[..space], trimmed[(space + 1)..].Trim());
    }

    /// <summary>
    /// The 409 for giving a client an email another client already holds, or null to carry on. It is
    /// given to a brand-new client, and to one on file whose record has none.
    /// </summary>
    private async Task<IActionResult?> RejectDuplicateClientEmailAsync(
        Guid? existingClientReferenceId, string? email, Guid? excludingClientId, CancellationToken cancellationToken)
    {
        var trimmed = Normalize(email);
        if (trimmed is null)
        {
            return null;
        }

        if (existingClientReferenceId is { } linkedId)
        {
            // A client on file keeps the email their record holds, so nothing is being given. Nor is
            // anything given to a Maconomy customer, whose email is Maconomy's to hold.
            if (await _clients.GetByIdAsync(linkedId, cancellationToken) is not { } linked
                || linked.IsMaconomyClient
                || !string.IsNullOrWhiteSpace(linked.PrimaryEmail))
            {
                return null;
            }

            excludingClientId = linkedId;
        }

        if ((await _clients.FindByEmailAsync(trimmed, excludingClientId, cancellationToken)).FirstOrDefault() is not { } holder)
        {
            return null;
        }

        await _maconomy.HydrateAsync(holder, cancellationToken);
        return StatusCode(StatusCodes.Status409Conflict, ApiResponseFactory.Error(
            CodeDuplicateEmail,
            "A client is already on file with that email address.",
            $"“{holder.ClientName}” is already on file with the email {trimmed}. Search for them in the "
                + "Client box and pick them, or give a different address — an email belongs to one client."));
    }

    /// <summary>The 400 for a client reference that does not name a client, or null to carry on.</summary>
    private async Task<IActionResult?> RejectUnknownClientReferenceAsync(
        Guid? existingClientReferenceId, CancellationToken cancellationToken)
    {
        if (existingClientReferenceId is not { } referenceId)
        {
            return null;
        }

        // Tenant-scoped and soft-delete-filtered by the ambient query filter, so another tenant's client
        // is unknown here in exactly the way a client that does not exist is.
        if (await _clients.GetByIdAsync(referenceId, cancellationToken) is not null)
        {
            return null;
        }

        return BadRequest(ApiResponseFactory.Error(
            ApiErrorCodes.ValidationFailed, "Validation failed.",
            "existingClientReferenceId must name a client on file. Search for the client in the Client "
                + "box and pick them from the results."));
    }

    /// <summary>A request may still be withdrawn while it is a draft.</summary>
    private static bool IsDeletable(RemsRequest r) => IsDraft(r);

    /// <summary>
    /// Moves the request to a status and writes the history row that says so: from, to, who, when, why,
    /// and for a send-back who it went to.
    /// </summary>
    private async Task MoveToAsync(
        RemsRequest rems, string statusCode, Guid changedBy, string? reason, Guid? returnedTo, CancellationToken cancellationToken)
    {
        var toStatusId = await _codes.RequireRemsIdAsync(RemsOptionSetKeys.Status, statusCode, cancellationToken);
        await _rems.AddStatusHistoryAsync(new RemsStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = rems.TenantId,
            REMSId = rems.Id,
            FromStatusId = rems.StatusId,
            ToStatusId = toStatusId,
            ChangedByUserId = changedBy,
            ReturnedToUserId = returnedTo,
            ChangedOnUtc = DateTime.UtcNow,
            Reason = reason,
        }, cancellationToken);
        rems.StatusId = toStatusId;
    }

    /// <summary>Which row actions this caller may perform, combining the record-level rule with the permission.</summary>
    private RemsRowActions ActionsFor(RemsRequest r, Guid me, bool privileged)
    {
        var canAct = CanAct(r, me, privileged);
        return new RemsRowActions(
            CanView: true,
            // Not once it is with the approvers, or approved: the page would open every field read-only.
            CanEdit: canAct && !RemsRequestStatuses.IsFrozen(r.Status!.Value)
                && User.HasPermission(Permissions.RemsRequestsUpdate),
            // Not gated on CanAct, unlike everything around it: picking up is precisely the move made on
            // somebody ELSE's request, by an admin who has no standing on it yet.
            CanPickUp: User.HasPermission(Permissions.RemsRequestsAssign)
                && !IsDraft(r)
                && r.AdminAssignedToId is null,
            CanDelete: canAct && User.HasPermission(Permissions.RemsRequestsDelete) && IsDeletable(r));
    }

    private RemsRequestRow ToRow(
        RemsRequest r, Guid me, bool privileged,
        IReadOnlyDictionary<Guid, string> names,
        bool syncEnabled, bool canSync)
    {
        var client = r.Client;
        var (ems, submission) = MapFormState(RemsWorkspaceMapper.FormStateOf(r));
        return new RemsRequestRow(
            r.Id, r.RemsNumber, ClientNameOf(r), client?.Person?.Suffix,
            r.Type!.Value, r.CreatedOnUtc, r.Status!.Value,
            client?.ClientEmail, client?.ClientPhone,
            UserRefOf(r.AdminAssignedToId, names), UserRefOf(r.CSEId, names),
            client?.EntityType?.Value, ems, submission,
            NameOf(names, r.CreatedById), NameOf(names, r.UpdatedById), r.UpdatedOnUtc,
            ActionsFor(r, me, privileged),
            client?.MaconomyCustomerNumber,
            MaconomySync: SyncViewFor(r, syncEnabled, canSync));
    }

    /// <summary>Whether the caller's tenant posts approved new clients to Maconomy right now.</summary>
    private async Task<bool> CustomerSyncEnabledAsync(CancellationToken cancellationToken)
        => User.GetActiveTenantId() is { } tenantId && await _customerSync.IsEnabledAsync(tenantId, cancellationToken);

    /// <summary>The sync chip for a request: the state is on its client now.</summary>
    private RemsMaconomySyncView? SyncViewFor(RemsRequest r, bool syncEnabled, bool canSync)
        => RemsMaconomySyncViews.For(r, r.Client is { } client && _customerSync.IsStale(client), syncEnabled, canSync);

    private async Task<RemsRequestDetail> BuildDetailAsync(RemsRequest rems, Guid me, bool privileged, CancellationToken cancellationToken)
    {
        var names = await _users.GetFullNamesAsync(
            new[] { rems.AdminAssignedToId, rems.CSEId, rems.CreatedById, rems.UpdatedById }
                .Where(x => x.HasValue).Select(x => x!.Value),
            cancellationToken);
        var form = RemsWorkspaceMapper.FormStateOf(rems);
        var (ems, submission) = MapFormState(form);

        var files = Attachments(rems)
            .Select(f => new RemsFileRef(f.Id, f.MediaId, f.Media?.OriginalFileName, f.Media?.MimeType, f.Media?.FileSize, f.Media?.PublicUrl))
            .ToList();

        // Asked only where a CSE is actually named: with none, the send-back dialog has one answer anyway
        // and there is nothing for a delegation lookup to decide.
        var canSendBackToCse = rems.CSEId is not null
            && await RemsSetupAccess.InitiatorHasCoverAsync(_delegations, rems, cancellationToken);

        // A Maconomy customer's name is one string from Maconomy: it has no parts to give the form.
        var client = rems.Client;
        var whole = client is null || client.IsOrganisation || client.IsMaconomyClient;

        var maconomySync = SyncViewFor(
            rems, await CustomerSyncEnabledAsync(cancellationToken), User.HasPermission(Permissions.RemsEngagementsManage));

        return new RemsRequestDetail(
            rems.Id, rems.RemsNumber,
            // No longer held on the request; the field stays in the contract and reads as empty.
            null,
            ClientNameOf(rems),
            client?.Person?.Suffix,
            whole ? null : client!.Person?.FirstName,
            whole ? null : client!.Person?.LastName,
            client is { IsMaconomyClient: false } ? client.LegalName : null,
            rems.Type!.Value, rems.Status!.Value, client?.ClientEmail, client?.ClientPhone,
            // Both are the client master's id now: the reference when the request names a client on file,
            // and the record the client is either way.
            IsLinked(rems) ? rems.ClientId : null, rems.ClientId,
            UserRefOf(rems.AdminAssignedToId, names), UserRefOf(rems.CSEId, names),
            form.EntityType, ems, submission, files,
            RecordAudit.From(rems, RecordAudit.Names(names)),
            ActionsFor(rems, me, privileged),
            canSendBackToCse,
            ClientFormLink(form),
            client?.MaconomyCustomerNumber,
            MaconomyUnavailable: client is { IsMaconomyClient: true, Maconomy: null },
            MaconomySync: maconomySync);
    }

    /// <summary>The client's intake link while the form is out with them, or null.</summary>
    private string? ClientFormLink(RemsFormStateInfo? form)
        => form is not null
            && !string.IsNullOrWhiteSpace(form.InviteCode)
            && form.FormSentOnUtc is not null
            && form.FormStatus is not (RemsFormStatus.Submitted or RemsFormStatus.Cancelled)
                ? $"{_baseUrl.TrimEnd('/')}/rems/form/{form.InviteCode}"
                : null;

    /// <summary>Projects the (optional) EMS form into dashboard state strings.</summary>
    private static (string EmsFormState, string? ClientSubmissionState) MapFormState(RemsFormStateInfo? form)
        => RemsWorkspaceMapper.FormState(form);

    // RemsRequestAssigned carries the pool broadcast: sent to every admin when a client's answers land on an
    // unclaimed request (see RemsPublicFormController).

    /// <summary>Tells whoever raised the request that an admin now owns it.</summary>
    private async Task NotifyRequesterOfPickUpAsync(RemsRequest rems, Guid adminUserId, Guid actorId, CancellationToken cancellationToken)
    {
        if (rems.CreatedById is not { } requesterId || requesterId == actorId)
        {
            return;
        }

        var names = await _users.GetFullNamesAsync(new[] { adminUserId }, cancellationToken);
        var adminName = NameOf(names, adminUserId) ?? "An admin";
        await _notifications.DispatchAsync(new CreateNotificationDto(
            requesterId,
            NotificationType.RemsRequestPickedUp,
            "Your REMS request was picked up",
            $"{rems.RemsNumber} — {adminName} is now handling it.",
            EntityType.Rems,
            rems.Id), cancellationToken);
    }

    private static RemsUserRef? UserRefOf(Guid? id, IReadOnlyDictionary<Guid, string> names)
        => id is { } uid ? new RemsUserRef(uid, names.TryGetValue(uid, out var name) ? name : string.Empty) : null;

    private static string? NameOf(IReadOnlyDictionary<Guid, string> names, Guid? id)
        => id.HasValue && names.TryGetValue(id.Value, out var name) ? name : null;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static RemsListScope ParseScope(string? scope) => scope?.Trim().ToLowerInvariant() switch
    {
        "partner" => RemsListScope.Partner,
        "pool" => RemsListScope.Pool,
        _ => RemsListScope.All,
    };

    private static RemsPoolFilter ParsePoolFilter(string? poolScope) => poolScope?.Trim().ToLowerInvariant() switch
    {
        "unassigned" => RemsPoolFilter.Unassigned,
        "mine" => RemsPoolFilter.Mine,
        _ => RemsPoolFilter.All,
    };

    /// <summary>The My Requests view.</summary>
    private static RemsListOwnership ParseOwnership(string? ownership) => ownership?.Trim().ToLowerInvariant() switch
    {
        "mine" => RemsListOwnership.Mine,
        _ => RemsListOwnership.All,
    };
}
