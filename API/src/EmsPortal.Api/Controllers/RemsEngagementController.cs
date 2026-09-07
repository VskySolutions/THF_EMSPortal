using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.Security;
using EmsPortal.Api.Validators.Rems;
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
/// client/entity/engagement workspace, the audit/government/tax conditional details.
/// </summary>
[ApiController]
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
    private const string CodeCopyInvalid = "REMS_COPY_INVALID";

    private const string MarketingSetKey = "REMSMarketing_MarketingMethods.MarketingMethodId";
    private const string TaxFormSetKey = "REMS.TaxForm";

    private readonly IRemsRepository _rems;
    /// <summary>Only to answer whether an initiator has cover arranged — see RemsSetupAccess.CanWork.</summary>
    private readonly IRemsDelegationRepository _delegations;
    private readonly IRemsFormRepository _forms;
    private readonly IRemsClientRepository _clients;
    private readonly IRemsEngagementRepository _engagements;
    private readonly IRemsSettingsRepository _settings;
    private readonly IAddressRepository _addresses;
    private readonly IPersonRepository _persons;
    private readonly IMediaRepository _media;
    private readonly IOptionSetRepository _optionSets;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityEventWriter _activity;
    private readonly IOptionCodeResolver _codes;

    public RemsEngagementController(
        IRemsRepository rems,
        IRemsDelegationRepository delegations,
        IRemsFormRepository forms,
        IRemsClientRepository clients,
        IRemsEngagementRepository engagements,
        IRemsSettingsRepository settings,
        IAddressRepository addresses,
        IPersonRepository persons,
        IMediaRepository media,
        IOptionSetRepository optionSets,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IActivityEventWriter activity,
        IOptionCodeResolver codes)
    {
        _rems = rems;
        _delegations = delegations;
        _forms = forms;
        _clients = clients;
        _engagements = engagements;
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

        var slice = string.Equals(assignment?.Trim(), "mine", StringComparison.OrdinalIgnoreCase)
            ? RemsClientFormAssignment.Mine
            : RemsClientFormAssignment.All;

        var (items, total) = await _forms.ListClientFormsAsync(
            new RemsClientFormQuery(search, submitted, requestStatus, me, slice, new SortRequest(sortBy, descending), page, limit), cancellationToken);
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

        var rows = items.Select(i => new RemsClientFormRow(
            i.RemsId, i.RemsNumber, i.ClientName, i.ClientNameSuffix,
            i.RequestStatus,
            HasForm: true, i.Submitted, i.SubmittedOnUtc,
            RemsWorkspaceMapper.UserRef(i.AdminAssignedToId, names), RemsWorkspaceMapper.UserRef(i.CSEId, names),
            CanPickUp: mayAssign && i.AdminAssignedToId is null,
            CanHandBack: mayAssign && i.AdminAssignedToId is { } holder && (elevated || holder == me),
            NameOf(i.CreatedById), i.CreatedOnUtc, NameOf(i.UpdatedById), i.UpdatedOnUtc));

        return Ok(ApiResponseFactory.Paginated(rows, "REMS client forms retrieved.", page, limit, total));
    }

    /// <summary>
    /// The submitted-form view (AC-REMS-013.2/3), rendered from the <c>REMSFormSubmission</c> payload
    /// as plain fields — distinct from the editable workspace data.
    /// </summary>
    [HttpGet("requests/{remsId:guid}/submission")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsRead)]
    [ProducesResponseType<ApiResponse<RemsSubmissionView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submission(Guid remsId, CancellationToken cancellationToken)
    {
        var rems = await _rems.GetByIdAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        if (GuardCanRead(rems) is { } notAllowed)
        {
            return notAllowed;
        }

        var form = await _forms.GetWithSubmissionsByRemsIdAsync(remsId, cancellationToken);
        var submission = form?.Submissions.OrderByDescending(s => s.SubmittedOnUtc).FirstOrDefault();
        if (form is null || submission is null)
        {
            return NotFound(ApiResponseFactory.NotFound("This request has no submitted form."));
        }

        return Ok(ApiResponseFactory.Success(
            await BuildSubmissionViewAsync(rems, form, submission, cancellationToken),
            "REMS submitted form retrieved."));
    }

    /// <summary>Correct the client's submitted answers, in place (Admin only).</summary>
    [HttpPut("requests/{remsId:guid}/submission")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<RemsSubmissionView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSubmission(
        Guid remsId, [FromBody] RemsFormPayloadV1? payload, CancellationToken cancellationToken)
    {
        var rems = await _rems.GetByIdAsync(remsId, cancellationToken);
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

        var form = await _forms.GetWithSubmissionsByRemsIdAsync(remsId, cancellationToken);
        var submission = form?.Submissions.OrderByDescending(s => s.SubmittedOnUtc).FirstOrDefault();
        if (form is null || submission is null)
        {
            return NotFound(ApiResponseFactory.NotFound("This request has no submitted form."));
        }

        if (payload is null)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", "payload: No form data was supplied."));
        }

        // Not the Admin's to change: the email is the address the invite went to, and the version pins the
        // shape the stored JSON is read back in.
        payload.Email = rems.CustomerEmail;
        payload.Version = 1;

        var validation = new RemsFormPayloadValidator().Validate(payload, form.EntityType!.Value);
        if (!validation.IsValid)
        {
            return BadRequest(ApiResponseFactory.ValidationError(validation.Errors));
        }

        // Mutated, not Update()d.
        submission.SubmittedPayload = RemsFormPayloadJson.Serialize(payload);
        await _activity.WriteAsync(
            new CreateActivityEventDto(EntityType.Rems, rems.Id, ActivityEventTypes.RemsFormCorrected),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(
            await BuildSubmissionViewAsync(rems, form, submission, cancellationToken),
            "REMS submitted form updated."));
    }

    /// <summary>
    /// The submitted-form view, with the correction trail resolved and this caller's own right to
    /// correct it.
    /// </summary>
    private async Task<RemsSubmissionView> BuildSubmissionViewAsync(
        REMS rems, REMSForm form, REMSFormSubmission submission, CancellationToken cancellationToken)
    {
        var payload = RemsFormPayloadJson.TryDeserialize(submission.SubmittedPayload) ?? new RemsFormPayloadV1();

        string? editedBy = null;
        if (submission.UpdatedById is { } editorId)
        {
            var names = await _users.GetFullNamesAsync(new[] { editorId }, cancellationToken);
            editedBy = names.TryGetValue(editorId, out var name) ? name : "an admin";
        }

        return new RemsSubmissionView(
            submission.Id, rems.Id, rems.REMSNumber, form.EntityType!.Value, rems.CustomerEmail,
            rems.ClientNameSuffix,
            submission.SubmittedOnUtc, payload,
            editedBy,
            editedBy is null ? null : submission.UpdatedOnUtc,
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
        var rems = await _rems.GetByIdAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        if (GuardCanRead(rems) is { } notAllowed)
        {
            return notAllowed;
        }

        // Null until the client submits their intake form. Everything below tolerates that.
        var client = await _clients.GetByRemsIdAsync(remsId, cancellationToken);

        // One engagement, belonging to the request rather than to any entity.
        var engagement = await _engagements.GetByRemsIdAsync(remsId, cancellationToken);
        var engagements = engagement is null ? Array.Empty<REMSEngagement>() : new[] { engagement };
        var engagementIds = engagements.Select(e => e.Id).ToList();

        var audit = (await _engagements.ListAuditDetailsAsync(engagementIds, cancellationToken)).ToDictionary(d => d.REMSEngagementId);
        var government = (await _engagements.ListGovernmentDetailsAsync(engagementIds, cancellationToken)).ToDictionary(d => d.REMSEngagementId);
        var tax = (await _engagements.ListTaxDetailsAsync(engagementIds, cancellationToken)).ToDictionary(d => d.REMSEngagementId);

        // The department → director map travels with the workspace so the setup form can name the director
        // a department maps to as soon as it is picked, instead of waiting for the save to come back.
        var settings = await _settings.GetAsync(cancellationToken);
        var directorRows = settings?.DepartmentDirectors.Where(d => !d.Deleted).ToList() ?? new();

        var names = await _users.GetFullNamesAsync(
            CollectUserIds(engagements).Concat(directorRows.Select(d => d.DirectorUserId)), cancellationToken);

        var departmentDirectors = directorRows
            .OrderBy(d => d.Department!.Value)
            .Select(d => new RemsDepartmentDirectorView(
                d.Department!.Value,
                new RemsUserRef(d.DirectorUserId, names.TryGetValue(d.DirectorUserId, out var n) ? n : string.Empty)))
            .ToList();

        // The other businesses this client named, with the request each has produced. Resolved to REMS
        // numbers in one lookup so a row can link to what it created rather than just claiming it exists.
        var additionalRows = await _rems.ListAdditionalEntitiesAsync(remsId, cancellationToken);
        var createdNumbers = await _rems.GetNumbersAsync(
            additionalRows.Where(a => a.CreatedREMSId.HasValue).Select(a => a.CreatedREMSId!.Value).ToList(),
            cancellationToken);
        var additionalEntities = additionalRows
            .Select(a => new RemsAdditionalEntityView(
                a.Id, a.FullName, a.EmailAddress, a.PhoneNumber, a.CreatedREMSId,
                a.CreatedREMSId is { } createdId && createdNumbers.TryGetValue(createdId, out var n) ? n : null))
            .ToList();

        var formState = (await _rems.GetFormStatesAsync(new[] { remsId }, cancellationToken)).FirstOrDefault();

        var workspace = RemsWorkspaceMapper.Workspace(
            rems, client, engagement, audit, government, tax, names,
            formState?.EntityType, additionalEntities, departmentDirectors);
        return Ok(ApiResponseFactory.Success(workspace, "REMS engagement workspace retrieved."));
    }

    // -------------------- Part A: client + entity editing --------------------

    /// <summary>Update the client record (AC-REMS-014).</summary>
    [HttpPut("requests/{remsId:guid}/client")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<RemsClientView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateClient(Guid remsId, [FromBody] UpdateRemsClientRequest request, CancellationToken cancellationToken)
    {
        if (await GuardSetupOwnerAsync(remsId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var client = await _clients.GetByRemsIdAsync(remsId, cancellationToken);
        if (client is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS client not found."));
        }

        if (request.Name is not null) client.Name = request.Name.Trim();
        if (request.MobileNumber is not null) client.MobileNumber = Normalize(request.MobileNumber);
        // The wire carries the CODE; the column is a foreign key to the option item it names. An unknown
        // code resolves to null rather than being stored as-is -- there is nothing to point at.
        if (request.ReferralSource is not null)
        {
            client.ReferralSourceId = await _codes.IdOfAsync(
                EntityType.Rems, RemsOptionSetKeys.ReferralSource, request.ReferralSource, cancellationToken);
        }
        if (request.BillingContactName is not null) client.BillingContactName = Normalize(request.BillingContactName);
        if (request.BillingEmail is not null) client.BillingEmail = Normalize(request.BillingEmail);

        // Billing ADDRESSES are not edited here. They are the main entity's rows rather than the client's,
        // there may be several of them, and they are written by the client's own intake form.
        _clients.Update(client);
        await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, remsId, ActivityEventTypes.RemsEngagementUpdated), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _clients.GetByRemsIdAsync(remsId, cancellationToken) ?? client;
        var view = new RemsClientView(
            refreshed.Id, refreshed.Name, refreshed.Email, refreshed.MobileNumber,
            refreshed.ReferralSource?.Value,
            refreshed.BillingContactName, refreshed.BillingEmail);
        return Ok(ApiResponseFactory.Success(view, "REMS client updated."));
    }

    /// <summary>Replace an entity's physical/mailing addresses (AC-REMS-014).</summary>
    [HttpPut("entities/{entityId:guid}/addresses")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsEntityAddressView>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEntityAddresses(
        Guid entityId, [FromBody] UpdateRemsEntityAddressesRequest request, CancellationToken cancellationToken)
    {
        var entity = await _clients.GetEntityAsync(entityId, cancellationToken);
        if (entity is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS entity not found."));
        }

        if (await GuardSetupOwnerAsync(entity.Client!.REMSId, cancellationToken) is { } denied)
        {
            return denied;
        }

        await UpsertEntityAddressAsync(entity, RemsAddressType.Physical, request.PhysicalAddress, cancellationToken);
        await UpsertEntityAddressAsync(entity, RemsAddressType.Mailing, request.MailingAddress, cancellationToken);

        await _activity.WriteAsync(new CreateActivityEventDto(
            EntityType.Rems, entity.Client!.REMSId, ActivityEventTypes.RemsEngagementUpdated), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _clients.GetEntityAsync(entityId, cancellationToken) ?? entity;
        var rows = refreshed.Addresses.Where(a => !a.Deleted)
            .Select(a => new RemsEntityAddressView(a.Id, a.AddressType.ToString(), RemsWorkspaceMapper.Address(a.Address)!));
        return Ok(ApiResponseFactory.Success(rows, "REMS entity addresses updated."));
    }

    /// <summary>Replace an entity's contacts (AC-REMS-014).</summary>
    [HttpPut("entities/{entityId:guid}/contacts")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsEntityContactView>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEntityContacts(
        Guid entityId, [FromBody] UpdateRemsEntityContactsRequest request, CancellationToken cancellationToken)
    {
        var entity = await _clients.GetEntityAsync(entityId, cancellationToken);
        if (entity is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS entity not found."));
        }

        if (await GuardSetupOwnerAsync(entity.Client!.REMSId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var existing = entity.Contacts.Where(c => !c.Deleted).ToList();
        var desiredRoles = request.Contacts.Select(c => c.Role.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove contacts whose role is no longer present.
        foreach (var contact in existing.Where(c => !desiredRoles.Contains(c.ContactRole)))
        {
            _clients.RemoveEntityContact(contact);
        }

        // Upsert each supplied contact by role.
        foreach (var input in request.Contacts)
        {
            var role = input.Role.Trim();
            var contact = existing.FirstOrDefault(c => string.Equals(c.ContactRole, role, StringComparison.OrdinalIgnoreCase));
            if (contact is null)
            {
                var person = NewContactPerson(input, entity.Client!.REMSId);
                await _persons.AddAsync(person, cancellationToken);
                await _clients.AddEntityContactAsync(new REMSEntityContact
                {
                    Id = Guid.NewGuid(),
                    REMSEntityId = entity.Id,
                    PersonId = person.Id,
                    ContactRole = role,
                    IsRequired = input.IsRequired,
                }, cancellationToken);
            }
            else
            {
                contact.IsRequired = input.IsRequired;
                if (contact.Person is { } person)
                {
                    ApplyContactPerson(person, input);
                    _persons.Update(person);
                }
            }
        }

        await _activity.WriteAsync(new CreateActivityEventDto(
            EntityType.Rems, entity.Client!.REMSId, ActivityEventTypes.RemsEngagementUpdated), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _clients.GetEntityAsync(entityId, cancellationToken) ?? entity;
        var rows = refreshed.Contacts.Where(c => !c.Deleted)
            .Select(c => new RemsEntityContactView(
                c.Id, c.ContactRole, c.IsRequired, c.Person?.DisplayName, c.Person?.PrimaryEmail,
                c.Person?.MobileNumber, c.Person?.Suffix));
        return Ok(ApiResponseFactory.Success(rows, "REMS entity contacts updated."));
    }

    // -------------------- Part A: engagement editing --------------------

    /// <summary>Update an engagement's team, service placement and fee/realization (AC-REMS-014).</summary>
    [HttpPut("engagements/{id:guid}")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsEngagementUpdateResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEngagement(Guid id, [FromBody] UpdateRemsEngagementRequest request, CancellationToken cancellationToken)
    {
        var engagement = await _engagements.GetWithContextAsync(id, cancellationToken);
        if (engagement is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }

        if (await GuardSetupOwnerAsync(engagement.REMSId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (!IsEditable(engagement))
        {
            return EngagementLocked();
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
            && !string.Equals(incomingDepartment, engagement.Department?.Value, StringComparison.Ordinal);
        if (request.Department is not null)
        {
            engagement.DepartmentId =
                await _codes.RemsIdAsync(RemsOptionSetKeys.Department, incomingDepartment, cancellationToken);
        }

        // The two sub-classifications. Nothing branches on either — they narrow the line and the industry
        // group for reporting.
        if (request.ServiceLine is not null)
        {
            engagement.ServiceLineId = await _codes.RemsIdAsync(
                RemsOptionSetKeys.ServiceLine, Normalize(request.ServiceLine), cancellationToken);
        }
        if (request.Industry is not null)
        {
            engagement.IndustryId = await _codes.RemsIdAsync(
                RemsOptionSetKeys.Industry, Normalize(request.Industry), cancellationToken);
        }

        // The department the director is mapped from is the one being SAVED, which on this request may be
        // the incoming code rather than what the row still holds.
        var mappedDirector = await MappedDirectorAsync(
            request.Department is not null ? incomingDepartment : engagement.Department?.Value, cancellationToken);
        if (request.DepartmentDirectorId.HasValue)
        {
            engagement.DepartmentDirectorId = request.DepartmentDirectorId;
        }
        else if (departmentChanged || engagement.DepartmentDirectorId is null)
        {
            // Prefill from the tenant department-director map (may be null = unassigned placeholder).
            engagement.DepartmentDirectorId = mappedDirector;
        }

        if (request.EngagementExecutiveId.HasValue) engagement.EngagementExecutiveId = request.EngagementExecutiveId;
        if (request.BillingManagerId.HasValue) engagement.BillingManagerId = request.BillingManagerId;
        if (request.FirstYearFeeEstimate.HasValue) engagement.FirstYearFeeEstimate = request.FirstYearFeeEstimate;
        // Assurance's own fee, kept apart from the first-year estimate above rather than sharing a column with
        // it: they are different questions.
        if (request.EngagementFee.HasValue) engagement.EngagementFee = request.EngagementFee;
        if (request.RealizationPercentage.HasValue) engagement.RealizationPercentage = request.RealizationPercentage;
        // The billing schedule: how often, and how it actually works.
        if (request.BillingPeriod is not null)
        {
            engagement.BillingPeriodId = await _codes.RemsIdAsync(
                RemsOptionSetKeys.BillingPeriod, Normalize(request.BillingPeriod), cancellationToken);
        }
        if (request.BillingProcessDescription is not null)
        {
            engagement.BillingProcessDescription = Normalize(request.BillingProcessDescription);
        }

        _engagements.Update(engagement);
        await LogEngagementUpdatedAsync(engagement, cancellationToken);
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
        var engagement = await _engagements.GetWithContextAsync(id, cancellationToken);
        if (engagement is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }

        if (await GuardSetupOwnerAsync(engagement.REMSId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (!IsEditable(engagement))
        {
            return EngagementLocked();
        }
        if (await _media.GetByIdAsync(request.MediaId, cancellationToken) is null)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", "Unknown mediaId."));
        }

        var detail = await _engagements.GetAuditDetailAsync(id, cancellationToken);
        if (detail is null)
        {
            await _engagements.AddAuditDetailAsync(new REMSEngagementAuditDetail
            {
                Id = Guid.NewGuid(),
                REMSEngagementId = id,
                ClientAcceptanceFormMediaId = request.MediaId,
            }, cancellationToken);
        }
        else
        {
            detail.ClientAcceptanceFormMediaId = request.MediaId;
        }

        await LogEngagementUpdatedAsync(engagement, cancellationToken);
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
        var engagement = await _engagements.GetWithContextAsync(id, cancellationToken);
        if (engagement is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }

        if (await GuardSetupOwnerAsync(engagement.REMSId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (!IsEditable(engagement))
        {
            return EngagementLocked();
        }

        // Idempotent: an engagement with no form on file is already in the state the caller asked for, and
        // answering 404 to "there is nothing there" would make a double-click read as an error.
        var detail = await _engagements.GetAuditDetailAsync(id, cancellationToken);
        if (detail?.ClientAcceptanceFormMediaId is not null)
        {
            detail.ClientAcceptanceFormMediaId = null;
            await LogEngagementUpdatedAsync(engagement, cancellationToken);
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
        var engagement = await _engagements.GetWithContextAsync(id, cancellationToken);
        if (engagement is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }

        if (await GuardSetupOwnerAsync(engagement.REMSId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (!IsEditable(engagement))
        {
            return EngagementLocked();
        }

        var detail = await _engagements.GetGovernmentDetailAsync(id, cancellationToken);
        if (detail is null)
        {
            detail = new REMSEngagementGovernmentDetail { Id = Guid.NewGuid(), REMSEngagementId = id };
            await _engagements.AddGovernmentDetailAsync(detail, cancellationToken);
        }

        detail.ContractNumber = Normalize(request.ContractNumber);
        detail.FloridaOnePercentStateFeeApplies = request.FloridaOnePercentStateFeeApplies;
        detail.ContractStartDate = request.ContractStartDate;
        detail.ContractEndDate = request.ContractEndDate;
        detail.OriginalTerm = Normalize(request.OriginalTerm);
        detail.RenewalTerms = Normalize(request.RenewalTerms);
        detail.PurchaseOrderStartDate = request.PurchaseOrderStartDate;
        detail.PurchaseOrderEndDate = request.PurchaseOrderEndDate;
        // GCS. Every field on this record is written from the request, blanks included.
        detail.PurchaseOrderNumber = Normalize(request.PurchaseOrderNumber);
        detail.PurchaseOrderAmount = request.PurchaseOrderAmount;
        detail.PersonnelLevelId = await _codes.RemsIdAsync(
            RemsOptionSetKeys.PersonnelLevel, Normalize(request.PersonnelLevel), cancellationToken);
        detail.BillRatePerHour = request.BillRatePerHour;

        await LogEngagementUpdatedAsync(engagement, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await BuildEngagementViewAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(view!, "Government audit detail updated."));
    }

    /// <summary>Set the ASSURANCE detail: the client's fiscal year end and the administrative fees.</summary>
    [HttpPut("engagements/{id:guid}/audit")]
    [RequireAnyPermission(Permissions.RemsEngagementsManage, Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsEngagementView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAuditDetail(Guid id, [FromBody] UpdateRemsAuditDetailRequest request, CancellationToken cancellationToken)
    {
        var engagement = await _engagements.GetWithContextAsync(id, cancellationToken);
        if (engagement is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }

        if (await GuardSetupOwnerAsync(engagement.REMSId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (!IsEditable(engagement))
        {
            return EngagementLocked();
        }

        var detail = await _engagements.GetAuditDetailAsync(id, cancellationToken);
        if (detail is null)
        {
            detail = new REMSEngagementAuditDetail { Id = Guid.NewGuid(), REMSEngagementId = id };
            await _engagements.AddAuditDetailAsync(detail, cancellationToken);
        }

        detail.ClientFiscalYearEnd = request.ClientFiscalYearEnd;
        detail.AdminFeesApply = request.AdminFeesApply;
        // An amount without a "yes" beside it is an amount nobody is charging. Answering "no" clears it
        // rather than leaving a figure behind that no screen shows and every report would still sum.
        detail.AdminFeesAmount = request.AdminFeesApply == true ? request.AdminFeesAmount : null;

        await LogEngagementUpdatedAsync(engagement, cancellationToken);
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
        var engagement = await _engagements.GetWithContextAsync(id, cancellationToken);
        if (engagement is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }

        if (await GuardSetupOwnerAsync(engagement.REMSId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (!IsEditable(engagement))
        {
            return EngagementLocked();
        }
        if (await _media.GetByIdAsync(request.MediaId, cancellationToken) is null)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", "Unknown mediaId."));
        }

        var detail = await _engagements.GetGovernmentDetailAsync(id, cancellationToken);
        if (detail is null)
        {
            detail = new REMSEngagementGovernmentDetail { Id = Guid.NewGuid(), REMSEngagementId = id };
            await _engagements.AddGovernmentDetailAsync(detail, cancellationToken);
        }
        detail.PurchaseOrderMediaId = request.MediaId;

        await LogEngagementUpdatedAsync(engagement, cancellationToken);
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
        var engagement = await _engagements.GetWithContextAsync(id, cancellationToken);
        if (engagement is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }

        if (await GuardSetupOwnerAsync(engagement.REMSId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (!IsEditable(engagement))
        {
            return EngagementLocked();
        }

        // Idempotent, exactly as the client-acceptance form's removal is: an engagement carrying no order is
        // already in the state the caller asked.
        var detail = await _engagements.GetGovernmentDetailAsync(id, cancellationToken);
        if (detail?.PurchaseOrderMediaId is not null)
        {
            detail.PurchaseOrderMediaId = null;
            await LogEngagementUpdatedAsync(engagement, cancellationToken);
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
        var engagement = await _engagements.GetWithContextAsync(id, cancellationToken);
        if (engagement is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }

        if (await GuardSetupOwnerAsync(engagement.REMSId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (!IsEditable(engagement))
        {
            return EngagementLocked();
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

        var detail = await _engagements.GetTaxDetailAsync(id, cancellationToken);
        if (detail is null)
        {
            detail = new REMSEngagementTaxDetail { Id = Guid.NewGuid(), REMSEngagementId = id };
            await _engagements.AddTaxDetailAsync(detail, cancellationToken);
        }

        detail.FiscalYearEnd = request.FiscalYearEnd;
        // The rule fills in what was left blank and steps aside for what was typed.
        if (request.FiscalYearEnd is { } fye)
        {
            var schedule = RemsTaxDueDates.Effective(fye, request.OriginalDueDate, request.FirstExtensionDueDate);
            detail.OriginalDueDate = schedule.OriginalDueDate;
            detail.FirstExtensionDueDate = schedule.ExtendedDueDate;
            detail.CalculatedDueDates = RemsTaxDueDates.EffectiveJson(fye, request.OriginalDueDate, request.FirstExtensionDueDate);
        }
        else
        {
            detail.OriginalDueDate = null;
            detail.FirstExtensionDueDate = null;
            detail.CalculatedDueDates = null;
        }

        // Reconcile the tax-form checklist rows by TaxFormId.
        var existingForms = detail.TaxForms.Where(f => !f.Deleted).ToList();
        var desiredForms = request.TaxFormIds.Distinct().ToList();
        foreach (var form in existingForms.Where(f => !desiredForms.Contains(f.TaxFormId)))
        {
            _engagements.RemoveTaxForm(form);
        }
        foreach (var formId in desiredForms.Where(f => existingForms.All(x => x.TaxFormId != f)))
        {
            await _engagements.AddTaxFormAsync(new REMSEngagementTaxForm
            {
                Id = Guid.NewGuid(),
                REMSEngagementTaxDetailId = detail.Id,
                TaxFormId = formId,
            }, cancellationToken);
        }

        await LogEngagementUpdatedAsync(engagement, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await BuildEngagementViewAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(view!, "Tax engagement detail updated."));
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
        var engagement = await _engagements.GetWithContextAsync(id, cancellationToken);
        if (engagement is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }

        if (await GuardSetupOwnerAsync(engagement.REMSId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (!IsEditable(engagement))
        {
            return EngagementLocked();
        }

        var valid = await ResolveOptionItemIdsAsync(MarketingSetKey, cancellationToken);
        var unknown = request.MarketingMethodIds.Where(m => !valid.Contains(m)).ToList();
        if (unknown.Count > 0)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", "One or more marketingMethodIds are not valid REMS marketing methods."));
        }

        var desired = request.MarketingMethodIds.Distinct().ToList();
        var existing = engagement.MarketingMethods.Where(m => !m.Deleted).ToList();
        foreach (var method in existing.Where(m => !desired.Contains(m.MarketingMethodId)))
        {
            _engagements.RemoveMarketingMethod(method);
        }
        foreach (var methodId in desired.Where(m => existing.All(x => x.MarketingMethodId != m)))
        {
            await _engagements.AddMarketingMethodAsync(new REMSEngagementMarketingMethod
            {
                Id = Guid.NewGuid(),
                REMSEngagementId = id,
                MarketingMethodId = methodId,
            }, cancellationToken);
        }

        await LogEngagementUpdatedAsync(engagement, cancellationToken);
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
        var engagement = await _engagements.GetWithContextAsync(id, cancellationToken);
        if (engagement is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }

        if (await GuardSetupOwnerAsync(engagement.REMSId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (!IsEditable(engagement))
        {
            return EngagementLocked();
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

        var existing = engagement.CommissionSplits.Where(s => !s.Deleted).ToList();
        var desiredByEmployee = request.Splits.ToDictionary(s => s.EmployeeId, s => s.Percentage);

        foreach (var split in existing.Where(s => !desiredByEmployee.ContainsKey(s.EmployeeId)))
        {
            _engagements.RemoveCommissionSplit(split);
        }
        foreach (var (employeeId, percentage) in desiredByEmployee)
        {
            var split = existing.FirstOrDefault(s => s.EmployeeId == employeeId);
            if (split is null)
            {
                await _engagements.AddCommissionSplitAsync(new REMSEngagementCommissionSplit
                {
                    Id = Guid.NewGuid(),
                    REMSEngagementId = id,
                    EmployeeId = employeeId,
                    CommissionPercentage = percentage,
                }, cancellationToken);
            }
            else if (split.CommissionPercentage != percentage)
            {
                split.CommissionPercentage = percentage;
            }
        }

        await LogEngagementUpdatedAsync(engagement, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await BuildEngagementViewAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(view!, "REMS commission splits updated."));
    }

    // -------------------- Helpers --------------------

    /// <summary>
    /// An engagement is editable only while it is Draft or has been Rejected (a fresh rework); locked
    /// once routed for approval or approved.
    /// </summary>
    private static bool IsEditable(REMSEngagement engagement)
        => engagement.Status is RemsEngagementStatus.Draft or RemsEngagementStatus.Rejected;

    /// <summary>The refusal for reading a request's setup, or null to carry on.</summary>
    private IActionResult? GuardCanRead(REMS rems)
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
    private async Task<IActionResult?> GuardSetupOwnerAsync(Guid remsId, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var rems = await _rems.GetByIdAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        return RemsSetupAccess.CanWork(
            User, rems, me, await RemsSetupAccess.CoverForWorkAsync(_delegations, rems, me, cancellationToken))
            ? null
            : StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden(RemsSetupAccess.WorkDeniedReason(rems)));
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

    private Task LogEngagementUpdatedAsync(REMSEngagement engagement, CancellationToken cancellationToken)
        => _activity.WriteAsync(
            new CreateActivityEventDto(EntityType.Rems, engagement.REMSId, ActivityEventTypes.RemsEngagementUpdated),
            cancellationToken);

    private async Task<RemsEngagementView?> BuildEngagementViewAsync(Guid engagementId, CancellationToken cancellationToken)
    {
        var engagement = await _engagements.GetByIdAsync(engagementId, cancellationToken);
        if (engagement is null)
        {
            return null;
        }

        var audit = await _engagements.GetAuditDetailAsync(engagementId, cancellationToken);
        var government = await _engagements.GetGovernmentDetailAsync(engagementId, cancellationToken);
        var tax = await _engagements.GetTaxDetailAsync(engagementId, cancellationToken);
        var names = await _users.GetFullNamesAsync(CollectUserIds(new[] { engagement }), cancellationToken);
        return RemsWorkspaceMapper.Engagement(engagement, audit, government, tax, names);
    }

    private static IReadOnlyCollection<Guid> CollectUserIds(IEnumerable<REMSEngagement> engagements)
    {
        var ids = new HashSet<Guid>();
        foreach (var e in engagements)
        {
            if (e.DepartmentDirectorId is { } d) ids.Add(d);
            if (e.EngagementExecutiveId is { } x) ids.Add(x);
            if (e.BillingManagerId is { } b) ids.Add(b);
            foreach (var s in e.CommissionSplits.Where(s => !s.Deleted))
            {
                ids.Add(s.EmployeeId);
            }
        }
        return ids;
    }

    private async Task<HashSet<Guid>> ResolveOptionItemIdsAsync(string setKey, CancellationToken cancellationToken)
    {
        var tenantId = User.GetActiveTenantId();
        var set = await _optionSets.GetEffectiveSetAsync(tenantId, EntityType.Rems, setKey, cancellationToken);
        return set?.Items.Where(i => !i.Deleted).Select(i => i.Id).ToHashSet() ?? new HashSet<Guid>();
    }

    /// <summary>Upserts a shared <see cref="Address"/> from the input (creating one when <paramref name="existingId"/> is null); returns the id, or null when blank.</summary>
    private async Task<Guid?> UpsertAddressAsync(Guid? existingId, RemsAddressInput input, AddressType type, CancellationToken cancellationToken)
    {
        if (!input.HasAny)
        {
            return existingId; // nothing supplied — leave the current address untouched.
        }

        if (existingId is { } id && await _addresses.GetByIdAsync(id, cancellationToken) is { } address)
        {
            ApplyAddress(address, input);
            _addresses.Update(address);
            return address.Id;
        }

        var created = NewAddress(input, type);
        await _addresses.AddAsync(created, cancellationToken);
        return created.Id;
    }

    /// <summary>Upserts (or, when blank, removes) an entity address of a given type from a supplied input.</summary>
    private async Task UpsertEntityAddressAsync(REMSEntity entity, RemsAddressType type, RemsAddressInput? input, CancellationToken cancellationToken)
    {
        if (input is null)
        {
            return; // not supplied — leave unchanged.
        }

        var existing = entity.Addresses.FirstOrDefault(a => !a.Deleted && a.AddressType == type);
        if (!input.HasAny)
        {
            if (existing is not null)
            {
                _clients.RemoveEntityAddress(existing);
            }
            return;
        }

        if (existing?.Address is { } address)
        {
            ApplyAddress(address, input);
            _addresses.Update(address);
        }
        else
        {
            var created = NewAddress(input, AddressTypeFor(type));
            await _addresses.AddAsync(created, cancellationToken);
            await _clients.AddEntityAddressAsync(new REMSEntityAddress
            {
                Id = Guid.NewGuid(),
                REMSEntityId = entity.Id,
                AddressId = created.Id,
                AddressType = type,
            }, cancellationToken);
        }
    }

    /// <summary>
    /// Clones a source <see cref="Address"/> onto a target entity's address slot (upsert by type) —
    /// used by copy-from.
    /// </summary>
    private async Task UpsertEntityAddressFromAsync(REMSEntity entity, RemsAddressType type, Address source, CancellationToken cancellationToken)
    {
        var existing = entity.Addresses.FirstOrDefault(a => !a.Deleted && a.AddressType == type);
        if (existing?.Address is { } address)
        {
            address.AddressLine1 = source.AddressLine1;
            address.CityName = source.CityName;
            address.StateName = source.StateName;
            address.PostalCode = source.PostalCode;
            _addresses.Update(address);
        }
        else
        {
            var created = new Address
            {
                Id = Guid.NewGuid(),
                AddressType = AddressTypeFor(type),
                AddressLine1 = source.AddressLine1,
                CityName = source.CityName,
                StateName = source.StateName,
                PostalCode = source.PostalCode,
            };
            await _addresses.AddAsync(created, cancellationToken);
            await _clients.AddEntityAddressAsync(new REMSEntityAddress
            {
                Id = Guid.NewGuid(),
                REMSEntityId = entity.Id,
                AddressId = created.Id,
                AddressType = type,
            }, cancellationToken);
        }
    }

    private static Address NewAddress(RemsAddressInput input, AddressType type)
    {
        var address = new Address { Id = Guid.NewGuid(), AddressType = type };
        ApplyAddress(address, input);
        return address;
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

    private static AddressType AddressTypeFor(RemsAddressType type)
        => type == RemsAddressType.Physical ? AddressType.Office : AddressType.Other;

    /// <param name="sourceRemsId">
    /// The request whose engagement setup captured this contact, recorded as the person's provenance —
    /// otherwise they are indistinguishable in the Person list from somebody onboarded deliberately.
    /// </param>
    private static Person NewContactPerson(RemsEntityContactInput input, Guid sourceRemsId)
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            PersonCode = "PER-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
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

    private IActionResult CopyInvalid(string message)
        => StatusCode(StatusCodes.Status409Conflict, ApiResponseFactory.Error(CodeCopyInvalid, message, message));

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
