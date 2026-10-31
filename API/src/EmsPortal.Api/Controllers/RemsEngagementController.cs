using EmsPortal.Api.Approval;
using EmsPortal.Api.Filters;
using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.Rems;
using EmsPortal.Api.Security;
using EmsPortal.Api.Validators.Rems;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.OptionSets;
using EmsPortal.Application.Common;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// REMS engagement workspace backend (WO-114 Part A + B): the submitted-form view, the editable
/// client/entity/engagement workspace, the audit/government/tax conditional details. The request is one
/// row now: the engagement is its setup columns and the entity is its client, so the id on an
/// <c>engagements/{id}</c> route is the REMS id and the id on an <c>entities/{entityId}</c> route is the
/// client id.
/// </summary>
[ApiController]
[LoadMaconomyClients]
[Route("api/rems")]
[Produces("application/json")]
[Tags("REMS Engagement Workspace")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class RemsEngagementController : ControllerBase
{
    private const string CodeEngagementLocked = "REMS_ENGAGEMENT_LOCKED";
    // STATIC-APPROVAL-POLICY
    private const string CodeCommissionReserved = "REMS_COMMISSION_RESERVED";

    private const string MarketingSetKey = "REMSMarketing_MarketingMethods.MarketingMethodId";
    private const string TaxFormSetKey = "REMS.TaxForm";
    private const string PrimaryContactCode = "primary_client_contact";

    private readonly IRemsRequestRepository _requests;
    /// <summary>Only to answer whether an initiator has cover arranged — see RemsSetupAccess.CanWork.</summary>
    private readonly IRemsDelegationRepository _delegations;
    private readonly IClientRepository _clients;
    private readonly IRemsSettingsRepository _settings;
    private readonly IAddressRepository _addresses;
    private readonly IPersonRepository _persons;
    private readonly IMediaRepository _media;
    private readonly IOptionSetRepository _optionSets;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityEventWriter _activity;
    private readonly IOptionCodeResolver _codes;
    // STATIC-APPROVAL-POLICY
    private readonly IRemsApprovalPolicy _policy;
    private readonly IMaconomyCustomerSyncService _customerSync;
    private readonly IMaconomyJobSyncService _jobSync;
    private readonly IMaconomyClientDirectory _directory;
    private readonly RemsClientIntake _intake;

    public RemsEngagementController(
        IRemsRequestRepository requests,
        IRemsDelegationRepository delegations,
        IClientRepository clients,
        IRemsSettingsRepository settings,
        IAddressRepository addresses,
        IPersonRepository persons,
        IMediaRepository media,
        IOptionSetRepository optionSets,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IActivityEventWriter activity,
        IOptionCodeResolver codes,
        IRemsApprovalPolicy policy,
        IMaconomyCustomerSyncService customerSync,
        IMaconomyJobSyncService jobSync,
        IMaconomyClientDirectory directory,
        RemsClientIntake intake)
    {
        _policy = policy;
        _customerSync = customerSync;
        _jobSync = jobSync;
        _directory = directory;
        _intake = intake;
        _requests = requests;
        _delegations = delegations;
        _clients = clients;
        _settings = settings;
        _addresses = addresses;
        _persons = persons;
        _media = media;
        _optionSets = optionSets;
        _users = users;
        _unitOfWork = unitOfWork;
        _activity = activity;
        _codes = codes;
    }

    // -------------------- Part A: submitted-form view + workspace read --------------------

    /// <summary>
    /// EMS Review (AC-REMS-013.1): every submitted request that has an EMS form, indicating
    /// submitted/not-submitted, client name.
    /// </summary>
    [HttpGet("client-forms")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsClientFormRow>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClientForms(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? submitted = null,
        [FromQuery] string? requestStatus = null,
        [FromQuery] string? assignment = null,
        [FromQuery] Guid? assignedAdminUserId = null,
        [FromQuery] Guid? cseUserId = null,
        // The Client column's dropdown — any of the chosen clients.
        [FromQuery] Guid[]? clientPersonIds = null,
        [FromQuery] DateTime? submittedFrom = null,
        [FromQuery] DateTime? submittedTo = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        [FromQuery] DateTime? updatedFrom = null,
        [FromQuery] DateTime? updatedTo = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var slice = string.Equals(assignment?.Trim(), "mine", StringComparison.OrdinalIgnoreCase)
            ? RemsClientFormAssignment.Mine
            : RemsClientFormAssignment.All;

        var (items, total) = await _requests.ListClientFormsAsync(
            new RemsClientFormQuery(
                search, submitted, requestStatus, me, slice, new SortRequest(sortBy, descending), page, limit,
                assignedAdminUserId, cseUserId, clientPersonIds, submittedFrom, submittedTo,
                createdFrom, createdTo, updatedFrom, updatedTo),
            cancellationToken);
        var names = await _users.GetFullNamesAsync(
            items.SelectMany(i => new[] { i.AdminAssignedToId, i.CSEId, i.CreatedById, i.UpdatedById })
                .Where(id => id.HasValue).Select(id => id!.Value),
            cancellationToken);

        string? NameOf(Guid? id) => id is { } uid && names.TryGetValue(uid, out var n) ? n : null;

        // Asked once for the whole page: the permission is the caller's, so only the row's own claimed/
        // unclaimed state varies. Drafts cannot reach this list, so being unclaimed is the whole test.
        var mayAssign = User.HasPermission(Permissions.RemsRequestsAssign);
        // Whether this caller can prise a request loose from whoever holds it — the remedy when the admin
        // on it is away. Ordinary admins give back only their own.
        var elevated = RemsSetupAccess.IsElevated(User);
        var syncStatuses = await SyncStatusesAsync(items, cancellationToken);

        var rows = items.Select(i => new RemsClientFormRow(
            i.RemsId, i.RemsNumber, i.ClientName, i.ClientNameSuffix,
            i.RequestStatus,
            HasForm: true, i.Submitted, i.SubmittedOnUtc,
            RemsWorkspaceMapper.UserRef(i.AdminAssignedToId, names), RemsWorkspaceMapper.UserRef(i.CSEId, names),
            CanPickUp: mayAssign && i.AdminAssignedToId is null,
            CanHandBack: mayAssign && i.AdminAssignedToId is { } holder && (elevated || holder == me),
            NameOf(i.CreatedById), i.CreatedOnUtc, NameOf(i.UpdatedById), i.UpdatedOnUtc,
            MaconomySyncStatus: syncStatuses.TryGetValue(i.RemsId, out var sync) ? sync.Customer : null,
            MaconomyJobSync: sync.Job));

        return Ok(ApiResponseFactory.Paginated(rows, "REMS client forms retrieved.", page, limit, total));
    }

    /// <summary>The clients across the queue, for its Client filter — whoever the list could narrow to.</summary>
    [HttpGet("client-forms/clients")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsClientChoice>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClientFormClients(CancellationToken cancellationToken)
    {
        var clients = await _requests.ListClientFormClientsAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(clients, "EMS Review clients retrieved."));
    }

    /// <summary>The quick-filter counts for the EMS Review list, under the same filters.</summary>
    [HttpGet("client-forms/quick-counts")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<RemsClientFormQuickCounts>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClientFormQuickCounts(
        [FromQuery] string? search = null,
        [FromQuery] bool? submitted = null,
        [FromQuery] string? requestStatus = null,
        [FromQuery] string? assignment = null,
        [FromQuery] Guid? assignedAdminUserId = null,
        [FromQuery] Guid? cseUserId = null,
        // The Client column's dropdown — any of the chosen clients.
        [FromQuery] Guid[]? clientPersonIds = null,
        [FromQuery] DateTime? submittedFrom = null,
        [FromQuery] DateTime? submittedTo = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        [FromQuery] DateTime? updatedFrom = null,
        [FromQuery] DateTime? updatedTo = null,
        CancellationToken cancellationToken = default)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var slice = string.Equals(assignment?.Trim(), "mine", StringComparison.OrdinalIgnoreCase)
            ? RemsClientFormAssignment.Mine
            : RemsClientFormAssignment.All;
        var counts = await _requests.CountClientFormQuickFiltersAsync(
            new RemsClientFormQuery(
                search, submitted, requestStatus, me, slice, SortRequest.Default, Page: 1, Limit: 1,
                assignedAdminUserId, cseUserId, clientPersonIds, submittedFrom, submittedTo,
                createdFrom, createdTo, updatedFrom, updatedTo),
            cancellationToken);
        return Ok(ApiResponseFactory.Success(counts, "EMS Review quick-filter counts retrieved."));
    }

    /// <summary>
    /// The submitted-form view (AC-REMS-013.2/3): the client's answers as plain fields, rebuilt from the
    /// client's rows — distinct from the editable workspace data.
    /// </summary>
    [HttpGet("requests/{remsId:guid}/submission")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsRead)]
    [ProducesResponseType<ApiResponse<RemsSubmissionView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submission(Guid remsId, CancellationToken cancellationToken)
    {
        var rems = await _requests.GetWorkspaceAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        if (GuardCanRead(rems) is { } notAllowed)
        {
            return notAllowed;
        }

        if (rems.FormSubmittedOnUtc is null || rems.Client is null)
        {
            return NotFound(ApiResponseFactory.NotFound("This request has no submitted form."));
        }

        await HydrateAsync(rems, cancellationToken);
        return Ok(ApiResponseFactory.Success(
            await BuildSubmissionViewAsync(rems, cancellationToken),
            "REMS submitted form retrieved."));
    }

    /// <summary>Correct the client's submitted answers, in place (Admin only).</summary>
    [HttpPut("requests/{remsId:guid}/submission")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<RemsSubmissionView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSubmission(
        Guid remsId, [FromBody] RemsFormPayloadV1? payload, CancellationToken cancellationToken)
    {
        var rems = await _requests.GetWorkspaceAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        if (GuardCanRead(rems) is { } notAllowed)
        {
            return notAllowed;
        }

        // Frozen for the same reason every other field on the request is: the approvers are deciding on
        // what is in front of them, and a snapshot that changed under them is a different decision.
        if (IsFrozenForApproval(rems.Status!.Value))
        {
            return StatusCode(StatusCodes.Status409Conflict, ApiResponseFactory.Error(
                CodeEngagementLocked,
                "This request is locked.",
                "The client's submitted form cannot be corrected while the engagement is pending approval or approved."));
        }

        if (rems.FormSubmittedOnUtc is null || rems.Client is null)
        {
            return NotFound(ApiResponseFactory.NotFound("This request has no submitted form."));
        }

        if (payload is null)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", "payload: No form data was supplied."));
        }

        await HydrateAsync(rems, cancellationToken);
        var entityType = rems.Client.EntityType?.Value ?? string.Empty;

        // Not the Admin's to change: the email is the address the invite went to, and the version pins the
        // shape the payload is read in.
        payload.Email = rems.Client.ClientEmail;
        payload.Version = 1;

        var validation = new RemsFormPayloadValidator().Validate(payload, entityType);
        if (!validation.IsValid)
        {
            return BadRequest(ApiResponseFactory.ValidationError(validation.Errors));
        }

        // The rows are the record: the correction is written straight onto the client.
        await _intake.ApplyAsync(rems, payload, entityType, cancellationToken);
        await _activity.WriteAsync(
            new CreateActivityEventDto(EntityType.Rems, rems.Id, ActivityEventTypes.RemsFormCorrected),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _requests.GetWorkspaceAsync(remsId, cancellationToken) ?? rems;
        await HydrateAsync(refreshed, cancellationToken);
        return Ok(ApiResponseFactory.Success(
            await BuildSubmissionViewAsync(refreshed, cancellationToken),
            "REMS submitted form updated."));
    }

    /// <summary>
    /// The submitted-form view, with the correction trail resolved and this caller's own right to
    /// correct it. A correction is a staff write to the client row after the client's own submission.
    /// </summary>
    private async Task<RemsSubmissionView> BuildSubmissionViewAsync(RemsRequest rems, CancellationToken cancellationToken)
    {
        var client = rems.Client!;
        var entityType = client.EntityType?.Value ?? string.Empty;
        var payload = RemsClientIntake.ToPayload(rems, entityType, client.ClientEmail ?? string.Empty);
        var submittedOn = rems.FormSubmittedOnUtc!.Value;

        string? editedBy = null;
        DateTime? editedOn = null;
        if (client.UpdatedOnUtc > submittedOn && client.UpdatedById is { } editorId)
        {
            var names = await _users.GetFullNamesAsync(new[] { editorId }, cancellationToken);
            editedBy = names.TryGetValue(editorId, out var name) ? name : "an admin";
            editedOn = client.UpdatedOnUtc;
        }

        return new RemsSubmissionView(
            rems.Id, rems.Id, rems.RemsNumber, entityType, client.ClientEmail,
            client.Person?.Suffix,
            submittedOn, payload,
            editedBy,
            editedOn,
            CanEdit: User.HasPermission(Permissions.RemsEngagementsManage) && !IsFrozenForApproval(rems.Status!.Value));
    }

    /// <summary>Once a round is open — and once it has succeeded — nothing about the request may move.</summary>
    private static bool IsFrozenForApproval(string? status)
        => status is RemsRequestStatuses.PendingApproval or RemsRequestStatuses.Approved;

    /// <summary>
    /// The engagement workspace (AC-REMS-014): the request's engagement with its audit/government/tax
    /// detail.
    /// </summary>
    [HttpGet("requests/{remsId:guid}/engagement")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsRead)]
    [ProducesResponseType<ApiResponse<RemsEngagementWorkspace>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Workspace(Guid remsId, CancellationToken cancellationToken)
    {
        var rems = await _requests.GetWorkspaceAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        if (GuardCanRead(rems) is { } notAllowed)
        {
            return notAllowed;
        }

        await HydrateAsync(rems, cancellationToken);

        // The department → director map travels with the workspace so the setup form can name the director
        // a department maps to as soon as it is picked, instead of waiting for the save to come back.
        var settings = await _settings.GetAsync(cancellationToken);
        var directorRows = settings?.DepartmentDirectors.Where(d => !d.Deleted).ToList() ?? new();

        var names = await _users.GetFullNamesAsync(
            CollectUserIds(rems).Concat(directorRows.Select(d => d.DirectorUserId)), cancellationToken);

        var departmentDirectors = directorRows
            .OrderBy(d => d.Department!.Value)
            .Select(d => new RemsDepartmentDirectorView(
                d.Department!.Value,
                new RemsUserRef(d.DirectorUserId, names.TryGetValue(d.DirectorUserId, out var n) ? n : string.Empty)))
            .ToList();

        var additionalEntities = await AdditionalEntitiesAsync(rems, cancellationToken);

        var workspace = RemsWorkspaceMapper.Workspace(rems, names, additionalEntities, departmentDirectors);
        return Ok(ApiResponseFactory.Success(workspace, "REMS engagement workspace retrieved."));
    }

    /// <summary>
    /// The other businesses this client named, with the request each has produced. Resolved to REMS
    /// numbers in one lookup so a row can link to what it created rather than just claiming it exists.
    /// </summary>
    private async Task<IReadOnlyList<RemsAdditionalEntityView>> AdditionalEntitiesAsync(RemsRequest rems, CancellationToken cancellationToken)
    {
        if (rems.Client is not { } client)
        {
            return Array.Empty<RemsAdditionalEntityView>();
        }

        var related = (await _requests.ListRelatedClientsAsync(new[] { rems.Id }, cancellationToken))
            .Where(r => r.CreatedRemsId.HasValue)
            .ToList();
        var createdNumbers = await _requests.GetNumbersAsync(
            related.Select(r => r.CreatedRemsId!.Value).ToList(), cancellationToken);

        // The rows are keyed by relationship; the mapper wants them by the related client.
        var relatedClientIds = client.Relationships.Where(r => !r.Deleted).ToDictionary(r => r.Id, r => r.RelatedClientId);
        var raisedFor = new Dictionary<Guid, (Guid RemsId, string RemsNumber)>();
        foreach (var row in related)
        {
            if (relatedClientIds.TryGetValue(row.Id, out var relatedClientId))
            {
                var createdId = row.CreatedRemsId!.Value;
                raisedFor[relatedClientId] = (createdId, createdNumbers.TryGetValue(createdId, out var number) ? number : string.Empty);
            }
        }
        return RemsWorkspaceMapper.AdditionalEntities(client, raisedFor);
    }

    // -------------------- Part A: client + entity editing --------------------

    /// <summary>Update the client record (AC-REMS-014).</summary>
    [HttpPut("requests/{remsId:guid}/client")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<RemsClientView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateClient(Guid remsId, [FromBody] UpdateRemsClientRequest request, CancellationToken cancellationToken)
    {
        var rems = await _requests.GetWorkspaceAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        if (await GuardSetupOwnerAsync(rems, cancellationToken) is { } denied)
        {
            return denied;
        }

        var client = rems.Client;
        if (client is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS client not found."));
        }

        if (Normalize(request.Name) is { } name)
        {
            if (client.IsOrganisation)
            {
                client.LegalName = name;
            }
            else if (client.Person is { } person)
            {
                var (first, last) = SplitName(name);
                person.FirstName = first;
                person.LastName = last;
                person.DisplayName = name;
                person.LastProfileUpdatedOn = DateTime.UtcNow;
                _persons.Update(person);
            }
        }
        if (request.MobileNumber is not null) client.PrimaryPhone = Normalize(request.MobileNumber);
        // The wire carries the CODE; the column is a foreign key to the option item it names. An unknown
        // code resolves to null rather than being stored as-is -- there is nothing to point at.
        if (request.ReferralSource is not null)
        {
            client.ReferralSourceId = await _codes.IdOfAsync(
                EntityType.Rems, RemsOptionSetKeys.ReferralSource, request.ReferralSource, cancellationToken);
        }

        // The billing name and email are the first billing contact's person; supplied blank, the contact goes.
        if (request.BillingContactName is not null || request.BillingEmail is not null)
        {
            await UpsertBillingContactAsync(rems, client, request.BillingContactName, request.BillingEmail, cancellationToken);
        }

        // Billing ADDRESSES are not edited here. There may be several of them, and they are written by the
        // client's own intake form.
        _clients.Update(client);
        await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, remsId, ActivityEventTypes.RemsEngagementUpdated), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _clients.GetByIdAsync(client.Id, cancellationToken) ?? client;
        await _directory.HydrateAsync(refreshed, cancellationToken);
        return Ok(ApiResponseFactory.Success(RemsWorkspaceMapper.ClientView(refreshed), "REMS client updated."));
    }

    private async Task UpsertBillingContactAsync(
        RemsRequest rems, Client client, string? nameInput, string? emailInput, CancellationToken cancellationToken)
    {
        var billing = RemsWorkspaceMapper.BillingContact(client);
        var name = nameInput is null ? Normalize(billing?.Person?.DisplayName) : Normalize(nameInput);
        var email = emailInput is null ? Normalize(billing?.Person?.PrimaryEmail) : Normalize(emailInput);

        if (name is null && email is null)
        {
            if (billing is not null)
            {
                client.Contacts.Remove(billing);
                _clients.RemoveContact(billing);
            }
            return;
        }

        var input = new RemsEntityContactInput { Name = name, Email = email };
        if (billing?.Person is { } person)
        {
            ApplyContactPerson(person, input);
            _persons.Update(person);
            return;
        }

        var created = NewContactPerson(input, client.TenantId, rems.Id);
        await _persons.AddAsync(created, cancellationToken);
        var contact = new ClientContact
        {
            Id = Guid.NewGuid(),
            TenantId = client.TenantId,
            ClientId = client.Id,
            PersonId = created.Id,
            ContactRoleId = await _codes.RequireRemsIdAsync(RemsOptionSetKeys.ContactRole, RemsContactRoles.BillingContactCode, cancellationToken),
            Person = created,
        };
        client.Contacts.Add(contact);
        await _clients.AddContactAsync(contact, cancellationToken);
    }

    /// <summary>Replace an entity's physical/mailing addresses (AC-REMS-014). The entity is the client.</summary>
    [HttpPut("entities/{entityId:guid}/addresses")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsEntityAddressView>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEntityAddresses(
        Guid entityId, [FromBody] UpdateRemsEntityAddressesRequest request, CancellationToken cancellationToken)
    {
        var client = await _clients.GetByIdAsync(entityId, cancellationToken);
        if (client is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS entity not found."));
        }

        var rems = await RequestOfClientAsync(client.Id, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }
        if (await GuardSetupOwnerAsync(rems, cancellationToken) is { } denied)
        {
            return denied;
        }

        await UpsertClientAddressAsync(client, RemsAddressTypes.Physical, request.PhysicalAddress, cancellationToken);
        await UpsertClientAddressAsync(client, RemsAddressTypes.Mailing, request.MailingAddress, cancellationToken);

        await _activity.WriteAsync(new CreateActivityEventDto(
            EntityType.Rems, rems.Id, ActivityEventTypes.RemsEngagementUpdated), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _clients.GetByIdAsync(entityId, cancellationToken) ?? client;
        var rows = refreshed.Addresses.Where(a => !a.Deleted && a.Address is not null)
            .OrderBy(a => a.AddressType?.SortOrder).ThenBy(a => a.CreatedOnUtc)
            .Select(RemsWorkspaceMapper.EntityAddress);
        return Ok(ApiResponseFactory.Success(rows, "REMS entity addresses updated."));
    }

    /// <summary>Replace an entity's contacts (AC-REMS-014). The entity is the client.</summary>
    [HttpPut("entities/{entityId:guid}/contacts")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsEntityContactView>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEntityContacts(
        Guid entityId, [FromBody] UpdateRemsEntityContactsRequest request, CancellationToken cancellationToken)
    {
        var client = await _clients.GetByIdAsync(entityId, cancellationToken);
        if (client is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS entity not found."));
        }

        var rems = await RequestOfClientAsync(client.Id, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }
        if (await GuardSetupOwnerAsync(rems, cancellationToken) is { } denied)
        {
            return denied;
        }

        // The wire still names roles by their old enum names; each must be a value on the REMS.ContactRole
        // list before anything is written.
        var desired = new List<(string Code, Guid RoleId, RemsEntityContactInput Input)>();
        foreach (var input in request.Contacts)
        {
            if (RemsContactRoles.CodeOf(input.Role) is not { } code)
            {
                return BadRequest(ApiResponseFactory.Error(
                    ApiErrorCodes.ValidationFailed, "Validation failed.", $"contacts: '{input.Role}' is not a REMS contact role."));
            }
            desired.Add((code, await _codes.RequireRemsIdAsync(RemsOptionSetKeys.ContactRole, code, cancellationToken), input));
        }

        var existing = client.Contacts.Where(c => !c.Deleted).ToList();
        var desiredRoleIds = desired.Select(d => d.RoleId).ToHashSet();

        // Remove contacts whose role is no longer present.
        foreach (var contact in existing.Where(c => !desiredRoleIds.Contains(c.ContactRoleId)).ToList())
        {
            client.Contacts.Remove(contact);
            _clients.RemoveContact(contact);
        }

        // Upsert each supplied contact by role. IsRequired is a rule of the entity type now, not stored.
        foreach (var (code, roleId, input) in desired)
        {
            var contact = existing.FirstOrDefault(c => c.ContactRoleId == roleId);
            if (contact is null)
            {
                var person = NewContactPerson(input, client.TenantId, rems.Id);
                await _persons.AddAsync(person, cancellationToken);
                contact = new ClientContact
                {
                    Id = Guid.NewGuid(),
                    TenantId = client.TenantId,
                    ClientId = client.Id,
                    PersonId = person.Id,
                    ContactRoleId = roleId,
                    IsPrimary = code == PrimaryContactCode,
                    Person = person,
                };
                client.Contacts.Add(contact);
                await _clients.AddContactAsync(contact, cancellationToken);
                existing.Add(contact);
            }
            else if (contact.Person is { } person)
            {
                ApplyContactPerson(person, input);
                _persons.Update(person);
            }
        }

        await _activity.WriteAsync(new CreateActivityEventDto(
            EntityType.Rems, rems.Id, ActivityEventTypes.RemsEngagementUpdated), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _clients.GetByIdAsync(entityId, cancellationToken) ?? client;
        var entityType = refreshed.EntityType?.Value;
        var rows = refreshed.Contacts.Where(c => !c.Deleted)
            .OrderBy(c => c.ContactRole?.SortOrder).ThenBy(c => c.CreatedOnUtc)
            .Select(c => RemsWorkspaceMapper.EntityContact(c, entityType));
        return Ok(ApiResponseFactory.Success(rows, "REMS entity contacts updated."));
    }

    // -------------------- Part A: engagement editing --------------------

    /// <summary>Update an engagement's team, service placement and fee/realization (AC-REMS-014).</summary>
    [HttpPut("engagements/{id:guid}")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsEngagementUpdateResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEngagement(Guid id, [FromBody] UpdateRemsEngagementRequest request, CancellationToken cancellationToken)
    {
        var (rems, refusal) = await OpenForEditAsync(id, cancellationToken);
        if (rems is null)
        {
            return refusal!;
        }

        // Any supplied team member must resolve to a real user.
        foreach (var userId in new[] { request.DepartmentDirectorId, request.EngagementExecutiveId, request.BillingManagerId })
        {
            if (userId is { } uid && await _users.GetByIdAsync(uid, cancellationToken) is null)
            {
                return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", $"Unknown user id {uid}."));
            }
        }

        // Each of the three is an option-set item, referenced by id.
        var incomingDepartment = Normalize(request.Department);
        var departmentChanged = request.Department is not null
            && !string.Equals(incomingDepartment, rems.Department?.Value, StringComparison.Ordinal);
        if (request.Department is not null)
        {
            rems.DepartmentId =
                await _codes.RemsIdAsync(RemsOptionSetKeys.Department, incomingDepartment, cancellationToken);
        }

        // The three sub-classifications. Nothing branches on any of them — they narrow the line and the industry
        // group for reporting.
        if (request.ServiceLine is not null)
        {
            rems.ServiceLineId = await _codes.RemsIdAsync(
                RemsOptionSetKeys.ServiceLine, Normalize(request.ServiceLine), cancellationToken);
        }
        if (request.JobTemplate is not null)
        {
            rems.JobTemplateId = await _codes.RemsIdAsync(
                RemsOptionSetKeys.JobTemplate, Normalize(request.JobTemplate), cancellationToken);
        }
        // The industry is the client's trade, so it lives on the client.
        if (request.Industry is not null && rems.Client is { } client)
        {
            client.IndustryId = await _codes.RemsIdAsync(
                RemsOptionSetKeys.Industry, Normalize(request.Industry), cancellationToken);
            _clients.Update(client);
        }

        // The department the director is mapped from is the one being SAVED, which on this request may be
        // the incoming code rather than what the row still holds.
        var mappedDirector = await MappedDirectorAsync(
            request.Department is not null ? incomingDepartment : rems.Department?.Value, cancellationToken);
        if (request.DepartmentDirectorId.HasValue)
        {
            rems.DepartmentDirectorId = request.DepartmentDirectorId;
        }
        else if (departmentChanged || rems.DepartmentDirectorId is null)
        {
            // Prefill from the tenant department-director map (may be null = unassigned placeholder).
            rems.DepartmentDirectorId = mappedDirector;
        }

        if (request.EngagementExecutiveId.HasValue) rems.EngagementExecutiveId = request.EngagementExecutiveId;
        if (request.BillingManagerId.HasValue) rems.BillingManagerId = request.BillingManagerId;
        if (request.FirstYearFeeEstimate.HasValue) rems.FirstYearFeeEstimate = request.FirstYearFeeEstimate;
        // Assurance's own fee, kept apart from the first-year estimate above rather than sharing a column with
        // it: they are different questions.
        if (request.EngagementFee.HasValue) rems.EngagementFee = request.EngagementFee;
        if (request.RealizationPercentage.HasValue) rems.RealizationPercentage = request.RealizationPercentage;
        // The billing schedule: how often, and how it actually works.
        if (request.BillingPeriod is not null)
        {
            rems.BillingPeriodId = await _codes.RemsIdAsync(
                RemsOptionSetKeys.BillingPeriod, Normalize(request.BillingPeriod), cancellationToken);
        }
        if (request.BillingProcessDescription is not null)
        {
            rems.BillingProcessDescription = Normalize(request.BillingProcessDescription);
        }

        _requests.Update(rems);
        await LogEngagementUpdatedAsync(rems.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await BuildEngagementViewAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(new RemsEngagementUpdateResult(view!, mappedDirector), "REMS engagement updated."));
    }

    /// <summary>
    /// Link a previously-uploaded media id as the audit engagement's signed client-acceptance form
    /// (AC-REMS-014.12).
    /// </summary>
    [HttpPost("engagements/{id:guid}/audit/client-acceptance-form")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsEngagementView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LinkClientAcceptanceForm(Guid id, [FromBody] LinkClientAcceptanceFormRequest request, CancellationToken cancellationToken)
    {
        var (rems, refusal) = await OpenForEditAsync(id, cancellationToken);
        if (rems is null)
        {
            return refusal!;
        }
        if (await _media.GetByIdAsync(request.MediaId, cancellationToken) is null)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", "Unknown mediaId."));
        }

        await SetFileAsync(rems, RemsFileKind.ClientAcceptanceForm, request.MediaId, cancellationToken);
        await LogEngagementUpdatedAsync(rems.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await BuildEngagementViewAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(view!, "Client acceptance form linked."));
    }

    /// <summary>Take the signed client-acceptance form off the engagement.</summary>
    [HttpDelete("engagements/{id:guid}/audit/client-acceptance-form")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsEngagementView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnlinkClientAcceptanceForm(Guid id, CancellationToken cancellationToken)
    {
        var (rems, refusal) = await OpenForEditAsync(id, cancellationToken);
        if (rems is null)
        {
            return refusal!;
        }

        // Idempotent: an engagement with no form on file is already in the state the caller asked for, and
        // answering 404 to "there is nothing there" would make a double-click read as an error.
        if (RemoveFiles(rems, RemsFileKind.ClientAcceptanceForm))
        {
            await LogEngagementUpdatedAsync(rems.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var view = await BuildEngagementViewAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(view!, "Client acceptance form removed."));
    }

    /// <summary>
    /// Set the government-audit contract detail: contract number + Florida 1% flag and contract/PO
    /// dates (AC-REMS-014.13).
    /// </summary>
    [HttpPut("engagements/{id:guid}/government")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsEngagementView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateGovernmentDetail(Guid id, [FromBody] UpdateRemsGovernmentDetailRequest request, CancellationToken cancellationToken)
    {
        var (rems, refusal) = await OpenForEditAsync(id, cancellationToken);
        if (rems is null)
        {
            return refusal!;
        }

        // The rate card's levels are codes off the REMS.PersonnelLevel list, resolved before anything is
        // written: a code the list does not carry is refused, not dropped on the floor.
        var desiredRates = new Dictionary<Guid, decimal>();
        foreach (var rate in request.PersonnelRates)
        {
            // A level without a rate is a level the engagement is not staffed at.
            if (rate.BillRatePerHour is not { } billRate)
            {
                continue;
            }
            var levelId = await _codes.RemsIdAsync(RemsOptionSetKeys.PersonnelLevel, Normalize(rate.PersonnelLevel), cancellationToken);
            if (levelId is null)
            {
                return BadRequest(ApiResponseFactory.Error(
                    ApiErrorCodes.ValidationFailed, "Validation failed.",
                    $"personnelRates: '{rate.PersonnelLevel}' is not a value in the REMS Personnel Level list."));
            }
            desiredRates[levelId.Value] = billRate;
        }

        // The firm's side of the contract sits on the request; blanks included.
        rems.ContractNumber = Normalize(request.ContractNumber);
        rems.IsFloridaOnePercentFeeApplicable = request.FloridaOnePercentStateFeeApplies;
        rems.PurchaseOrderNumber = Normalize(request.PurchaseOrderNumber);
        rems.PurchaseOrderAmount = request.PurchaseOrderAmount;

        await WriteContractAsync(rems, request, cancellationToken);

        // Reconcile the rate card by level: a level the request no longer prices comes off, one it newly
        // prices goes on, and the rest just take the rate they were sent.
        var existingRates = rems.PersonnelRates.Where(r => !r.Deleted).ToList();
        foreach (var rate in existingRates.Where(r => !desiredRates.ContainsKey(r.PersonnelLevelId)))
        {
            rems.PersonnelRates.Remove(rate);
            _requests.RemovePersonnelRate(rate);
        }
        foreach (var (levelId, billRate) in desiredRates)
        {
            var rate = existingRates.FirstOrDefault(r => r.PersonnelLevelId == levelId);
            if (rate is null)
            {
                var created = new RemsPersonnelRate
                {
                    Id = Guid.NewGuid(),
                    TenantId = rems.TenantId,
                    REMSId = rems.Id,
                    PersonnelLevelId = levelId,
                    BillRatePerHour = billRate,
                };
                rems.PersonnelRates.Add(created);
                await _requests.AddPersonnelRateAsync(created, cancellationToken);
            }
            else
            {
                rate.BillRatePerHour = billRate;
            }
        }

        _requests.Update(rems);
        await LogEngagementUpdatedAsync(rems.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await BuildEngagementViewAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(view!, "Government audit detail updated."));
    }

    /// <summary>
    /// The contract dates and terms are what the client declared, on the contract the request runs under:
    /// the one it points at, else the client's current one, else a new current one when there is anything
    /// to put on it.
    /// </summary>
    private async Task WriteContractAsync(RemsRequest rems, UpdateRemsGovernmentDetailRequest request, CancellationToken cancellationToken)
    {
        var client = rems.Client!;
        var contract = rems.Contract ?? client.Contracts.FirstOrDefault(c => !c.Deleted && c.IsCurrent);
        if (contract is null)
        {
            var any = request.ContractStartDate is not null || request.ContractEndDate is not null
                || Normalize(request.OriginalTerm) is not null || Normalize(request.RenewalTerms) is not null
                || request.PurchaseOrderStartDate is not null || request.PurchaseOrderEndDate is not null;
            if (!any)
            {
                return;
            }
            contract = new ClientContract { Id = Guid.NewGuid(), TenantId = rems.TenantId, ClientId = client.Id, IsCurrent = true };
            client.Contracts.Add(contract);
            await _clients.AddContractAsync(contract, cancellationToken);
        }
        if (rems.ContractId != contract.Id)
        {
            rems.ContractId = contract.Id;
            rems.Contract = contract;
        }

        contract.ContractStartDate = request.ContractStartDate;
        contract.ContractEndDate = request.ContractEndDate;
        contract.OriginalTerm = Normalize(request.OriginalTerm);
        contract.RenewalTerms = Normalize(request.RenewalTerms);
        contract.PurchaseOrderStartDate = request.PurchaseOrderStartDate;
        contract.PurchaseOrderEndDate = request.PurchaseOrderEndDate;
        _clients.UpdateContract(contract);
    }

    /// <summary>Set the ASSURANCE detail: the client's fiscal year end.</summary>
    [HttpPut("engagements/{id:guid}/audit")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsEngagementView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAuditDetail(Guid id, [FromBody] UpdateRemsAuditDetailRequest request, CancellationToken cancellationToken)
    {
        var (rems, refusal) = await OpenForEditAsync(id, cancellationToken);
        if (rems is null)
        {
            return refusal!;
        }

        // The fiscal year end is the client's, whichever department asks for it.
        var client = rems.Client!;
        client.FiscalYearEnd = request.ClientFiscalYearEnd;
        _clients.Update(client);

        await LogEngagementUpdatedAsync(rems.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await BuildEngagementViewAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(view!, "Assurance engagement detail updated."));
    }

    /// <summary>Link a previously-uploaded media id as the GCS engagement's purchase-order document.</summary>
    [HttpPost("engagements/{id:guid}/government/purchase-order")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsEngagementView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LinkPurchaseOrder(Guid id, [FromBody] LinkPurchaseOrderRequest request, CancellationToken cancellationToken)
    {
        var (rems, refusal) = await OpenForEditAsync(id, cancellationToken);
        if (rems is null)
        {
            return refusal!;
        }
        if (await _media.GetByIdAsync(request.MediaId, cancellationToken) is null)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", "Unknown mediaId."));
        }

        await SetFileAsync(rems, RemsFileKind.PurchaseOrder, request.MediaId, cancellationToken);
        await LogEngagementUpdatedAsync(rems.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await BuildEngagementViewAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(view!, "Purchase order linked."));
    }

    /// <summary>Take the purchase-order document off the GCS engagement.</summary>
    [HttpDelete("engagements/{id:guid}/government/purchase-order")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsEngagementView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnlinkPurchaseOrder(Guid id, CancellationToken cancellationToken)
    {
        var (rems, refusal) = await OpenForEditAsync(id, cancellationToken);
        if (rems is null)
        {
            return refusal!;
        }

        // Idempotent, exactly as the client-acceptance form's removal is: an engagement carrying no order is
        // already in the state the caller asked.
        if (RemoveFiles(rems, RemsFileKind.PurchaseOrder))
        {
            await LogEngagementUpdatedAsync(rems.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var view = await BuildEngagementViewAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(view!, "Purchase order removed."));
    }

    /// <summary>
    /// Set the tax engagement detail (AC-REMS-014.14): the fiscal year end (which recomputes the
    /// due-date schedule) and the tax-form checklist.
    /// </summary>
    [HttpPut("engagements/{id:guid}/tax")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsEngagementView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTaxDetail(Guid id, [FromBody] UpdateRemsTaxDetailRequest request, CancellationToken cancellationToken)
    {
        var (rems, refusal) = await OpenForEditAsync(id, cancellationToken);
        if (rems is null)
        {
            return refusal!;
        }

        // Validate the tax-form ids against the REMS.TaxForm option set.
        if (request.TaxFormIds.Count > 0)
        {
            var valid = await ResolveOptionItemIdsAsync(TaxFormSetKey, cancellationToken);
            var unknown = request.TaxFormIds.Where(f => !valid.Contains(f)).ToList();
            if (unknown.Count > 0)
            {
                return BadRequest(ApiResponseFactory.Error(
                    ApiErrorCodes.ValidationFailed, "Validation failed.", "One or more taxFormIds are not valid REMS tax forms."));
            }
        }

        var client = rems.Client!;
        client.FiscalYearEnd = request.FiscalYearEnd;
        _clients.Update(client);

        // Every date is a row: the rule fills in what was left blank and steps aside for what was typed,
        // and a typed date is remembered as an override by whoever typed it.
        await WriteTaxDueDatesAsync(rems, request, User.GetUserId(), cancellationToken);

        // Reconcile the tax-form checklist rows by TaxFormId.
        var existingForms = rems.TaxForms.Where(f => !f.Deleted).ToList();
        var desiredForms = request.TaxFormIds.Distinct().ToList();
        foreach (var form in existingForms.Where(f => !desiredForms.Contains(f.TaxFormId)))
        {
            rems.TaxForms.Remove(form);
            _requests.RemoveTaxForm(form);
        }
        foreach (var formId in desiredForms.Where(f => existingForms.All(x => x.TaxFormId != f)))
        {
            var created = new RemsTaxForm
            {
                Id = Guid.NewGuid(),
                TenantId = rems.TenantId,
                REMSId = rems.Id,
                TaxFormId = formId,
            };
            rems.TaxForms.Add(created);
            await _requests.AddTaxFormAsync(created, cancellationToken);
        }

        await LogEngagementUpdatedAsync(rems.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await BuildEngagementViewAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(view!, "Tax engagement detail updated."));
    }

    private async Task WriteTaxDueDatesAsync(RemsRequest rems, UpdateRemsTaxDetailRequest request, Guid? me, CancellationToken cancellationToken)
    {
        var rows = rems.TaxDueDates.Where(d => !d.Deleted).ToList();
        if (request.FiscalYearEnd is not { } fye)
        {
            foreach (var row in rows)
            {
                rems.TaxDueDates.Remove(row);
                _requests.RemoveTaxDueDate(row);
            }
            return;
        }

        var schedule = RemsTaxDueDates.Effective(fye, request.OriginalDueDate, request.FirstExtensionDueDate);
        await UpsertTaxDueDateAsync(rems, rows, RemsTaxDueDateKind.Original, schedule.OriginalDueDate, request.OriginalDueDate is not null, me, cancellationToken);
        await UpsertTaxDueDateAsync(rems, rows, RemsTaxDueDateKind.FirstExtension, schedule.ExtendedDueDate, request.FirstExtensionDueDate is not null, me, cancellationToken);
    }

    private async Task UpsertTaxDueDateAsync(
        RemsRequest rems, List<RemsTaxDueDate> rows, RemsTaxDueDateKind kind, DateOnly dueOn, bool overridden, Guid? me,
        CancellationToken cancellationToken)
    {
        var ofKind = rows.Where(d => d.Kind == kind).OrderBy(d => d.CreatedOnUtc).ToList();
        var row = ofKind.FirstOrDefault();
        if (row is null)
        {
            row = new RemsTaxDueDate
            {
                Id = Guid.NewGuid(),
                TenantId = rems.TenantId,
                REMSId = rems.Id,
                Kind = kind,
                DueOn = dueOn,
                IsOverridden = overridden,
                OverriddenByUserId = overridden ? me : null,
            };
            rems.TaxDueDates.Add(row);
            await _requests.AddTaxDueDateAsync(row, cancellationToken);
            return;
        }

        row.DueOn = dueOn;
        row.IsOverridden = overridden;
        row.OverriddenByUserId = overridden ? me : null;
        _requests.UpdateTaxDueDate(row);
        // One row per kind; anything beyond the first is a leftover.
        foreach (var extra in ofKind.Skip(1))
        {
            rems.TaxDueDates.Remove(extra);
            _requests.RemoveTaxDueDate(extra);
        }
    }

    // -------------------- Part B: marketing, commission --------------------

    /// <summary>
    /// Set the engagement marketing tags (AC-REMS-017): a list of REMS marketing option ids, at least
    /// one required to save.
    /// </summary>
    [HttpPut("engagements/{id:guid}/marketing")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsEngagementView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetMarketing(Guid id, [FromBody] SetRemsMarketingRequest request, CancellationToken cancellationToken)
    {
        var (rems, refusal) = await OpenForEditAsync(id, cancellationToken);
        if (rems is null)
        {
            return refusal!;
        }

        var valid = await ResolveOptionItemIdsAsync(MarketingSetKey, cancellationToken);
        var unknown = request.MarketingMethodIds.Where(m => !valid.Contains(m)).ToList();
        if (unknown.Count > 0)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", "One or more marketingMethodIds are not valid REMS marketing methods."));
        }

        var desired = request.MarketingMethodIds.Distinct().ToList();
        var existing = rems.MarketingMethods.Where(m => !m.Deleted).ToList();
        foreach (var method in existing.Where(m => !desired.Contains(m.MarketingMethodId)))
        {
            rems.MarketingMethods.Remove(method);
            _requests.RemoveMarketingMethod(method);
        }
        foreach (var methodId in desired.Where(m => existing.All(x => x.MarketingMethodId != m)))
        {
            var created = new RemsMarketingMethod
            {
                Id = Guid.NewGuid(),
                TenantId = rems.TenantId,
                REMSId = rems.Id,
                MarketingMethodId = methodId,
            };
            rems.MarketingMethods.Add(created);
            await _requests.AddMarketingMethodAsync(created, cancellationToken);
        }

        await LogEngagementUpdatedAsync(rems.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await BuildEngagementViewAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(view!, "REMS marketing tags updated."));
    }

    /// <summary>
    /// Set the engagement commission splits (AC-REMS-016): up to ten recipients, each &gt; 0 and &lt;=
    /// 100, allocating no more than 100% in total.
    /// </summary>
    [HttpPut("engagements/{id:guid}/commission")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsEngagementView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetCommission(Guid id, [FromBody] SetRemsCommissionRequest request, CancellationToken cancellationToken)
    {
        var (rems, refusal) = await OpenForEditAsync(id, cancellationToken);
        if (rems is null)
        {
            return refusal!;
        }

        // Every recipient must resolve to a real user.
        foreach (var split in request.Splits)
        {
            if (await _users.GetByIdAsync(split.EmployeeId, cancellationToken) is null)
            {
                return BadRequest(ApiResponseFactory.Error(
                    ApiErrorCodes.ValidationFailed, "Validation failed.", $"Unknown employeeId {split.EmployeeId}."));
            }
        }

        // STATIC-APPROVAL-POLICY: the seats approve at their own stage and may not also be paid commission.
        var policy = await _policy.ForTenantAsync(User.GetActiveTenantId(), cancellationToken);
        if (policy.StaticRouting
            && request.Splits.Select(s => s.EmployeeId).Intersect(RemsStaticApprovalRoute.ReservedUserIds(rems, policy)).Any())
        {
            return StatusCode(StatusCodes.Status409Conflict, ApiResponseFactory.Error(
                CodeCommissionReserved,
                "A commission recipient cannot be the CSE, the Department Director or a Shareholder on this request.",
                "A commission recipient cannot be the CSE, the Department Director or a Shareholder on this request."));
        }

        var existing = rems.CommissionSplits.Where(s => !s.Deleted).ToList();
        var desiredByEmployee = request.Splits.ToDictionary(s => s.EmployeeId, s => s.Percentage);

        foreach (var split in existing.Where(s => !desiredByEmployee.ContainsKey(s.EmployeeId)))
        {
            rems.CommissionSplits.Remove(split);
            _requests.RemoveCommissionSplit(split);
        }
        foreach (var (employeeId, percentage) in desiredByEmployee)
        {
            var split = existing.FirstOrDefault(s => s.EmployeeId == employeeId);
            if (split is null)
            {
                var created = new RemsCommissionSplit
                {
                    Id = Guid.NewGuid(),
                    TenantId = rems.TenantId,
                    REMSId = rems.Id,
                    EmployeeId = employeeId,
                    CommissionPercentage = percentage,
                };
                rems.CommissionSplits.Add(created);
                await _requests.AddCommissionSplitAsync(created, cancellationToken);
            }
            else if (split.CommissionPercentage != percentage)
            {
                split.CommissionPercentage = percentage;
            }
        }

        await LogEngagementUpdatedAsync(rems.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await BuildEngagementViewAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(view!, "REMS commission splits updated."));
    }

    // -------------------- Helpers --------------------

    /// <summary>
    /// An engagement is editable only while it is Draft or has been Rejected (a fresh rework); locked
    /// once routed for approval or approved.
    /// </summary>
    private static bool IsEditable(RemsRequest rems)
        => RemsDerived.EngagementStatusOf(rems) is RemsEngagementStatus.Draft or RemsEngagementStatus.Rejected;

    /// <summary>The refusal for reading a request's setup, or null to carry on.</summary>
    private IActionResult? GuardCanRead(RemsRequest rems)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        return RemsSetupAccess.CanRead(User, rems, me)
            ? null
            : StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("Not permitted to view this request's engagement."));
    }

    /// <summary>The refusal for WRITING a request's setup, or null to carry on.</summary>
    private async Task<IActionResult?> GuardSetupOwnerAsync(RemsRequest rems, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        return RemsSetupAccess.CanWork(
            User, rems, me, await RemsSetupAccess.CoverForWorkAsync(_delegations, rems, me, cancellationToken))
            ? null
            : StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden(RemsSetupAccess.WorkDeniedReason(rems)));
    }

    /// <summary>
    /// The request an engagement route names, loaded whole and cleared for writing; otherwise the refusal
    /// to send back.
    /// </summary>
    private async Task<(RemsRequest? Request, IActionResult? Refusal)> OpenForEditAsync(Guid remsId, CancellationToken cancellationToken)
    {
        var rems = await _requests.GetWorkspaceAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return (null, NotFound(ApiResponseFactory.NotFound("REMS engagement not found.")));
        }
        if (await GuardSetupOwnerAsync(rems, cancellationToken) is { } denied)
        {
            return (null, denied);
        }
        if (!IsEditable(rems))
        {
            return (null, EngagementLocked());
        }
        return (rems, null);
    }

    /// <summary>
    /// The request a client edit is made under. A client can have several requests now; the newest is
    /// the one being worked, and it is what the write guard and the activity log speak of.
    /// </summary>
    private async Task<RemsRequest?> RequestOfClientAsync(Guid clientId, CancellationToken cancellationToken)
        => (await _requests.ListByClientAsync(clientId, cancellationToken)).FirstOrDefault();

    /// <summary>
    /// A Maconomy customer's name and contact come from the directory rather than the row; the client and
    /// the parties it declared are read with that in place.
    /// </summary>
    private async Task HydrateAsync(RemsRequest rems, CancellationToken cancellationToken)
    {
        await _directory.HydrateAsync(rems.Client, cancellationToken);
        foreach (var relationship in rems.Client?.Relationships.Where(r => !r.Deleted) ?? Enumerable.Empty<ClientRelationship>())
        {
            await _directory.HydrateAsync(relationship.RelatedClient, cancellationToken);
        }
    }

    /// <summary>
    /// The sync chip per list row. The state lives on the client now and the row does not carry it, so
    /// the rows that can show one — a Maconomy customer's, or an approved request's — are read back.
    /// </summary>
    /// <summary>
    /// The two Maconomy chips per row, for the rows that can carry one: the customer status word and the
    /// job view. Each needs the request itself, so only approved rows and Maconomy clients are read.
    /// </summary>
    private async Task<Dictionary<Guid, (string? Customer, RemsMaconomyJobSyncView? Job)>> SyncStatusesAsync(
        IReadOnlyList<RemsClientFormItem> items, CancellationToken cancellationToken)
    {
        var statuses = new Dictionary<Guid, (string? Customer, RemsMaconomyJobSyncView? Job)>();
        var candidates = items
            .Where(i => i.MaconomyCustomerNumber is not null || i.RequestStatus == RemsRequestStatuses.Approved)
            .ToList();
        if (candidates.Count == 0)
        {
            return statuses;
        }
        var jobContext = User.GetActiveTenantId() is { } tenantId
            ? await _jobSync.GetContextAsync(tenantId, cancellationToken)
            : MaconomyJobSyncContext.None;
        foreach (var item in candidates)
        {
            var rems = await _requests.GetByIdAsync(item.RemsId, cancellationToken);
            if (rems is null)
            {
                continue;
            }
            // Only the status word is read here; the flags For() also works out belong to the request page.
            var customer = rems.Client is { } client
                ? RemsMaconomySyncViews.For(rems, _customerSync.IsStale(client), enabled: false, canManage: false)?.Status
                : null;
            RemsMaconomyJobSyncView? job = null;
            if (item.RequestStatus == RemsRequestStatuses.Approved)
            {
                string? syncedBy = null;
                if (rems.MaconomyJobSyncedById is { } by)
                {
                    var names = await _users.GetFullNamesAsync(new[] { by }, cancellationToken);
                    names.TryGetValue(by, out syncedBy);
                }
                job = RemsMaconomyJobSyncViews.ForListed(rems, jobContext, _jobSync.IsStale(rems), canManage: true, syncedBy);
            }
            statuses[item.RemsId] = (customer, job);
        }
        return statuses;
    }

    private async Task<Guid?> MappedDirectorAsync(string? department, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(department))
        {
            return null;
        }

        var settings = await _settings.GetAsync(cancellationToken);
        var normalized = department.Trim().ToLowerInvariant();
        return settings?.DepartmentDirectors
            .Where(d => !d.Deleted)
            .FirstOrDefault(d => d.Department!.Value.Trim().ToLowerInvariant() == normalized)?.DirectorUserId;
    }

    private Task LogEngagementUpdatedAsync(Guid remsId, CancellationToken cancellationToken)
        => _activity.WriteAsync(
            new CreateActivityEventDto(EntityType.Rems, remsId, ActivityEventTypes.RemsEngagementUpdated),
            cancellationToken);

    private async Task<RemsEngagementView?> BuildEngagementViewAsync(Guid remsId, CancellationToken cancellationToken)
    {
        var rems = await _requests.GetWorkspaceAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return null;
        }

        var names = await _users.GetFullNamesAsync(CollectUserIds(rems), cancellationToken);
        return RemsWorkspaceMapper.Engagement(rems, names);
    }

    private static IReadOnlyCollection<Guid> CollectUserIds(RemsRequest rems)
    {
        var ids = new HashSet<Guid>();
        if (rems.DepartmentDirectorId is { } d) ids.Add(d);
        if (rems.EngagementExecutiveId is { } x) ids.Add(x);
        if (rems.BillingManagerId is { } b) ids.Add(b);
        foreach (var s in rems.CommissionSplits.Where(s => !s.Deleted))
        {
            ids.Add(s.EmployeeId);
        }
        return ids;
    }

    private async Task<HashSet<Guid>> ResolveOptionItemIdsAsync(string setKey, CancellationToken cancellationToken)
    {
        var tenantId = User.GetActiveTenantId();
        var set = await _optionSets.GetEffectiveSetAsync(tenantId, EntityType.Rems, setKey, cancellationToken);
        return set?.Items.Where(i => !i.Deleted).Select(i => i.Id).ToHashSet() ?? new HashSet<Guid>();
    }

    /// <summary>Replaces the request's file of a kind with the given media; there is at most one of each kind.</summary>
    private async Task SetFileAsync(RemsRequest rems, RemsFileKind kind, Guid mediaId, CancellationToken cancellationToken)
    {
        RemoveFiles(rems, kind);
        var file = new RemsFile
        {
            Id = Guid.NewGuid(),
            TenantId = rems.TenantId,
            REMSId = rems.Id,
            MediaId = mediaId,
            Kind = kind,
        };
        rems.Files.Add(file);
        await _requests.AddFileAsync(file, cancellationToken);
    }

    /// <summary>Takes every file of a kind off the request; true when there was one.</summary>
    private bool RemoveFiles(RemsRequest rems, RemsFileKind kind)
    {
        var files = rems.Files.Where(f => !f.Deleted && f.Kind == kind).ToList();
        foreach (var file in files)
        {
            rems.Files.Remove(file);
            _requests.RemoveFile(file);
        }
        return files.Count > 0;
    }

    /// <summary>Upserts (or, when blank, removes) the client's address of a kind from a supplied input.</summary>
    private async Task UpsertClientAddressAsync(Client client, string kind, RemsAddressInput? input, CancellationToken cancellationToken)
    {
        if (input is null)
        {
            return; // not supplied — leave unchanged.
        }

        var typeId = await _codes.RequireRemsIdAsync(RemsOptionSetKeys.AddressType, kind, cancellationToken);
        var existing = client.Addresses.Where(a => !a.Deleted && a.AddressTypeId == typeId).OrderBy(a => a.CreatedOnUtc).FirstOrDefault();
        if (!input.HasAny)
        {
            if (existing is not null)
            {
                client.Addresses.Remove(existing);
                _clients.RemoveAddress(existing);
            }
            return;
        }

        var address = existing?.Address ?? (existing is null ? null : await _addresses.GetByIdAsync(existing.AddressId, cancellationToken));
        if (address is not null)
        {
            ApplyAddress(address, input);
            _addresses.Update(address);
            return;
        }

        var created = new Address { Id = Guid.NewGuid(), AddressType = RemsAddressTypes.PlatformTypeOf(kind) };
        ApplyAddress(created, input);
        await _addresses.AddAsync(created, cancellationToken);
        var link = new ClientAddress
        {
            Id = Guid.NewGuid(),
            TenantId = client.TenantId,
            ClientId = client.Id,
            AddressId = created.Id,
            AddressTypeId = typeId,
            IsCurrent = true,
            Address = created,
        };
        client.Addresses.Add(link);
        await _clients.AddAddressAsync(link, cancellationToken);
    }

    /// <summary>
    /// Copies the standard address block onto a shared <see cref="Address"/> (create and update
    /// alike).
    /// </summary>
    private static void ApplyAddress(Address address, RemsAddressInput input)
    {
        address.AddressLine1 = Normalize(input.Street);
        address.AddressLine2 = Normalize(input.AddressLine2);
        address.CityName = Normalize(input.City);
        address.StateCode = Normalize(input.StateCode);
        address.StateName = Normalize(input.State);
        address.CountryCode = Normalize(input.CountryCode);
        address.CountryName = Normalize(input.CountryName);
        address.PostalCode = Normalize(input.Zip);
    }

    /// <param name="sourceRemsId">
    /// The request whose engagement setup captured this contact, recorded as the person's provenance —
    /// otherwise they are indistinguishable in the Person list from somebody onboarded deliberately.
    /// </param>
    private static Person NewContactPerson(RemsEntityContactInput input, Guid tenantId, Guid sourceRemsId)
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            PersonCode = "PER-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            TenantId = tenantId,
            IsActive = true,
            SourceEntityType = EntityType.Rems,
            SourceEntityId = sourceRemsId,
        };
        ApplyContactPerson(person, input);
        return person;
    }

    private static void ApplyContactPerson(Person person, RemsEntityContactInput input)
    {
        var (first, last) = SplitName(input.Name);
        person.FirstName = first;
        person.LastName = last;
        person.DisplayName = Normalize(input.Name) ?? first;
        person.PrimaryEmail = Normalize(input.Email);
        person.MobileNumber = Normalize(input.Phone);
        person.LastProfileUpdatedOn = DateTime.UtcNow;
    }

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

    private IActionResult EngagementLocked()
        => StatusCode(StatusCodes.Status409Conflict, ApiResponseFactory.Error(
            CodeEngagementLocked, "This engagement is locked.", "The engagement cannot be edited while it is pending approval or approved."));

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
