using System.Globalization;
using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.RateLimiting;
using EmsPortal.Api.Rems;
using EmsPortal.Api.Validators.Rems;
using EmsPortal.Application.Abstractions.Email;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.OptionSets;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Configuration;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// The public, unauthenticated REMS client onboarding form (WO-113). There is no form row and no stored
/// payload: the invite lives on the request, and what the client types is written straight onto the
/// client master through <see cref="RemsClientIntake"/>.
/// </summary>
[ApiController]
[Route("api/rems/public/forms")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.PublicFormRead)]
[Produces("application/json")]
[Tags("REMS Public Form")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class RemsPublicFormController : ControllerBase
{
    private const string CodeNotEditable = "REMS_FORM_NOT_EDITABLE";

    private static readonly RemsFormPayloadValidator PayloadValidator = new();

    private readonly IRemsRequestRepository _requests;
    private readonly RemsClientIntake _intake;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;
    private readonly IActivityEventWriter _activity;
    private readonly INotificationDispatcher _notifications;
    private readonly IRemsEmailNotifier _emailNotifier;
    private readonly string _baseUrl;
    private readonly IOptionSetRepository _optionSets;
    private readonly IOptionCodeResolver _codes;
    private readonly ILogger<RemsPublicFormController> _logger;
    private readonly IMaconomyClientDirectory _maconomy;

    /// <summary>The address this request's form is locked to, worked out once by <see cref="LoadFormAsync"/>.</summary>
    private string _lockedEmail = string.Empty;

    public RemsPublicFormController(
        IRemsRequestRepository requests,
        RemsClientIntake intake,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        IActivityEventWriter activity,
        INotificationDispatcher notifications,
        IRemsEmailNotifier emailNotifier,
        IOptions<AppOptions> appOptions,
        IOptionSetRepository optionSets,
        IOptionCodeResolver codes,
        ILogger<RemsPublicFormController> logger,
        IMaconomyClientDirectory maconomy)
    {
        _requests = requests;
        _intake = intake;
        _users = users;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _activity = activity;
        _notifications = notifications;
        _emailNotifier = emailNotifier;
        _baseUrl = appOptions.Value.BaseUrl;
        _optionSets = optionSets;
        _codes = codes;
        _logger = logger;
        _maconomy = maconomy;
    }

    // -------------------- Load --------------------

    /// <summary>
    /// Resolve the public form and return its load state (WO-113): <c>Invalid</c> (bad link),
    /// <c>Unavailable</c> (request deleted, or not yet sent).
    /// </summary>
    [HttpGet("{inviteCode}")]
    [ProducesResponseType<ApiResponse<RemsPublicFormResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Load(string inviteCode, CancellationToken cancellationToken)
    {
        var request = await LoadFormAsync(inviteCode, cancellationToken);
        if (request is null)
        {
            return Ok(ApiResponseFactory.Success(new RemsPublicFormResponse(RemsPublicFormStates.Invalid), "REMS form resolved."));
        }

        if (IsUnavailable(request))
        {
            return Ok(ApiResponseFactory.Success(new RemsPublicFormResponse(RemsPublicFormStates.Unavailable), "REMS form resolved."));
        }

        var client = request.Client!;
        if (RemsDerived.IsFormSubmitted(request))
        {
            return Ok(ApiResponseFactory.Success(
                new RemsPublicFormResponse(RemsPublicFormStates.Submitted, ClientName: client.ClientName),
                "REMS form resolved."));
        }

        // Saved: built but not yet sent — the link is not active until the Admin sends it.
        if (!IsEditable(request))
        {
            return Ok(ApiResponseFactory.Success(new RemsPublicFormResponse(RemsPublicFormStates.Unavailable), "REMS form resolved."));
        }

        var entityType = EntityTypeOf(request)!;
        return Ok(ApiResponseFactory.Success(
            new RemsPublicFormResponse(
                RemsPublicFormStates.Editable,
                EntityType: entityType,
                Prefill: BuildPrefill(client, entityType),
                DraftPayload: DraftPayload(request, entityType),
                ReferralSources: await ResolvePublicOptionsAsync(request.TenantId, RemsOptionSetKeys.ReferralSource, cancellationToken)),
            "REMS form resolved."));
    }

    /// <summary>
    /// Resolves an option list for the anonymous form, scoped to the REQUEST's tenant rather than to
    /// an ambient one — this caller holds an invite code, not a session.
    /// </summary>
    private async Task<IReadOnlyList<RemsPublicOption>?> ResolvePublicOptionsAsync(
        Guid tenantId, string key, CancellationToken cancellationToken)
    {
        var set = await _optionSets.GetEffectiveSetAsync(tenantId, EntityType.Rems, key, cancellationToken);
        if (set is null)
        {
            return null;
        }

        var ordered = set.ItemSortMode switch
        {
            OptionItemSortMode.AlphabeticalAsc => set.Items.OrderBy(i => i.Label),
            OptionItemSortMode.AlphabeticalDesc => set.Items.OrderByDescending(i => i.Label),
            _ => set.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Label),
        };

        var options = ordered
            .Where(i => i.IsActive && !i.Deleted)
            .Select(i => new RemsPublicOption(i.Value, i.Label, i.Description))
            .ToList();
        return options.Count > 0 ? options : null;
    }

    // -------------------- Draft (auto-save / explicit save) --------------------

    /// <summary>Save the in-progress draft (WO-113): the client rows are the draft, there is no copy.</summary>
    [HttpPut("{inviteCode}/draft")]
    [ProducesResponseType<ApiResponse<RemsDraftSavedResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveDraft(string inviteCode, [FromBody] RemsFormPayloadV1 payload, CancellationToken cancellationToken)
    {
        var request = await LoadFormAsync(inviteCode, cancellationToken);
        if (request is null)
        {
            return FormNotFound();
        }
        if (!IsEditable(request))
        {
            return NotEditable();
        }

        var now = DateTime.UtcNow;
        await _intake.ApplyAsync(request, payload, EntityTypeOf(request)!, cancellationToken);
        request.FormLastSavedOnUtc = now;
        _requests.Update(request);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new RemsDraftSavedResponse(now), "Draft saved."));
    }

    // -------------------- Review --------------------

    /// <summary>
    /// Validate the supplied (or saved) payload against the full entity-type rules and return the
    /// read-only review presentation.
    /// </summary>
    [HttpPost("{inviteCode}/review")]
    [EnableRateLimiting(RateLimitPolicies.PublicFormWrite)]
    [ProducesResponseType<ApiResponse<RemsReviewModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Review(
        string inviteCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RemsFormPayloadV1? payload,
        CancellationToken cancellationToken)
    {
        var request = await LoadFormAsync(inviteCode, cancellationToken);
        if (request is null)
        {
            return FormNotFound();
        }
        if (!IsEditable(request))
        {
            return NotEditable();
        }

        var entityType = EntityTypeOf(request)!;
        var effective = ResolvePayload(payload, request, entityType);
        var validation = PayloadValidator.Validate(effective, entityType);
        if (!validation.IsValid)
        {
            return BadRequest(ApiResponseFactory.ValidationError(validation.Errors));
        }

        var model = BuildReviewModel(effective!, entityType);
        return Ok(ApiResponseFactory.Success(model, "REMS form review ready."));
    }

    // -------------------- Submit --------------------

    /// <summary>Transactionally submit the form (WO-113).</summary>
    [HttpPost("{inviteCode}/submit")]
    [EnableRateLimiting(RateLimitPolicies.PublicFormWrite)]
    [ProducesResponseType<ApiResponse<RemsPublicFormResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(
        string inviteCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RemsFormPayloadV1? payload,
        CancellationToken cancellationToken)
    {
        var request = await LoadFormAsync(inviteCode, cancellationToken);
        if (request is null)
        {
            return FormNotFound();
        }

        // Idempotency: an already-submitted form returns the thank-you state — never a duplicate, never a reset.
        if (RemsDerived.IsFormSubmitted(request))
        {
            return Ok(ApiResponseFactory.Success(
                new RemsPublicFormResponse(RemsPublicFormStates.Submitted, ClientName: request.Client?.ClientName),
                "REMS form already submitted."));
        }
        if (!IsEditable(request))
        {
            return NotEditable();
        }

        var entityType = EntityTypeOf(request)!;
        var effective = ResolvePayload(payload, request, entityType);

        // Re-validate everything server-side (AC-REMS-024.8) before any write.
        var validation = PayloadValidator.Validate(effective, entityType);
        if (!validation.IsValid)
        {
            return BadRequest(ApiResponseFactory.ValidationError(validation.Errors));
        }

        await SubmitTransactionAsync(request, effective!, entityType, cancellationToken);
        await DispatchPostSubmitAsync(request, effective!, cancellationToken);

        return Ok(ApiResponseFactory.Success(
            new RemsPublicFormResponse(
                RemsPublicFormStates.Submitted,
                ClientName: NullIfBlank(effective!.EffectiveClientName) ?? request.Client!.ClientName),
            "REMS form submitted."));
    }

    // -------------------- Cancel --------------------

    /// <summary>
    /// Acknowledge a client's cancellation of the form (AC-REMS-010.9) — a client-side confirmation
    /// only.
    /// </summary>
    [HttpPost("{inviteCode}/cancel")]
    [EnableRateLimiting(RateLimitPolicies.PublicFormWrite)]
    [ProducesResponseType<ApiResponse<RemsPublicCancelResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(string inviteCode, CancellationToken cancellationToken)
    {
        var request = await LoadFormAsync(inviteCode, cancellationToken);
        if (request is null)
        {
            return FormNotFound();
        }

        // Intentionally non-destructive: no state change, nothing cleared — the client may resume later.
        return Ok(ApiResponseFactory.Success(new RemsPublicCancelResponse(true), "Cancellation acknowledged."));
    }

    // -------------------- Submit transaction --------------------

    private async Task SubmitTransactionAsync(
        RemsRequest request, RemsFormPayloadV1 payload, string entityType, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // The client's answers are in, so the request passes to the Admin the initiator named. The engagement
        // setup was filled before any of this, so what happens next is review, not setup.
        var submittedStatusId = await _codes.RequireRemsIdAsync(
            RemsOptionSetKeys.Status, RemsRequestStatuses.AdminReview, cancellationToken);

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // The answers go onto the client master; the request only records when they arrived.
            await _intake.ApplyAsync(request, payload, entityType, ct);
            request.FormSubmittedOnUtc = now;
            request.FormLastSavedOnUtc = now;

            var previousStatusId = request.StatusId;
            request.StatusId = submittedStatusId;
            _requests.Update(request);
            await _requests.AddStatusHistoryAsync(new RemsStatusHistory
            {
                Id = Guid.NewGuid(),
                TenantId = request.TenantId, // EXPLICIT — no tenant stamping in the public context.
                REMSId = request.Id,
                FromStatusId = previousStatusId,
                ToStatusId = submittedStatusId,
                ChangedByUserId = null, // the client is not a user
                ChangedOnUtc = now,
            }, ct);

            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    // -------------------- Post-commit (best-effort) --------------------

    /// <summary>
    /// After the submission is durably committed, notify the assigned Admin and CSE in-app and by
    /// email (AC-REMS ...).
    /// </summary>
    private async Task DispatchPostSubmitAsync(RemsRequest request, RemsFormPayloadV1 payload, CancellationToken cancellationToken)
    {
        var client = request.Client!;
        // Assigned admin, CSE, and the requester — the customer coming back is the milestone the person who
        // raised the request is waiting on.
        var recipients = new[] { request.AdminAssignedToId, request.CSEId, request.CreatedById }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        try
        {
            foreach (var userId in recipients)
            {
                await _notifications.DispatchAsync(new CreateNotificationDto(
                    userId,
                    NotificationType.RemsFormSubmitted,
                    "A REMS onboarding form was submitted",
                    $"{request.RemsNumber} — {client.ClientName}",
                    EntityType.Rems,
                    request.Id), cancellationToken);
            }

            // Nobody has claimed this request, so the answers that just landed are on no admin's desk in
            // particular.
            if (request.AdminAssignedToId is null)
            {
                var admins = await _users.ListByTenantRolesAsync(
                    request.TenantId, new[] { Roles.Admin, Roles.SuperAdmin }, cancellationToken);
                foreach (var admin in admins.Where(a => !recipients.Contains(a.Id)))
                {
                    await _notifications.DispatchAsync(new CreateNotificationDto(
                        admin.Id,
                        NotificationType.RemsRequestAssigned,
                        "A REMS request is waiting for pickup",
                        $"{request.RemsNumber} — {client.ClientName}",
                        EntityType.Rems,
                        request.Id), cancellationToken);
                }
            }

            await _activity.WriteAsync(
                new CreateActivityEventDto(EntityType.Rems, request.Id, ActivityEventTypes.RemsFormSubmitted), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // External "form submitted" email to the Admin + CSE. Enqueued on a worker; never throws/blocks.
            var model = new RemsFormSubmittedEmail(
                Clean(payload.EffectiveClientName) ?? client.ClientName,
                request.RemsNumber,
                $"{_baseUrl.TrimEnd('/')}/rems/requests/{request.Id}",
                (request.FormSubmittedOnUtc ?? DateTime.UtcNow).ToString("f", CultureInfo.InvariantCulture) + " UTC");

            foreach (var userId in recipients)
            {
                var user = await _users.GetByIdAsync(userId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(user?.Email))
                {
                    _emailNotifier.SendFormSubmitted(request.TenantId, user!.Email, model);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "The form on REMS request {RemsId} was submitted, but post-commit notification/email dispatch failed.", request.Id);
        }
    }

    // -------------------- Review model --------------------

    private RemsReviewModel BuildReviewModel(RemsFormPayloadV1 payload, string entityType)
    {
        var contact = new RemsReviewContact(
            Clean(payload.EffectiveClientName), Clean(payload.ClientSuffix),
            Clean(payload.ClientFirstName), Clean(payload.ClientLastName),
            _lockedEmail, Clean(payload.MobileNumber), Clean(payload.ReferralSource));

        var contract = string.Equals(entityType, RemsFormPayloadValidator.Government, StringComparison.Ordinal)
            ? new RemsReviewContractDetails(
                payload.ContractStartDate, payload.ContractEndDate, Clean(payload.OriginalTerm),
                Clean(payload.RenewalTerms), payload.PoStartDate, payload.PoEndDate)
            : null;

        var others = payload.RelatedEntities
            .Select(r => new RemsReviewOtherEntity(
                Clean(r.SourceKey), Clean(r.FullName), Clean(r.EmailAddress), Clean(r.PhoneNumber)))
            .ToList();

        // Every address is shown as it will be recorded, which for the mailing one means the physical address
        // wherever the client ticked "same as physical" — the flag decides which node is the answer.
        var address = new RemsReviewAddressGroup(
            NonEmpty(payload.PhysicalAddress), NonEmpty(payload.EffectiveMailingAddress),
            payload.EffectiveBillingAddresses);

        RemsReviewContactRow Row(RemsRolePayload role, string roleName, bool isRequired)
            => new(
                roleName, isRequired, Clean(role.Prefix), Clean(role.Suffix),
                Clean(role.EffectiveFirstName), Clean(role.EffectiveLastName), Clean(role.DisplayName),
                Clean(role.Email), Clean(role.Phone));

        var additionalContacts = EnumerateRoles(entityType, payload.EffectiveRoles)
            .Where(t => t.Role is { HasAny: true })
            .Select(t => Row(t.Role!, t.RoleName, t.IsRequired))
            .ToList();

        // The extra billing contacts an older payload carries. Retired with the Billing Contact block,
        // and never required.
        var extraBilling = payload.AdditionalBillingContacts
            .Where(r => r is { HasAny: true })
            .Select(r => Row(r, nameof(RemsContactRole.BillingContact), isRequired: false))
            .ToList();

        // Retired, and populated only where an older payload carries it: the billing addresses above are
        // where a form filled in today puts these answers.
        var billing = new RemsReviewBilling(
            Clean(payload.BillingContactName), Clean(payload.BillingEmail), extraBilling);

        // The other people on an individual's return, with the firm's rules already applied — review
        // shows what will be recorded, not what the boxes happened to hold.
        var individuals = string.Equals(entityType, RemsFormPayloadValidator.Individual, StringComparison.Ordinal)
            ? payload.AdditionalIndividuals
                .Where(x => x is { HasAny: true })
                .Select(x => new RemsReviewIndividual(
                    Clean(x.SourceKey), Clean(x.Type), x.EffectiveFilingType,
                    Clean(x.FirstName), Clean(x.LastName), Clean(x.Suffix), Clean(x.DisplayName),
                    Clean(x.Email), Clean(x.Phone), x.EffectiveIsMinor, x.EffectiveBillingPreference,
                    x.AsksBillingName ? Clean(x.BillingFirstName) : null,
                    x.AsksBillingName ? Clean(x.BillingLastName) : null))
                .ToList()
            : new List<RemsReviewIndividual>();

        return new RemsReviewModel(
            contact, contract, others, address, additionalContacts, billing, individuals);
    }

    // -------------------- Helpers --------------------

    /// <summary>
    /// Loads the request by invite code (unscoped, with the client's whole graph) and, once resolved, pins
    /// the tenant context to its tenant and works out the email the form is locked to.
    /// </summary>
    private async Task<RemsRequest?> LoadFormAsync(string inviteCode, CancellationToken cancellationToken)
    {
        var request = await _requests.GetByInviteCodeUnscopedAsync(inviteCode?.Trim() ?? string.Empty, cancellationToken);
        if (request is null)
        {
            return null;
        }

        if (request.TenantId != Guid.Empty)
        {
            // Establish the resolved tenant so DbContext stamping and the notification/activity writers behave
            // exactly as they do for an authenticated request. Every write also sets TenantId explicitly.
            _tenantContext.Set(request.TenantId, string.Empty);
        }

        var client = request.Client;
        // No REMS controller ran first here, so a Maconomy customer's card is fetched explicitly.
        await _maconomy.HydrateAsync(client, cancellationToken);
        _lockedEmail = NullIfBlank(request.InviteRecipientEmail) ?? NullIfBlank(client?.ClientEmail) ?? string.Empty;
        if (_lockedEmail.Length == 0 && client is { IsMaconomyClient: true })
        {
            // Maconomy cannot be read: the address the form was last sent to is the one it is locked to.
            _lockedEmail = (await _requests.ListEmailEventsAsync(request.Id, cancellationToken))
                .Where(e => e.EventType is RemsFormEmailEventType.Sent or RemsFormEmailEventType.Reminder
                    && !string.IsNullOrWhiteSpace(e.RecipientEmail))
                .OrderByDescending(e => e.OccurredOnUtc)
                .Select(e => e.RecipientEmail)
                .FirstOrDefault() ?? string.Empty;
        }

        return request;
    }

    /// <summary>The locked prefill for the editable form.</summary>
    private RemsPublicPrefill BuildPrefill(Client client, string entityType)
    {
        if (client.IsMaconomyClient)
        {
            // Maconomy holds one name string. For a person it is nearly always "LAST, FIRST", so that is
            // offered split — a starting point the client corrects, not an answer.
            var whole = client.Maconomy?.Name.Trim() ?? string.Empty;
            var comma = whole.IndexOf(',');
            var individual = RemsClientEntityType.IsIndividual(entityType);
            return new RemsPublicPrefill(
                whole,
                individual && comma > 0 ? whole[(comma + 1)..].Trim() : string.Empty,
                individual && comma > 0 ? whole[..comma].Trim() : string.Empty,
                null, _lockedEmail, client.ClientPhone);
        }

        // STRAIGHT OFF THE CLIENT'S OWN RECORD, no splitting.
        var person = client.Person;
        var first = client.IsOrganisation ? string.Empty : person?.FirstName ?? string.Empty;
        var last = client.IsOrganisation ? string.Empty : person?.LastName ?? string.Empty;
        var name = client.IsOrganisation
            ? client.LegalName?.Trim() ?? string.Empty
            : string.Join(" ", new[] { first, last }.Where(p => p.Length > 0));
        return new RemsPublicPrefill(
            name, first, last, client.IsOrganisation ? null : person?.Suffix,
            _lockedEmail, client.ClientPhone);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The entity type the form is filled in under: the client's, locked when the request named it.</summary>
    private static string? EntityTypeOf(RemsRequest request) => NullIfBlank(request.Client?.EntityType?.Value);

    /// <summary>The link is dead when the request is gone/deleted or has no client to write to.</summary>
    private static bool IsUnavailable(RemsRequest request)
        => request.Deleted || request.Client is null;

    /// <summary>
    /// Editable == sent-and-live under a known entity type: the only state that accepts draft saves, review
    /// and submit.
    /// </summary>
    private static bool IsEditable(RemsRequest request)
        => !IsUnavailable(request)
            && RemsDerived.FormStatusOf(request) == RemsFormStatus.Sent
            && EntityTypeOf(request) is not null;

    /// <summary>
    /// The in-progress draft, read back off the client rows: there is one once a save has written them, or
    /// once anything is on file for the client at all. Null until then, so a fresh form opens empty.
    /// </summary>
    private RemsFormPayloadV1? DraftPayload(RemsRequest request, string entityType)
        => request.FormLastSavedOnUtc is not null || RemsClientIntake.HasFormContent(request.Client!)
            ? RemsClientIntake.ToPayload(request, entityType, _lockedEmail)
            : null;

    /// <summary>The payload to act on: the request body when supplied, else the saved draft.</summary>
    private RemsFormPayloadV1? ResolvePayload(RemsFormPayloadV1? body, RemsRequest request, string entityType)
        => body ?? DraftPayload(request, entityType);

    /// <summary>The relevant (role, canonical role name, required?) tuples for an industry group.</summary>
    private static IEnumerable<(RemsRolePayload? Role, string RoleName, bool IsRequired)> EnumerateRoles(
        string entityType, RemsRolesPayload roles)
    {
        // Mirrors RemsFormPayloadValidator's branches — the roles it REQUIRES are shown as required here.
        // if/else rather than a switch because the business branch matches a family of codes.
        if (entityType == RemsFormPayloadValidator.Individual)
        {
            yield return (roles.Self, nameof(RemsContactRole.Self), true);
            yield return (roles.Spouse, nameof(RemsContactRole.Spouse), false);
            yield return (roles.BillingContact, nameof(RemsContactRole.BillingContact), false);   // retired
        }
        else if (RemsFormPayloadValidator.IsBusinessGroup(entityType))
        {
            yield return (roles.PrimaryContact, nameof(RemsContactRole.PrimaryClientContact), true);
            // Owed by every business but a commercial one, which may give a financial contact or not.
            yield return (roles.FinancialContact, nameof(RemsContactRole.FinancialContact),
                entityType != RemsFormPayloadValidator.Commercial);
            // Trust and Estate only. Yielded for the whole family — a null role shows nothing, and an
            // answer given before the entity type changed is still an answer (as with the retired roles).
            yield return (roles.TrustEstateContact, nameof(RemsContactRole.TrustEstateContact), false);
            yield return (roles.OtherContact, nameof(RemsContactRole.OtherContact), false);
            yield return (roles.BillingContact, nameof(RemsContactRole.BillingContact), false);   // retired
            yield return (roles.Banker, nameof(RemsContactRole.Banker), false);                   // retired
            yield return (roles.Lawyer, nameof(RemsContactRole.Lawyer), false);                   // retired
        }
        else if (entityType == RemsFormPayloadValidator.Government)
        {
            // Neither is owed; a government body gives whichever it has.
            yield return (roles.FinanceDirector, nameof(RemsContactRole.FinanceDirector), false);
            yield return (roles.OtherContact, nameof(RemsContactRole.OtherContact), false);
            yield return (roles.BillingContact, nameof(RemsContactRole.BillingContact), false);   // retired
        }
    }

    private static RemsAddressPayload? NonEmpty(RemsAddressPayload? address) => address is { HasAny: true } ? address : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private IActionResult FormNotFound()
        => NotFound(ApiResponseFactory.NotFound("This form is not available."));

    private IActionResult NotEditable()
        => StatusCode(StatusCodes.Status409Conflict, ApiResponseFactory.Error(
            CodeNotEditable, "This form is not available.", "The form is not currently accepting changes."));
}
