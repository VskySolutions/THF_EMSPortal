using System.Globalization;
using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.Validators.Rems;
using EmsPortal.Application.Abstractions.Email;
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
using Microsoft.Extensions.Options;

namespace EmsPortal.Api.Controllers;

/// <summary>The public, unauthenticated REMS client onboarding form (WO-113).</summary>
[ApiController]
[Route("api/rems/public/forms")]
[AllowAnonymous]
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

    private readonly IRemsFormRepository _forms;
    private readonly IRemsRepository _rems;
    private readonly IRemsClientRepository _clients;
    private readonly IRemsEngagementRepository _engagements;
    private readonly IAddressRepository _addresses;
    private readonly IPersonRepository _persons;
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

    public RemsPublicFormController(
        IRemsFormRepository forms,
        IRemsRepository rems,
        IRemsClientRepository clients,
        IRemsEngagementRepository engagements,
        IAddressRepository addresses,
        IPersonRepository persons,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        IActivityEventWriter activity,
        INotificationDispatcher notifications,
        IRemsEmailNotifier emailNotifier,
        IOptions<AppOptions> appOptions,
        IOptionSetRepository optionSets,
        IOptionCodeResolver codes,
        ILogger<RemsPublicFormController> logger)
    {
        _forms = forms;
        _rems = rems;
        _clients = clients;
        _engagements = engagements;
        _addresses = addresses;
        _persons = persons;
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
    }

    // -------------------- Load --------------------

    /// <summary>
    /// Resolve the public form and return its load state (WO-113): <c>Invalid</c> (bad link),
    /// <c>Unavailable</c> (request deleted, cancelled, or not yet sent).
    /// </summary>
    [HttpGet("{inviteCode}")]
    [ProducesResponseType<ApiResponse<RemsPublicFormResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Load(string inviteCode, CancellationToken cancellationToken)
    {
        var form = await LoadFormAsync(inviteCode, cancellationToken);
        if (form is null)
        {
            return Ok(ApiResponseFactory.Success(new RemsPublicFormResponse(RemsPublicFormStates.Invalid), "REMS form resolved."));
        }

        var rems = form.Rems!;
        if (IsUnavailable(form))
        {
            return Ok(ApiResponseFactory.Success(new RemsPublicFormResponse(RemsPublicFormStates.Unavailable), "REMS form resolved."));
        }

        return form.Status switch
        {
            RemsFormStatus.Submitted => Ok(ApiResponseFactory.Success(
                new RemsPublicFormResponse(RemsPublicFormStates.Submitted, ClientName: rems.ClientDisplayName),
                "REMS form resolved.")),

            RemsFormStatus.Sent => Ok(ApiResponseFactory.Success(
                new RemsPublicFormResponse(
                    RemsPublicFormStates.Editable,
                    EntityType: form.EntityType!.Value,
                    Prefill: BuildPrefill(rems),
                    DraftPayload: RemsFormPayloadJson.TryDeserialize(CurrentDraft(form)?.DraftPayload),
                    ReferralSources: await ResolvePublicOptionsAsync(rems.TenantId, RemsOptionSetKeys.ReferralSource, cancellationToken)),
                "REMS form resolved.")),

            // Draft / Saved: built but not yet sent — the link is not active until the Admin sends it.
            _ => Ok(ApiResponseFactory.Success(new RemsPublicFormResponse(RemsPublicFormStates.Unavailable), "REMS form resolved.")),
        };
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

    /// <summary>Upsert the single in-progress draft (WO-113).</summary>
    [HttpPut("{inviteCode}/draft")]
    [ProducesResponseType<ApiResponse<RemsDraftSavedResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveDraft(string inviteCode, [FromBody] RemsFormPayloadV1 payload, CancellationToken cancellationToken)
    {
        var form = await LoadFormAsync(inviteCode, cancellationToken);
        if (form is null)
        {
            return FormNotFound();
        }
        if (!IsEditable(form))
        {
            return NotEditable();
        }

        var now = DateTime.UtcNow;
        var json = RemsFormPayloadJson.Serialize(payload);
        var draft = CurrentDraft(form);
        if (draft is null)
        {
            draft = new REMSFormDraft
            {
                Id = Guid.NewGuid(),
                TenantId = form.TenantId, // EXPLICIT — no tenant stamping in the public context.
                REMSFormId = form.Id,
                DraftPayload = json,
                LastSavedOnUtc = now,
            };
            await _forms.AddDraftAsync(draft, cancellationToken);
        }
        else
        {
            draft.DraftPayload = json;
            draft.LastSavedOnUtc = now;
            _forms.UpdateDraft(draft);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new RemsDraftSavedResponse(now), "Draft saved."));
    }

    // -------------------- Review --------------------

    /// <summary>
    /// Validate the supplied (or stored) payload against the full entity-type rules and return the
    /// read-only review presentation.
    /// </summary>
    [HttpPost("{inviteCode}/review")]
    [ProducesResponseType<ApiResponse<RemsReviewModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Review(
        string inviteCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RemsFormPayloadV1? payload,
        CancellationToken cancellationToken)
    {
        var form = await LoadFormAsync(inviteCode, cancellationToken);
        if (form is null)
        {
            return FormNotFound();
        }
        if (!IsEditable(form))
        {
            return NotEditable();
        }

        var effective = ResolvePayload(payload, form);
        var validation = PayloadValidator.Validate(effective, form.EntityType!.Value);
        if (!validation.IsValid)
        {
            return BadRequest(ApiResponseFactory.ValidationError(validation.Errors));
        }

        var model = BuildReviewModel(form.Rems!, effective!, form.EntityType!.Value);
        return Ok(ApiResponseFactory.Success(model, "REMS form review ready."));
    }

    // -------------------- Submit --------------------

    /// <summary>Transactionally submit the form (WO-113).</summary>
    [HttpPost("{inviteCode}/submit")]
    [ProducesResponseType<ApiResponse<RemsPublicFormResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(
        string inviteCode,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RemsFormPayloadV1? payload,
        CancellationToken cancellationToken)
    {
        var form = await LoadFormAsync(inviteCode, cancellationToken);
        if (form is null)
        {
            return FormNotFound();
        }

        var rems = form.Rems!;

        // Idempotency: an already-submitted form returns the thank-you state — never a duplicate, never a reset.
        if (form.Status == RemsFormStatus.Submitted)
        {
            return Ok(ApiResponseFactory.Success(
                new RemsPublicFormResponse(RemsPublicFormStates.Submitted, ClientName: rems.ClientDisplayName),
                "REMS form already submitted."));
        }
        if (!IsEditable(form))
        {
            return NotEditable();
        }

        var effective = ResolvePayload(payload, form);

        // Re-validate everything server-side (AC-REMS-024.8) before any write.
        var validation = PayloadValidator.Validate(effective, form.EntityType!.Value);
        if (!validation.IsValid)
        {
            return BadRequest(ApiResponseFactory.ValidationError(validation.Errors));
        }

        await SubmitTransactionAsync(form, effective!, cancellationToken);
        await DispatchPostSubmitAsync(form, effective!, cancellationToken);

        return Ok(ApiResponseFactory.Success(
            new RemsPublicFormResponse(
                RemsPublicFormStates.Submitted,
                ClientName: NullIfBlank(effective!.EffectiveClientName) ?? rems.ClientDisplayName),
            "REMS form submitted."));
    }

    // -------------------- Cancel --------------------

    /// <summary>
    /// Acknowledge a client's cancellation of the form (AC-REMS-010.9) — a client-side confirmation
    /// only.
    /// </summary>
    [HttpPost("{inviteCode}/cancel")]
    [ProducesResponseType<ApiResponse<RemsPublicCancelResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(string inviteCode, CancellationToken cancellationToken)
    {
        var form = await LoadFormAsync(inviteCode, cancellationToken);
        if (form is null)
        {
            return FormNotFound();
        }

        // Intentionally non-destructive: no state change, no draft deletion — the client may resume later.
        return Ok(ApiResponseFactory.Success(new RemsPublicCancelResponse(true), "Cancellation acknowledged."));
    }

    // -------------------- Submit transaction --------------------

    private async Task SubmitTransactionAsync(REMSForm form, RemsFormPayloadV1 payload, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // Build the ENTIRE object graph up front (stable, pre-generated ids).
        var engagement = await _engagements.GetByRemsIdAsync(form.REMSId, cancellationToken);
        // The referral source is a foreign key to an option item, so the CODE the client picked is resolved
        // before the graph is staged.
        var referralSourceId = await _codes.IdOfAsync(
            EntityType.Rems, RemsOptionSetKeys.ReferralSource, payload.ReferralSource, cancellationToken);
        var graph = BuildSubmitGraph(form, payload, now, engagement?.Id, referralSourceId);

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // 2. Immutable submission snapshot (the (TenantId, REMSFormId) unique index is the duplicate backstop).
            await _forms.AddSubmissionAsync(graph.Submission, ct);

            // 4-7. The materialised client aggregate. Addresses/Persons are inserted before the rows that
            // reference them so the FKs resolve within the one transaction.
            foreach (var address in graph.Addresses)
            {
                await _addresses.AddAsync(address, ct);
            }
            foreach (var person in graph.Persons)
            {
                await _persons.AddAsync(person, ct);
            }

            await _clients.AddAsync(graph.Client, ct);
            foreach (var entity in graph.Entities)
            {
                await _clients.AddEntityAsync(entity, ct);
            }
            foreach (var entityAddress in graph.EntityAddresses)
            {
                await _clients.AddEntityAddressAsync(entityAddress, ct);
            }
            foreach (var contact in graph.Contacts)
            {
                await _clients.AddEntityContactAsync(contact, ct);
            }
            foreach (var additional in graph.AdditionalEntities)
            {
                await _rems.AddAdditionalEntityAsync(additional, ct);
            }
            // After the entity and the Persons above, both of which these rows point at.
            foreach (var individual in graph.AdditionalIndividuals)
            {
                await _rems.AddAdditionalIndividualAsync(individual, ct);
            }
            if (graph.GovernmentDetail is not null)
            {
                await _engagements.AddGovernmentDetailAsync(graph.GovernmentDetail, ct);
            }

            // 3.
            form.Status = RemsFormStatus.Submitted;
            form.SubmittedOnUtc = now;
            form.InviteLockedOnUtc ??= now;
            // The client's answers are in, so the request passes to the Admin the initiator named. The
            // engagement setup was filled before any of this, so what happens next is review, not setup.
            form.Rems!.StatusId = await _codes.RequireRemsIdAsync(
                RemsOptionSetKeys.Status, RemsRequestStatuses.AdminReview, ct);

            // 8. Commit.
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    /// <summary>
    /// Materialises the whole submit graph (submission, client, entities, addresses, contacts,
    /// engagements.
    /// </summary>
    private SubmitGraph BuildSubmitGraph(
        REMSForm form, RemsFormPayloadV1 payload, DateTime now, Guid? engagementId, Guid? referralSourceId)
    {
        var tenantId = form.TenantId;
        var isBusiness = RemsFormPayloadValidator.IsBusinessGroup(form.EntityType!.Value);
        var isGovernment = string.Equals(form.EntityType!.Value, RemsFormPayloadValidator.Government, StringComparison.Ordinal);

        var submissionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var graph = new SubmitGraph
        {
            Submission = new REMSFormSubmission
            {
                Id = submissionId,
                TenantId = tenantId,
                REMSFormId = form.Id,
                SubmittedPayload = RemsFormPayloadJson.Serialize(payload),
                SubmittedOnUtc = now,
            },
        };

        // Billing ADDRESSES are not staged here — they are the main entity's, and there may be several (see
        // StageEntityAddresses).
        graph.Client = new REMSClient
        {
            Id = clientId,
            TenantId = tenantId,
            REMSId = form.REMSId,
            SourceFormSubmissionId = submissionId,
            // EffectiveClientName, not ClientName: an individual gives their name in two boxes now, and
            // the joined echo beside them is the client's to send rather than ours to trust.
            Name = Clean(payload.EffectiveClientName) ?? string.Empty,
            Email = form.Rems!.CustomerEmail ?? string.Empty, // LOCKED to the request's customer email.
            MobileNumber = Clean(payload.MobileNumber),
            ReferralSourceId = referralSourceId,
            ReferralSourceDetail = Clean(payload.ReferralSourceDetail),
            BillingContactName = Clean(payload.BillingContactName),
            BillingEmail = Clean(payload.BillingEmail),
        };

        // Main entity + its addresses, role contacts and (Government) contract detail.
        var mainEntityId = Guid.NewGuid();
        graph.Entities.Add(new REMSEntity
        {
            Id = mainEntityId,
            TenantId = tenantId,
            REMSClientId = clientId,
            Name = Clean(payload.EffectiveClientName) ?? string.Empty,
            EIN = isBusiness ? Clean(payload.Ein) : null,
            IsMainEntity = true,
        });
        StageEntityAddresses(
            graph, tenantId, mainEntityId,
            payload.PhysicalAddress, payload.EffectiveMailingAddress, payload.EffectiveBillingAddresses);
        StageRoleContacts(
            graph, tenantId, form.REMSId, form.EntityType!.Value, mainEntityId, payload.Roles,
            payload.AdditionalBillingContacts);
        // Everyone else on this client's return.
        if (string.Equals(form.EntityType!.Value, RemsFormPayloadValidator.Individual, StringComparison.Ordinal))
        {
            StageAdditionalIndividuals(graph, tenantId, form.REMSId, mainEntityId, payload.AdditionalIndividuals);
        }

        // Guarded on the engagement existing: a request written before the setup moved to the front could
        // reach here without one, and a contract detail with nothing to hang off would fail the insert.
        if (isGovernment && engagementId is { } governmentEngagementId)
        {
            graph.GovernmentDetail = new REMSEngagementGovernmentDetail
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                REMSEngagementId = governmentEngagementId,
                ContractStartDate = payload.ContractStartDate,
                ContractEndDate = payload.ContractEndDate,
                OriginalTerm = Clean(payload.OriginalTerm),
                RenewalTerms = Clean(payload.RenewalTerms),
                PurchaseOrderStartDate = payload.PoStartDate,
                PurchaseOrderEndDate = payload.PoEndDate,
            };
        }

        // The client's other businesses, as contacts on the REQUEST rather than entities under the client.
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "main" };
        var fallbackIndex = 0;
        foreach (var related in payload.RelatedEntities)
        {
            graph.AdditionalEntities.Add(new REMSAdditionalEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                REMSId = form.REMSId,
                SourceKey = UniqueEntityKey(related.SourceKey, usedKeys, ref fallbackIndex),
                FullName = Clean(related.FullName) ?? string.Empty,
                EmailAddress = Clean(related.EmailAddress),
                PhoneNumber = Clean(related.PhoneNumber),
            });
        }

        return graph;
    }

    /// <summary>Stages the entity's physical and mailing addresses and every billing address the client gave.</summary>
    private static void StageEntityAddresses(
        SubmitGraph graph, Guid tenantId, Guid entityId,
        RemsAddressPayload? physical, RemsAddressPayload? mailing,
        IReadOnlyList<RemsAddressPayload> billing)
    {
        Stage(physical is { HasAny: true }, physical, AddressType.Office, RemsAddressType.Physical);
        Stage(mailing is { HasAny: true }, mailing, AddressType.Other, RemsAddressType.Mailing);
        foreach (var row in billing)
        {
            Stage(row is { HasAnyContent: true }, row, AddressType.Billing, RemsAddressType.Billing);
        }

        void Stage(bool present, RemsAddressPayload? payload, AddressType addressType, RemsAddressType remsType)
        {
            if (!present || payload is null)
            {
                return;
            }
            var address = NewAddress(payload, addressType);
            graph.Addresses.Add(address);
            graph.EntityAddresses.Add(NewEntityAddress(tenantId, entityId, address.Id, remsType));
        }
    }

    /// <summary>
    /// Stages the other people on an individual client's return — a spouse, a child, anyone else the
    /// firm is preparing for.
    /// </summary>
    private static void StageAdditionalIndividuals(
        SubmitGraph graph, Guid tenantId, Guid sourceRemsId, Guid entityId,
        IReadOnlyList<RemsAdditionalIndividualPayload> individuals)
    {
        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fallbackIndex = 0;
        foreach (var individual in individuals)
        {
            if (individual is not { HasAny: true })
            {
                continue;
            }

            var person = new Person
            {
                Id = Guid.NewGuid(),
                PersonCode = "PER-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                TenantId = tenantId,
                SourceEntityType = EntityType.Rems,
                SourceEntityId = sourceRemsId,
                FirstName = Clean(individual.FirstName) ?? string.Empty,
                LastName = Clean(individual.LastName) ?? string.Empty,
                // The particle onto the Person's own column, not just into the display name.
                Suffix = Clean(individual.Suffix),
                DisplayName = Clean(individual.DisplayName) ?? string.Empty,
                PrimaryEmail = Clean(individual.Email),
                MobileNumber = Clean(individual.Phone),
                IsActive = true,
                LastProfileUpdatedOn = DateTime.UtcNow,
            };
            graph.Persons.Add(person);

            graph.AdditionalIndividuals.Add(new REMSAdditionalIndividual
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                REMSId = sourceRemsId,
                REMSEntityId = entityId,
                PersonId = person.Id,
                SourceKey = UniqueIndividualKey(individual.SourceKey, usedKeys, ref fallbackIndex),
                RelationType = Clean(individual.Type) ?? string.Empty,
                FilingType = individual.EffectiveFilingType,
                FirstName = person.FirstName,
                LastName = person.LastName,
                Suffix = person.Suffix,
                Email = person.PrimaryEmail,
                PhoneNumber = person.MobileNumber,
                IsMinor = individual.EffectiveIsMinor,
                BillingPreference = individual.EffectiveBillingPreference,
                // Only where the form asked. A name left behind a control the rules had since disabled is
                // an answer nobody gave.
                BillingFirstName = individual.AsksBillingName ? Clean(individual.BillingFirstName) : null,
                BillingLastName = individual.AsksBillingName ? Clean(individual.BillingLastName) : null,
            });
        }
    }

    private static void StageRoleContacts(
        SubmitGraph graph, Guid tenantId, Guid sourceRemsId, string entityType, Guid entityId,
        RemsRolesPayload? roles, IReadOnlyList<RemsRolePayload>? additionalBillingContacts = null)
    {
        if (roles is null)
        {
            return;
        }

        foreach (var (role, roleName, isRequired) in EnumerateRoles(entityType, roles.Normalized()))
        {
            Stage(role, roleName, isRequired);
        }

        // The extra billing contacts an older payload carries.
        foreach (var extra in additionalBillingContacts ?? Array.Empty<RemsRolePayload>())
        {
            Stage(extra, nameof(RemsContactRole.BillingContact), isRequired: false);
        }

        void Stage(RemsRolePayload? role, string roleName, bool isRequired)
        {
            if (role is not { HasAny: true })
            {
                return;
            }

            var person = BuildContactPerson(tenantId, sourceRemsId, role);
            graph.Persons.Add(person);
            graph.Contacts.Add(new REMSEntityContact
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                REMSEntityId = entityId,
                PersonId = person.Id,
                ContactRole = roleName,
                IsRequired = isRequired,
            });
        }
    }

    /// <summary>
    /// Satisfies the required <c>REMSEntityContact.PersonId</c> FK by creating a minimal tenant-scoped
    /// <see cref="Person"/> from the role's name / email / phone.
    /// </summary>
    private static Person BuildContactPerson(Guid tenantId, Guid sourceRemsId, RemsRolePayload role)
    {
        // The form asks for the two parts, so the Person is filed under what the client actually typed into
        // them.
        var first = role.EffectiveFirstName;
        var last = role.EffectiveLastName;
        return new Person
        {
            Id = Guid.NewGuid(),
            PersonCode = "PER-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            TenantId = tenantId,
            SourceEntityType = EntityType.Rems,
            SourceEntityId = sourceRemsId,
            // The generational particle the client gave for this contact.
            Suffix = Clean(role.Suffix),
            FirstName = first,
            LastName = last,
            DisplayName = Clean(role.NameWithSuffix) ?? first,
            PrimaryEmail = Clean(role.Email),
            MobileNumber = Clean(role.Phone),
            IsActive = true,
            LastProfileUpdatedOn = DateTime.UtcNow,
        };
    }

    // No engagement is minted on submit: the initiator fills the engagement setup before the client is ever
    // contacted.

    /// <summary>The staged submit graph — every row carries an explicit TenantId and a pre-generated id.</summary>
    private sealed class SubmitGraph
    {
        public required REMSFormSubmission Submission { get; init; }
        public REMSClient Client { get; set; } = null!;
        public List<Address> Addresses { get; } = new();
        public List<Person> Persons { get; } = new();
        public List<REMSEntity> Entities { get; } = new();
        public List<REMSEntityAddress> EntityAddresses { get; } = new();
        public List<REMSEntityContact> Contacts { get; } = new();
        public List<REMSAdditionalEntity> AdditionalEntities { get; } = new();
        public List<REMSAdditionalIndividual> AdditionalIndividuals { get; } = new();
        public REMSEngagementGovernmentDetail? GovernmentDetail { get; set; }
    }

    // -------------------- Post-commit (best-effort) --------------------

    /// <summary>
    /// After the submission is durably committed, notify the assigned Admin and CSE in-app and by
    /// email (AC-REMS ...).
    /// </summary>
    private async Task DispatchPostSubmitAsync(REMSForm form, RemsFormPayloadV1 payload, CancellationToken cancellationToken)
    {
        var rems = form.Rems!;
        // Assigned admin, CSE, and the requester — the customer coming back is the milestone the person who
        // raised the request is waiting on.
        var recipients = new[] { rems.AdminAssignedToId, rems.CSEId, rems.CreatedById }
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
                    $"{rems.REMSNumber} — {rems.ClientDisplayName}",
                    EntityType.Rems,
                    rems.Id), cancellationToken);
            }

            // Nobody has claimed this request, so the answers that just landed are on no admin's desk in
            // particular.
            if (rems.AdminAssignedToId is null)
            {
                var admins = await _users.ListByTenantRolesAsync(
                    form.TenantId, new[] { Roles.Admin, Roles.SuperAdmin }, cancellationToken);
                foreach (var admin in admins.Where(a => !recipients.Contains(a.Id)))
                {
                    await _notifications.DispatchAsync(new CreateNotificationDto(
                        admin.Id,
                        NotificationType.RemsRequestAssigned,
                        "A REMS request is waiting for pickup",
                        $"{rems.REMSNumber} — {rems.ClientDisplayName}",
                        EntityType.Rems,
                        rems.Id), cancellationToken);
                }
            }

            await _activity.WriteAsync(
                new CreateActivityEventDto(EntityType.Rems, rems.Id, ActivityEventTypes.RemsFormSubmitted), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // External "form submitted" email to the Admin + CSE. Enqueued on a worker; never throws/blocks.
            var model = new RemsFormSubmittedEmail(
                Clean(payload.EffectiveClientName) ?? rems.ClientDisplayName,
                rems.REMSNumber,
                $"{_baseUrl.TrimEnd('/')}/rems/requests/{rems.Id}",
                (form.SubmittedOnUtc ?? DateTime.UtcNow).ToString("f", CultureInfo.InvariantCulture) + " UTC");

            foreach (var userId in recipients)
            {
                var user = await _users.GetByIdAsync(userId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(user?.Email))
                {
                    _emailNotifier.SendFormSubmitted(form.TenantId, user!.Email, model);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "REMS form {FormId} was submitted, but post-commit notification/email dispatch failed.", form.Id);
        }
    }

    // -------------------- Review model --------------------

    private RemsReviewModel BuildReviewModel(REMS rems, RemsFormPayloadV1 payload, string entityType)
    {
        var contact = new RemsReviewContact(
            Clean(payload.EffectiveClientName), Clean(payload.ClientSuffix),
            Clean(payload.ClientFirstName), Clean(payload.ClientLastName),
            rems.CustomerEmail ?? string.Empty, Clean(payload.MobileNumber), Clean(payload.ReferralSource));

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
    /// Loads the form by invite code (unscoped) and, once resolved, pins the tenant context to its
    /// tenant.
    /// </summary>
    private async Task<REMSForm?> LoadFormAsync(string inviteCode, CancellationToken cancellationToken)
    {
        var form = await _forms.GetByInviteCodeUnscopedAsync(inviteCode?.Trim() ?? string.Empty, cancellationToken);
        if (form is not null && form.TenantId != Guid.Empty)
        {
            // Establish the resolved tenant so DbContext stamping and the notification/activity writers behave
            // exactly as they do for an authenticated request. Every write also sets TenantId explicitly.
            _tenantContext.Set(form.TenantId, string.Empty);
        }

        return form;
    }

    /// <summary>The locked prefill for the editable form.</summary>
    private static RemsPublicPrefill BuildPrefill(REMS rems)
    {
        // STRAIGHT OFF THE CLIENT'S OWN RECORD, no splitting.
        var person = rems.ClientPerson;
        var first = person?.IsOrganisation == true ? string.Empty : person?.FirstName ?? string.Empty;
        var last = person?.IsOrganisation == true ? string.Empty : person?.LastName ?? string.Empty;
        var name = person?.IsOrganisation == true
            ? person.CorporateName?.Trim() ?? string.Empty
            : string.Join(" ", new[] { first, last }.Where(p => p.Length > 0));
        return new RemsPublicPrefill(
            name, first, last, rems.ClientNameSuffix,
            rems.CustomerEmail ?? string.Empty, rems.CustomerMobileNumber);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The link is dead when the request is gone/deleted or the form was cancelled.</summary>
    private static bool IsUnavailable(REMSForm form)
        => form.Rems is null || form.Rems.Deleted || form.Status == RemsFormStatus.Cancelled;

    /// <summary>Editable == sent-and-live: the only state that accepts draft saves, review and submit.</summary>
    private static bool IsEditable(REMSForm form)
        => !IsUnavailable(form) && form.Status == RemsFormStatus.Sent;

    /// <summary>
    /// The single active draft for the form (query filters were ignored on load, so exclude
    /// soft-deleted).
    /// </summary>
    private static REMSFormDraft? CurrentDraft(REMSForm form)
        => form.Drafts.FirstOrDefault(d => !d.Deleted);

    /// <summary>The payload to act on: the request body when supplied, else the stored draft.</summary>
    private static RemsFormPayloadV1? ResolvePayload(RemsFormPayloadV1? body, REMSForm form)
        => body ?? RemsFormPayloadJson.TryDeserialize(CurrentDraft(form)?.DraftPayload);

    /// <summary>The relevant (role, canonical role name, required?) tuples for an industry group.</summary>
    private static IEnumerable<(RemsRolePayload? Role, string RoleName, bool IsRequired)> EnumerateRoles(
        string entityType, RemsRolesPayload roles)
    {
        // Mirrors RemsFormPayloadValidator's branches — the roles it REQUIRES are staged as required here.
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
            yield return (roles.FinancialContact, nameof(RemsContactRole.FinancialContact), true);
            // Trust and Estate only. Yielded for the whole family — a null role stages nothing, and an
            // answer given before the entity type changed is still an answer (as with the retired roles).
            yield return (roles.TrustEstateContact, nameof(RemsContactRole.TrustEstateContact), false);
            yield return (roles.OtherContact, nameof(RemsContactRole.OtherContact), false);
            yield return (roles.BillingContact, nameof(RemsContactRole.BillingContact), false);   // retired
            yield return (roles.Banker, nameof(RemsContactRole.Banker), false);                   // retired
            yield return (roles.Lawyer, nameof(RemsContactRole.Lawyer), false);                   // retired
        }
        else if (entityType == RemsFormPayloadValidator.Government)
        {
            yield return (roles.FinanceDirector, nameof(RemsContactRole.FinanceDirector), true);
            yield return (roles.OtherContact, nameof(RemsContactRole.OtherContact), false);
            yield return (roles.BillingContact, nameof(RemsContactRole.BillingContact), false);   // retired
        }
    }

    /// <summary>
    /// A per-request-unique source key for a declared individual, on the same terms as <see
    /// cref="UniqueEntityKey"/> — the unique index on (tenant, request.
    /// </summary>
    private static string UniqueIndividualKey(string? supplied, HashSet<string> used, ref int fallbackIndex)
    {
        var candidate = supplied?.Trim();
        if (string.IsNullOrEmpty(candidate) || used.Contains(candidate))
        {
            do
            {
                candidate = $"individual-{++fallbackIndex}";
            }
            while (used.Contains(candidate));
        }

        candidate = candidate.Length > 64 ? candidate[..64] : candidate;
        used.Add(candidate);
        return candidate;
    }

    /// <summary>
    /// A per-client-unique, non-"main" source key for a related entity; never trusts the client value
    /// blindly.
    /// </summary>
    private static string UniqueEntityKey(string? supplied, HashSet<string> used, ref int fallbackIndex)
    {
        var candidate = supplied?.Trim();
        if (string.IsNullOrEmpty(candidate) || string.Equals(candidate, "main", StringComparison.OrdinalIgnoreCase) || used.Contains(candidate))
        {
            do
            {
                candidate = $"related-{++fallbackIndex}";
            }
            while (used.Contains(candidate));
        }

        // Cap to the column length (64) and record it as taken.
        candidate = candidate.Length > 64 ? candidate[..64] : candidate;
        used.Add(candidate);
        return candidate;
    }

    private static Address NewAddress(RemsAddressPayload payload, AddressType type) => new()
    {
        Id = Guid.NewGuid(),
        AddressType = type,
        AddressLine1 = Clean(payload.Street),
        AddressLine2 = Clean(payload.AddressLine2),
        CityName = Clean(payload.City),
        StateCode = Clean(payload.StateCode),
        StateName = Clean(payload.State),
        CountryCode = Clean(payload.CountryCode),
        CountryName = Clean(payload.CountryName),
        PostalCode = Clean(payload.Zip),
        // Who the post is addressed to. Null on a physical or mailing node, which is asked for a place
        // and nothing more; filled on a billing one, where the form asks both halves of the answer.
        Suffix = Clean(payload.Suffix),
        FirstName = Clean(payload.FirstName),
        LastName = Clean(payload.LastName),
        Email = Clean(payload.Email),
        PhoneNumber = Clean(payload.Phone),
    };

    private static REMSEntityAddress NewEntityAddress(Guid tenantId, Guid entityId, Guid addressId, RemsAddressType type) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        REMSEntityId = entityId,
        AddressId = addressId,
        AddressType = type,
    };

    private static RemsAddressPayload? NonEmpty(RemsAddressPayload? address) => address is { HasAny: true } ? address : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private IActionResult FormNotFound()
        => NotFound(ApiResponseFactory.NotFound("This form is not available."));

    private IActionResult NotEditable()
        => StatusCode(StatusCodes.Status409Conflict, ApiResponseFactory.Error(
            CodeNotEditable, "This form is not available.", "The form is not currently accepting changes."));
}
