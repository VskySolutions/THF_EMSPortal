using EmsPortal.Api.Models;
using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.Security;
using EmsPortal.Api.Validators.Rems;
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

namespace EmsPortal.Api.Controllers;

/// <summary>
/// REMS request lifecycle backend (WO-111): the partner dashboard, Admin Pool, and the create/edit/
/// assign/delete actions on a REMS request.
/// </summary>
[ApiController]
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

    private readonly IRemsRepository _rems;
    private readonly IRemsEngagementRepository _engagements;
    private readonly IRemsApprovalRepository _approvals;
    private readonly IRemsDelegationRepository _delegations;
    private readonly IRemsNumberGenerator _numberGenerator;
    private readonly IUserRepository _users;
    private readonly IPersonRepository _persons;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityEventWriter _activity;
    private readonly INotificationDispatcher _notifications;
    private readonly IOptionCodeResolver _codes;
    /// <summary>Where the SPA is served from — the front half of the client's public form link.</summary>
    private readonly string _baseUrl;

    public RemsRequestsController(
        IRemsRepository rems,
        IRemsEngagementRepository engagements,
        IRemsApprovalRepository approvals,
        IRemsDelegationRepository delegations,
        IRemsNumberGenerator numberGenerator,
        IUserRepository users,
        IPersonRepository persons,
        IUnitOfWork unitOfWork,
        IActivityEventWriter activity,
        INotificationDispatcher notifications,
        IOptionCodeResolver codes,
        IOptions<AppOptions> appOptions)
    {
        _rems = rems;
        _engagements = engagements;
        _approvals = approvals;
        _delegations = delegations;
        _numberGenerator = numberGenerator;
        _users = users;
        _persons = persons;
        _unitOfWork = unitOfWork;
        _activity = activity;
        _notifications = notifications;
        _codes = codes;
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
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
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

        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);
        var privileged = IsPrivileged();

        var options = new RemsRequestListOptions(
            me, privileged, clientName, contact, status, type, assignedAdminUserId,
            createdFrom, createdTo, ParseScope(scope), ParsePoolFilter(poolScope),
            ParseOwnership(ownership), new SortRequest(sortBy, descending), page, limit);
        var (items, total) = await _rems.ListRequestsAsync(options, cancellationToken);

        var names = await _users.GetFullNamesAsync(
            items.SelectMany(r => new[] { r.AdminAssignedToId, r.CSEId, r.CreatedById, r.UpdatedById })
                .Where(id => id.HasValue).Select(id => id!.Value),
            cancellationToken);
        var formStates = (await _rems.GetFormStatesAsync(items.Select(r => r.Id).ToList(), cancellationToken))
            .ToDictionary(f => f.RemsId);

        var rows = items.Select(r => ToRow(r, me, privileged, names, formStates));
        return Ok(ApiResponseFactory.Paginated(rows, "REMS requests retrieved.", page, limit, total));
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

        var rems = await _rems.GetByIdAsync(id, cancellationToken);
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

        // A supplied client reference must resolve to a client.
        if (await RejectUnknownClientReferenceAsync(request.ExistingClientReferenceId, cancellationToken) is { } badClient)
        {
            return badClient;
        }

        // Same name, same client: a request naming somebody already on file is linked to them instead of being
        // filed as new.
        var type = request.Type;
        if (request.ExistingClientReferenceId is null
            && await FindSoleClientByExactNameAsync(request.ClientName, cancellationToken) is { } matchedClientId)
        {
            request.ExistingClientReferenceId = matchedClientId;
            if (type == RemsRequestTypes.BrandNewClient) type = RemsRequestTypes.ExistingClient;
        }

        if (await RejectDuplicateClientEmailAsync(
                request.ExistingClientReferenceId, request.CustomerEmail, null, cancellationToken) is { } emailClash)
        {
            return emailClash;
        }

        // Whose request this is.
        var seat = await RemsActingAs.ResolveAsync(this, _delegations, me, cancellationToken);
        if (seat is { CanPrepare: false })
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("Your delegation does not allow preparing requests."));
        }

        // Always a draft, and always UNASSIGNED. The initiator fills the whole request — client details and
        // engagement setup — and then sends the intake link to the client themselves.
        var rems = new REMS
        {
            Id = Guid.NewGuid(),
            Description = request.Description,
            // Both are foreign keys to their option items. A request is always one of the two types and
            // always starts as a draft, so both resolve or the list has been tampered with.
            TypeId = await _codes.RequireRemsIdAsync(RemsOptionSetKeys.Type, type, cancellationToken),
            StatusId = await _codes.RequireRemsIdAsync(
                RemsOptionSetKeys.Status, RemsRequestStatuses.Draft, cancellationToken),
            // The client's name, suffix, email and mobile are NOT set here any more.
            CSEId = request.CSEId,
            ExistingClientReferenceId = request.ExistingClientReferenceId,
            OnBehalfOfUserId = seat?.PrincipalUserId,
        };

        // Allocate the REMS number and stage the row + client person + activity + attachment atomically.
        // The person is staged first: the request's FK points at them.
        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            rems.REMSNumber = await _numberGenerator.GenerateAsync(tenantId, ct);
            rems.ClientPersonId = await ResolveClientPersonAsync(
                rems,
                new ClientDetails(
                    request.ClientName, request.ClientNameSuffix,
                    request.CustomerEmail, request.CustomerMobileNumber,
                    request.ClientFirstName, request.ClientLastName, request.ClientCorporateName),
                tenantId, ct);
            await _rems.AddAsync(rems, ct);

            // The request's one engagement, created here rather than on client submit.
            await _engagements.AddAsync(new REMSEngagement
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                REMSId = rems.Id,
                Status = RemsEngagementStatus.Draft,
            }, ct);

            await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, rems.Id, ActivityEventTypes.RemsCreated), ct);
            if (request.MediaId is { } mediaId)
            {
                await _rems.AddFileAsync(new REMSFiles { Id = Guid.NewGuid(), REMSId = rems.Id, MediaId = mediaId }, ct);
            }
            // Mark the additional-entity row this came from as dealt with, so the originating request stops
            // flagging it.
            if (request.FromAdditionalEntityId is { } sourceRowId
                && await _rems.GetAdditionalEntityAsync(sourceRowId, ct) is { CreatedREMSId: null } sourceRow)
            {
                sourceRow.CreatedREMSId = rems.Id;
                _rems.UpdateAdditionalEntity(sourceRow);
            }
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        var created = await _rems.GetByIdAsync(rems.Id, cancellationToken) ?? rems;
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

        var rems = await _rems.GetByIdAsync(id, cancellationToken);
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

        // No assignment block here any more.

        // A supplied client reference must name a client — as on create, and for the same reason: an edit
        // is the other way a reference reaches the request.
        if (await RejectUnknownClientReferenceAsync(request.ExistingClientReferenceId, cancellationToken) is { } badClient)
        {
            return badClient;
        }

        // Same name, same client — as on create. Confined to a request that is not already linked, so an
        // edit can never re-point an existing reference at somebody the name happens to match.
        if (rems.ExistingClientReferenceId is null
            && request.ExistingClientReferenceId is null
            && request.ClientName is not null
            && await FindSoleClientByExactNameAsync(request.ClientName, cancellationToken) is { } matchedClientId)
        {
            request.ExistingClientReferenceId = matchedClientId;
            if ((request.Type ?? rems.Type!.Value) == RemsRequestTypes.BrandNewClient)
            {
                request.Type = RemsRequestTypes.ExistingClient;
            }
        }

        // The client this request already minted is not a duplicate of itself, so it is excluded.
        if (await RejectDuplicateClientEmailAsync(
                request.ExistingClientReferenceId ?? rems.ExistingClientReferenceId,
                request.CustomerEmail ?? rems.CustomerEmail,
                rems.ClientPersonId,
                cancellationToken) is { } emailClash)
        {
            return emailClash;
        }
        if (request.Description is not null) rems.Description = request.Description;
        if (request.Type is not null)
        {
            rems.TypeId = await _codes.RequireRemsIdAsync(RemsOptionSetKeys.Type, request.Type, cancellationToken);
        }
        // The client's name, suffix, email and mobile are not the REQUEST's to hold any more.
        if (request.CSEId.HasValue) rems.CSEId = request.CSEId;
        if (request.ExistingClientReferenceId.HasValue) rems.ExistingClientReferenceId = request.ExistingClientReferenceId;

        // The client IS the person record now, so this is where an edited name, suffix, email or mobile
        // actually lands. An omitted field leaves what is already on the person alone.
        rems.ClientPersonId = await ResolveClientPersonAsync(
            rems,
            new ClientDetails(
                request.ClientName ?? rems.ClientPerson?.ClientDisplayName,
                request.ClientNameSuffix ?? rems.ClientNameSuffix,
                request.CustomerEmail ?? rems.CustomerEmail,
                request.CustomerMobileNumber ?? rems.CustomerMobileNumber,
                request.ClientFirstName ?? rems.ClientPerson?.FirstName,
                request.ClientLastName ?? rems.ClientPerson?.LastName,
                request.ClientCorporateName ?? rems.ClientPerson?.CorporateName),
            rems.TenantId, cancellationToken);

        // Editing never moves a request along any more. A draft leaves draft only by being sent to the
        // client, which is its own action.
        _rems.Update(rems);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _rems.GetByIdAsync(rems.Id, cancellationToken) ?? rems;
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

        var rems = await _rems.GetByIdAsync(id, cancellationToken);
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

        var existing = rems.Files.Where(f => !f.Deleted).Select(f => f.MediaId).ToHashSet();
        foreach (var mediaId in request.MediaIds.Distinct().Where(m => m != Guid.Empty && !existing.Contains(m)))
        {
            await _rems.AddFileAsync(new REMSFiles { Id = Guid.NewGuid(), REMSId = rems.Id, MediaId = mediaId }, cancellationToken);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _rems.GetByIdAsync(rems.Id, cancellationToken) ?? rems;
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

        var rems = await _rems.GetByIdAsync(id, cancellationToken);
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

        var file = rems.Files.FirstOrDefault(f => f.Id == fileId && !f.Deleted);
        if (file is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Attachment not found on this request."));
        }

        // The DbContext converts the delete into a soft-delete (Deleted flag).
        _rems.RemoveFile(file);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _rems.GetByIdAsync(rems.Id, cancellationToken) ?? rems;
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

        var rems = await _rems.GetByIdAsync(id, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        // A draft has not been submitted to anybody yet — it is still its initiator's private working copy,
        // and there is nothing on it for an admin to take over.
        if (rems.Status!.Value == RemsRequestStatuses.Draft)
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
            rems.AdminAssignedToId = me;
            _rems.Update(rems);
            await _activity.WriteAsync(new CreateActivityEventDto(
                EntityType.Rems, rems.Id, ActivityEventTypes.RemsAssigned, null, me.ToString()), cancellationToken);
            // Nobody tells the picker what they just did. The person waiting on the request is the one who
            // raised it, and until now their submission has been met with silence.
            await NotifyRequesterOfPickUpAsync(rems, me, me, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var refreshed = await _rems.GetByIdAsync(rems.Id, cancellationToken) ?? rems;
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

        var rems = await _rems.GetByIdAsync(id, cancellationToken);
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

        var refreshed = await _rems.GetByIdAsync(rems.Id, cancellationToken) ?? rems;
        var detail = await BuildDetailAsync(refreshed, me, privileged, cancellationToken);
        return Ok(ApiResponseFactory.Success(detail, "REMS request handed back."));
    }

    // -------------------- The admin ↔ initiator rework loop --------------------

    /// <summary>
    /// The Admin returns a request to its initiator because the Engagement Setup needs work, with a
    /// mandatory reason.
    /// </summary>
    [HttpPost("{id:guid}/send-back")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<RemsRequestDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendBack(Guid id, [FromBody] SendBackRemsRequestRequest request, CancellationToken cancellationToken)
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

        var reason = request.Reason.Trim();
        await _rems.AddSendBackAsync(new REMSSendBack
        {
            Id = Guid.NewGuid(),
            TenantId = rems.TenantId,
            REMSId = rems.Id,
            Reason = reason,
            ReturnedToUserId = returnedTo,
        }, cancellationToken);

        rems.StatusId = await _codes.RequireRemsIdAsync(
            RemsOptionSetKeys.Status, RemsRequestStatuses.ReturnedToInitiator, cancellationToken);
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
                $"{rems.REMSNumber} — {rems.ClientDisplayName}: {reason}", EntityType.Rems, rems.Id), cancellationToken);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _rems.GetByIdAsync(rems.Id, cancellationToken) ?? rems;
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

        var rems = await _rems.GetByIdAsync(id, cancellationToken);
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

        // Close the open return, if this came round the admin's loop. A round declined by the approvers
        // has no send-back row to close — its reasons live on the round's tasks.
        if (await _rems.GetOpenSendBackAsync(rems.Id, cancellationToken) is { } open)
        {
            open.ResolvedOnUtc = DateTime.UtcNow;
            _rems.UpdateSendBack(open);
        }

        rems.StatusId = await _codes.RequireRemsIdAsync(
            RemsOptionSetKeys.Status, RemsRequestStatuses.AwaitingAdminConfirmation, cancellationToken);
        _rems.Update(rems);
        await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, rems.Id, ActivityEventTypes.RemsReturnedToAdmin), cancellationToken);

        foreach (var userId in Recipients(rems.AdminAssignedToId))
        {
            await _notifications.DispatchAsync(new CreateNotificationDto(
                userId, NotificationType.RemsRequestPickedUp,                "A REMS engagement setup was revised",
                $"{rems.REMSNumber} — {rems.ClientDisplayName}", EntityType.Rems, rems.Id), cancellationToken);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _rems.GetByIdAsync(rems.Id, cancellationToken) ?? rems;
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
        // One lookup covering both who returned it and who it was handed to.
        var names = await _users.GetFullNamesAsync(
            rows.SelectMany(r => new[] { r.CreatedById, r.ReturnedToUserId })
                .Where(x => x.HasValue).Select(x => x!.Value).Distinct(),
            cancellationToken);
        var views = rows
            .Select(r => new RemsSendBackView(
                r.Id, r.Reason,
                r.CreatedById is { } by && names.TryGetValue(by, out var n) ? n : null,
                r.CreatedOnUtc, r.ResolvedOnUtc,
                r.ReturnedToUserId is { } to && names.TryGetValue(to, out var toName) ? toName : null))
            .ToList();
        return Ok(ApiResponseFactory.Success(views, "REMS send-backs retrieved."));
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

        // The DbContext converts the delete into a soft-delete (Deleted flag).
        _rems.Remove(rems);
        await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, rems.Id, ActivityEventTypes.RemsDeleted), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(new { id }, "REMS request deleted."));
    }

    // -------------------- Pickers --------------------

    /// <summary>Search of existing <see cref="Person"/> records (by name, email, phone) for the client picker.</summary>
    [HttpGet("/api/rems/clients/lookup")]
    [RequirePermission(Permissions.RemsRequestsCreate)]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsClientLookupItem>>>(StatusCodes.Status200OK)]
    /// <param name="entityType">
    /// The REMS.EntityType code the request is being raised under. It decides which KIND of client the
    /// picker offers: <c>individual</c> offers people, anything else offers organisations. Omitted, the
    /// picker offers both — which is what a caller who has not answered the entity type yet should see,
    /// rather than an empty list they cannot explain.
    /// </param>
    public async Task<IActionResult> ClientLookup(
        [FromQuery] string? q, [FromQuery] string? entityType, CancellationToken cancellationToken)
    {
        var term = q?.Trim() ?? string.Empty;
        if (term.Length == 0)
        {
            return Ok(ApiResponseFactory.Success(
                Array.Empty<RemsClientLookupItem>(), "Enter a name, email or phone number to search."));
        }

        // A request for an Individual can only be filed under a person.
        PartyType? partyType = string.IsNullOrWhiteSpace(entityType)
            ? null
            : entityType.Trim().Equals(RemsFormPayloadValidator.Individual, StringComparison.OrdinalIgnoreCase)
                ? PartyType.Individual
                : PartyType.Organisation;

        // The ambient tenant filter pins the search to the caller's active tenant.
        var (items, _) = await _persons.ListAsync(
            term, tenantId: null, isUser: null, isActive: true, SortRequest.Default, page: 1, limit: 20,
            sourceEntityType: EntityType.Client, partyType: partyType, cancellationToken: cancellationToken);

        // The PARTS, not one joined string.
        var results = items.Select(p => new RemsClientLookupItem(
            p.Id, p.ClientDisplayName, p.PrimaryEmail, p.MobileNumber, p.Suffix,
            p.IsOrganisation ? string.Empty : p.FirstName,
            p.IsOrganisation ? string.Empty : p.LastName,
            p.CorporateName,
            p.IsOrganisation));
        return Ok(ApiResponseFactory.Success(results, "Clients retrieved."));
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
    private static bool CanSee(REMS r, Guid me, bool privileged)
        => r.Status!.Value == RemsRequestStatuses.Draft
            ? privileged || IsMine(r, me)
            : privileged || IsMine(r, me) || r.AdminAssignedToId == me || r.CSEId == me;

    /// <summary>
    /// Whose request this is: the person who created it, or the principal they created it FOR. A
    /// delegate preparing a request for a shareholder produces the shareholder's work.
    /// </summary>
    private static bool IsMine(REMS r, Guid me) => r.CreatedById == me || r.OnBehalfOfUserId == me;

    /// <summary>Record-level ACT (edit/delete): the creator or a privileged caller.</summary>
    private static bool CanAct(REMS r, Guid me, bool privileged)
        => privileged || IsMine(r, me);

    /// <summary>The client already on file under this exact name, if there is exactly one.</summary>
    private async Task<Guid?> FindSoleClientByExactNameAsync(string? clientName, CancellationToken cancellationToken)
    {
        var name = clientName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length < 2)
        {
            return null;
        }

        var (candidates, _) = await _persons.ListAsync(
            name, tenantId: null, isUser: null, isActive: true, SortRequest.Default, page: 1, limit: 20,
            sourceEntityType: EntityType.Client, cancellationToken: cancellationToken);
        var matches = candidates
            .Where(p => string.Equals(p.FullName.Trim(), name, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Id)
            .Distinct()
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// Puts the request's client into the Persons table and returns who they are, so a client entered
    /// once is a record the platform holds rather than three columns on one request.
    /// </summary>
    /// <param name="client"> The client's details AS SUBMITTED. Taken as a parameter rather than read off the request, because the request no longer holds them: its name, suffix, email and mobile are read-throughs onto the very Person this method is about to write, so reading them here would be asking the answer to produce itself. </param>
    private async Task<Guid> ResolveClientPersonAsync(
        REMS rems, ClientDetails client, Guid tenantId, CancellationToken cancellationToken)
    {
        // Two names and a particle, on purpose.
        var corporate = Normalize(client.CorporateName);
        var isOrganisation = corporate is not null;
        var suffix = isOrganisation ? null : Normalize(client.Suffix);
        var email = Normalize(client.Email);
        var phone = Normalize(client.Phone);

        // The PARTS where the form sent them, the guessed split only where it did not.
        var (first, last) = isOrganisation
            ? (string.Empty, string.Empty)
            : Normalize(client.FirstName) is not null || Normalize(client.LastName) is not null
                ? (Normalize(client.FirstName) ?? string.Empty, Normalize(client.LastName) ?? string.Empty)
                : SplitName(client.Name?.Trim() ?? string.Empty);

        // What the record reads as.
        var name = isOrganisation
            ? corporate!
            : string.Join(" ", new[] { first, last }.Where(p => p.Length > 0));
        var displayName = suffix is null || name.Length == 0 ? name : $"{name} {suffix}";

        // Matched an existing client. A reference that no longer resolves (person deleted, or another
        // tenant's) falls through and is treated as a client we do not have.
        if (rems.ExistingClientReferenceId is { } referenceId
            && await _persons.GetByIdAsync(referenceId, cancellationToken) is { } matched)
        {
            var filled = false;
            if (email is not null && string.IsNullOrWhiteSpace(matched.PrimaryEmail))
            {
                matched.PrimaryEmail = email;
                filled = true;
            }
            if (phone is not null && string.IsNullOrWhiteSpace(matched.MobileNumber))
            {
                matched.MobileNumber = phone;
                filled = true;
            }
            // Only into a blank, like the two above: this request's particle is an answer about the
            // client, but a particle already on their record was put there deliberately and is theirs.
            if (suffix is not null && string.IsNullOrWhiteSpace(matched.Suffix))
            {
                matched.Suffix = suffix;
                filled = true;
            }
            if (filled)
            {
                matched.LastProfileUpdatedOn = DateTime.UtcNow;
                _persons.Update(matched);
            }
            return matched.Id;
        }

        // A person this request minted, still referred to by nobody else. Excludes one who has since become
        // a user — their profile is theirs from that point on, not a by-product of the request.
        if (rems.ClientPersonId is { } ownedId
            && await _persons.GetByIdAsync(ownedId, cancellationToken) is { UserId: null } owned
            && owned.SourceEntityType == EntityType.Client
            && owned.SourceEntityId == rems.Id
            && !await _rems.IsClientPersonSharedAsync(ownedId, rems.Id, cancellationToken))
        {
            owned.PartyType = isOrganisation ? PartyType.Organisation : PartyType.Individual;
            owned.CorporateName = corporate;
            owned.FirstName = first;
            owned.LastName = last;
            owned.Suffix = suffix;
            owned.DisplayName = displayName;
            owned.PrimaryEmail = email;
            owned.MobileNumber = phone;
            owned.LastProfileUpdatedOn = DateTime.UtcNow;
            _persons.Update(owned);
            return owned.Id;
        }

        var person = new Person
        {
            Id = Guid.NewGuid(),
            // Globally unique by construction (the filtered unique index on PersonCode), so no pre-check.
            PersonCode = "PER-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            // Set explicitly rather than left to ambient stamping: on create the request itself has no
            // tenant yet (it is stamped on save), so the caller's tenant is the only one that is known.
            TenantId = tenantId,
            // Client, not Rems: this person IS the client, and the picker offers only those.
            SourceEntityType = EntityType.Client,
            SourceEntityId = rems.Id,
            // Which shape this record is. It decides where the name lives, how the client lists read it
            // back, and whether the picker offers this record for an individual request or a corporate one.
            PartyType = isOrganisation ? PartyType.Organisation : PartyType.Individual,
            CorporateName = corporate,
            FirstName = first,
            LastName = last,
            Suffix = suffix,
            DisplayName = displayName,
            PrimaryEmail = email,
            MobileNumber = phone,
            IsActive = true,
            LastProfileUpdatedOn = DateTime.UtcNow,
        };
        await _persons.AddAsync(person, cancellationToken);
        return person.Id;
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
    /// The 409 for filing a brand-new client under an email another client already holds, or null to
    /// carry on.
    /// </summary>
    private async Task<IActionResult?> RejectDuplicateClientEmailAsync(
        Guid? existingClientReferenceId, string? email, Guid? excludingPersonId, CancellationToken cancellationToken)
    {
        var trimmed = Normalize(email);
        if (existingClientReferenceId is not null || trimmed is null)
        {
            return null;
        }

        if (await _persons.FindClientByEmailAsync(trimmed, excludingPersonId, cancellationToken) is not { } holder)
        {
            return null;
        }

        return StatusCode(StatusCodes.Status409Conflict, ApiResponseFactory.Error(
            CodeDuplicateEmail,
            "A client is already on file with that email address.",
            $"“{holder.ClientDisplayName}” is already on file with the email {trimmed}. Search for them in the "
                + "Client box and pick them, rather than filing a second record for the same client."));
    }

    // ResolveParentClientAsync stood alongside the reference check below — it validated the Parent Client id
    // against the request's type and returned the name to denormalise.

    /// <summary>The 400 for a client reference that does not name a client, or null to carry on.</summary>
    private async Task<IActionResult?> RejectUnknownClientReferenceAsync(
        Guid? existingClientReferenceId, CancellationToken cancellationToken)
    {
        if (existingClientReferenceId is not { } referenceId)
        {
            return null;
        }

        // Tenant-scoped and soft-delete-filtered by the ambient query filter, so another tenant's client
        // is unknown here in exactly the way a person who does not exist is.
        if (await _persons.GetByIdAsync(referenceId, cancellationToken) is { SourceEntityType: EntityType.Client })
        {
            return null;
        }

        return BadRequest(ApiResponseFactory.Error(
            ApiErrorCodes.ValidationFailed, "Validation failed.",
            "existingClientReferenceId must name a client on file. Search for the client in the Client "
                + "box and pick them from the results."));
    }

    /// <summary>A request may still be withdrawn while it is a draft.</summary>
    private static bool IsDeletable(REMS r) => r.Status!.Value == RemsRequestStatuses.Draft;

    /// <summary>Which row actions this caller may perform, combining the record-level rule with the permission.</summary>
    private RemsRowActions ActionsFor(REMS r, Guid me, bool privileged)
    {
        var canAct = CanAct(r, me, privileged);
        return new RemsRowActions(
            CanView: true,
            CanEdit: canAct && User.HasPermission(Permissions.RemsRequestsUpdate),
            // Not gated on CanAct, unlike everything around it: picking up is precisely the move made on
            // somebody ELSE's request, by an admin who has no standing on it yet.
            CanPickUp: User.HasPermission(Permissions.RemsRequestsAssign)
                && r.Status!.Value != RemsRequestStatuses.Draft
                && r.AdminAssignedToId is null,
            CanDelete: canAct && User.HasPermission(Permissions.RemsRequestsDelete) && IsDeletable(r));
    }

    private RemsRequestRow ToRow(
        REMS r, Guid me, bool privileged,
        IReadOnlyDictionary<Guid, string> names,
        IReadOnlyDictionary<Guid, RemsFormStateInfo> forms)
    {
        forms.TryGetValue(r.Id, out var form);
        var (ems, submission) = MapFormState(form);
        return new RemsRequestRow(
            r.Id, r.REMSNumber, r.ClientDisplayName, r.ClientNameSuffix,
            r.Type!.Value, r.CreatedOnUtc, r.Status!.Value,
            r.CustomerEmail, r.CustomerMobileNumber,
            UserRefOf(r.AdminAssignedToId, names), UserRefOf(r.CSEId, names),
            form?.EntityType, ems, submission,
            NameOf(names, r.CreatedById), NameOf(names, r.UpdatedById), r.UpdatedOnUtc,
            ActionsFor(r, me, privileged));
    }

    private async Task<RemsRequestDetail> BuildDetailAsync(REMS rems, Guid me, bool privileged, CancellationToken cancellationToken)
    {
        var names = await _users.GetFullNamesAsync(
            new[] { rems.AdminAssignedToId, rems.CSEId, rems.CreatedById, rems.UpdatedById }
                .Where(x => x.HasValue).Select(x => x!.Value),
            cancellationToken);
        var form = (await _rems.GetFormStatesAsync(new[] { rems.Id }, cancellationToken)).FirstOrDefault();
        var (ems, submission) = MapFormState(form);

        var files = rems.Files
            .Where(f => !f.Deleted)
            .Select(f => new RemsFileRef(f.Id, f.MediaId, f.Media?.OriginalFileName, f.Media?.MimeType, f.Media?.FileSize, f.Media?.PublicUrl))
            .ToList();

        // Asked only where a CSE is actually named: with none, the send-back dialog has one answer anyway
        // and there is nothing for a delegation lookup to decide.
        var canSendBackToCse = rems.CSEId is not null
            && await RemsSetupAccess.InitiatorHasCoverAsync(_delegations, rems, cancellationToken);

        return new RemsRequestDetail(
            rems.Id, rems.REMSNumber, rems.Description, rems.ClientDisplayName,
            rems.ClientNameSuffix,
            rems.ClientPerson?.IsOrganisation == true ? null : rems.ClientPerson?.FirstName,
            rems.ClientPerson?.IsOrganisation == true ? null : rems.ClientPerson?.LastName,
            rems.ClientPerson?.CorporateName,
            rems.Type!.Value, rems.Status!.Value, rems.CustomerEmail, rems.CustomerMobileNumber,
            rems.ExistingClientReferenceId, rems.ClientPersonId,
            UserRefOf(rems.AdminAssignedToId, names), UserRefOf(rems.CSEId, names),
            form?.EntityType, ems, submission, files,
            RecordAudit.From(rems, RecordAudit.Names(names)),
            ActionsFor(rems, me, privileged),
            canSendBackToCse,
            ClientFormLink(form));
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
    private async Task NotifyRequesterOfPickUpAsync(REMS rems, Guid adminUserId, Guid actorId, CancellationToken cancellationToken)
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
            $"{rems.REMSNumber} — {adminName} is now handling it.",
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
