using System.Security.Cryptography;
using EmsPortal.Api.Filters;
using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.Security;
using EmsPortal.Api.Validators.Rems;
using EmsPortal.Application.Abstractions.Email;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.OptionSets;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Application.Email;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Configuration;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;
using EmsPortal.Api.RateLimiting;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// REMS EMS-form build/send + email-log backend (WO-112). There is no form row any more: the form is the
/// request's invite columns, its entity type is the client's, and its status is derived from both.
/// </summary>
[ApiController]
[LoadMaconomyClients]
[Route("api/rems/requests")]
[Produces("application/json")]
[Tags("REMS EMS Form")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class RemsFormController : ControllerBase
{
    // REMS-form conflict codes (409): the form isn't in a state the requested action allows.
    private const string CodeFormNotBuilt = "REMS_FORM_NOT_BUILT";
    private const string CodeFormNotSendable = "REMS_FORM_NOT_SENDABLE";
    private const string CodeFormAlreadySent = "REMS_FORM_ALREADY_SENT";
    private const string CodeClientEmailMissing = "REMS_CLIENT_EMAIL_MISSING";
    private const string CodeCommissionNotFullyAllocated = "REMS_COMMISSION_NOT_FULLY_ALLOCATED";
    private const string CodeEntityTypeLocked = "REMS_ENTITY_TYPE_LOCKED";
    private const string CodeEntityTypeOffRecord = "REMS_ENTITY_TYPE_OFF_RECORD";
    private const string CodeFormAlreadySubmitted = "REMS_FORM_ALREADY_SUBMITTED";
    private const string CodeClientEmailUnconfirmed = "REMS_CLIENT_EMAIL_UNCONFIRMED";
    private const string CodeClientEmailInvalid = "REMS_CLIENT_EMAIL_INVALID";

    private readonly IRemsRequestRepository _requests;
    private readonly IClientRepository _clients;
    /// <summary>Only to answer whether an initiator has cover arranged — see RemsSetupAccess.CanWork.</summary>
    private readonly IRemsDelegationRepository _delegations;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityEventWriter _activity;
    private readonly INotificationDispatcher _notifications;
    private readonly IRemsEmailNotifier _emailNotifier;
    private readonly IEmailTemplateService _templates;
    /// <summary>Only to stamp a send with the template row it went out on; the service renders it but does not name it.</summary>
    private readonly IEmailTemplateRepository _templateRows;
    private readonly ITenantRepository _tenants;
    private readonly IOptionCodeResolver _codes;
    private readonly IMaconomyClientDirectory _maconomy;
    private readonly string _baseUrl;

    public RemsFormController(
        IRemsRequestRepository requests,
        IClientRepository clients,
        IRemsDelegationRepository delegations,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IActivityEventWriter activity,
        INotificationDispatcher notifications,
        IRemsEmailNotifier emailNotifier,
        IEmailTemplateService templates,
        IEmailTemplateRepository templateRows,
        ITenantRepository tenants,
        IOptionCodeResolver codes,
        IMaconomyClientDirectory maconomy,
        IOptions<AppOptions> appOptions)
    {
        _requests = requests;
        _clients = clients;
        _delegations = delegations;
        _users = users;
        _unitOfWork = unitOfWork;
        _activity = activity;
        _notifications = notifications;
        _emailNotifier = emailNotifier;
        _templates = templates;
        _templateRows = templateRows;
        _tenants = tenants;
        _codes = codes;
        _maconomy = maconomy;
        _baseUrl = appOptions.Value.BaseUrl;
    }

    // -------------------- Build screen --------------------

    /// <summary>The EMS form build-screen model: the request context plus the current form (AC-REMS-007.1).</summary>
    [HttpGet("{remsId:guid}/form")]
    [RequireAnyPermission(Permissions.RemsFormsManage, Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsFormBuildScreen>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForm(Guid remsId, CancellationToken cancellationToken)
    {
        var rems = await LoadAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        if (await GuardSetupOwnerAsync(rems, cancellationToken) is { } denied)
        {
            return denied;
        }

        var screen = await BuildScreenAsync(rems, cancellationToken);
        return Ok(ApiResponseFactory.Success(screen, "REMS form retrieved."));
    }

    /// <summary>
    /// Build/save the form (AC-REMS-007): set the CSE on the request, the industry group on its client,
    /// and mint (or, before send, regenerate) the invite link.
    /// </summary>
    [HttpPost("{remsId:guid}/form")]
    [RequireAnyPermission(Permissions.RemsFormsManage, Permissions.RemsRequestsUpdate)]
    [ProducesResponseType<ApiResponse<RemsFormBuildScreen>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveForm(Guid remsId, [FromBody] SaveRemsFormRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me || User.GetActiveTenantId() is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("No active tenant/user context."));
        }

        var rems = await LoadAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        if (await GuardSetupOwnerAsync(rems, cancellationToken) is { } denied)
        {
            return denied;
        }

        // The CSE must resolve to a real user (AC-REMS-007.7).
        if (await _users.GetByIdAsync(request.CseUserId, cancellationToken) is null)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", "Unknown cseUserId."));
        }

        var client = rems.Client!;
        var alreadySent = rems.InviteSentOnUtc is not null;
        // No invite yet, or a type other than the one the client's record holds.
        var industryChanged = rems.InviteCode is null
            || !string.Equals(client.EntityType?.Value, request.EntityType, StringComparison.Ordinal);

        // The entity type is a foreign key to its option item, so the CODE the caller sent is resolved once.
        var entityTypeId = await _codes.RequireRemsIdAsync(
            RemsOptionSetKeys.EntityType, request.EntityType, cancellationToken);

        // Once sent, the industry group / invite code are locked (AC-REMS-007.5).
        if (alreadySent && industryChanged)
        {
            return FormConflict(CodeEntityTypeLocked,
                "The entity type and invite link are locked once the form has been sent.");
        }

        // Checked only when the type is being set or changed, so a request filed before the rule existed
        // can still save its CSE.
        if (industryChanged
            && await EntityTypeOffRecordAsync(rems, request.EntityType, cancellationToken) is { } offRecord)
        {
            return FormConflict(CodeEntityTypeOffRecord, offRecord);
        }

        // CSE assign / reassign detection drives the in-app notification (AC-REMS-007.8/9).
        var notifyCse = rems.CSEId != request.CseUserId; // first assignment (null -> x) or reassignment (x -> y)

        rems.CSEId = request.CseUserId;
        if (industryChanged)
        {
            // The type is the client's. Setting or changing it before send mints / regenerates the invite
            // code and link (AC-REMS-007.5).
            client.EntityTypeId = entityTypeId;
            _clients.Update(client);
            rems.InviteCode = await GenerateUniqueInviteCodeAsync(tenantId, cancellationToken);
        }
        _requests.Update(rems);

        await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, remsId, ActivityEventTypes.RemsFormBuilt), cancellationToken);
        if (notifyCse)
        {
            await _notifications.DispatchAsync(CseAssignedNotification(request.CseUserId, rems), cancellationToken);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await LoadAsync(remsId, cancellationToken) ?? rems;
        var screen = await BuildScreenAsync(refreshed, cancellationToken);
        return Ok(ApiResponseFactory.Success(screen, "REMS form saved."));
    }

    // -------------------- Preview & send --------------------

    /// <summary>The pre-send preview: destination email + form link (AC-REMS-008.1).</summary>
    [HttpGet("{remsId:guid}/form/preview")]
    [RequirePermission(Permissions.RemsFormsSend)]
    [ProducesResponseType<ApiResponse<RemsFormPreview>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(Guid remsId, CancellationToken cancellationToken)
    {
        var rems = await LoadAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        if (await GuardSetupOwnerAsync(rems, cancellationToken) is { } denied)
        {
            return denied;
        }

        if (rems.InviteCode is null)
        {
            return FormConflict(CodeFormNotBuilt, "The form has not been built yet.");
        }

        // Render the effective template with the values this send would use.
        var formLink = BuildFormLink(rems.InviteCode);
        var rendered = User.GetActiveTenantId() is { } previewTenantId
            ? await _templates.RenderEffectiveAsync(
                previewTenantId,
                EmailTemplateKey.RemsFormLink,
                await FormLinkModelAsync(rems, formLink, cancellationToken),
                cancellationToken)
            : null;

        var preview = new RemsFormPreview(
            Normalize(rems.Client!.ClientEmail), formLink, rendered?.Subject, rendered?.Body);
        return Ok(ApiResponseFactory.Success(preview, "REMS form preview retrieved."));
    }

    /// <summary>Send the form-link email to the client (AC-REMS-008).</summary>
    [HttpPost("{remsId:guid}/form/send")]
    [EnableRateLimiting(RateLimitPolicies.Email)]
    [RequirePermission(Permissions.RemsFormsSend)]
    [ProducesResponseType<ApiResponse<RemsFormBuildScreen>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Send(Guid remsId, [FromBody] SendRemsFormRequest? request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me || User.GetActiveTenantId() is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("No active tenant/user context."));
        }

        // The workspace graph, for the commission splits the send is gated on.
        var rems = await LoadAsync(remsId, cancellationToken, withWorkspace: true);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        if (await GuardSetupOwnerAsync(rems, cancellationToken) is { } denied)
        {
            return denied;
        }

        var client = rems.Client!;
        if (rems.InviteCode is null)
        {
            return FormConflict(CodeFormNotBuilt, "The form has not been built yet.");
        }
        if (rems.InviteSentOnUtc is not null)
        {
            return FormConflict(CodeFormAlreadySent, "The form has already been sent.");
        }
        // Must be saved with a CSE + industry group (AC-REMS-007.10).
        if (rems.CSEId is null || client.EntityTypeId is null || RemsDerived.FormStatusOf(rems) != RemsFormStatus.Saved)
        {
            return FormConflict(CodeFormNotSendable,
                "The form must be saved with a CSE and an entity type before it can be sent.");
        }
        // The client must have an email to receive the link (AC-REMS-008.2).
        if (await ClientEmailBlockedAsync(rems, cancellationToken) is { } noAddress)
        {
            return FormConflict(noAddress.Code, noAddress.Reason);
        }
        var email = Normalize(client.ClientEmail)!;

        // Commission is optional; where anyone is named, the split has to be settled before the client is written to.
        var splits = rems.CommissionSplits.Where(s => !s.Deleted).ToList();
        var allocated = Math.Round(splits.Sum(s => s.CommissionPercentage), 2, MidpointRounding.AwayFromZero);
        if (splits.Count > 0 && allocated != 100m)
        {
            return FormConflict(
                CodeCommissionNotFullyAllocated,
                $"Commission totals {allocated:0.##}% — the recipients on the Commission tab must add up "
                + "to 100% before this request can be sent to the client.");
        }

        var now = DateTime.UtcNow;
        var formLink = BuildFormLink(rems.InviteCode);
        // The sender's wording with every placeholder filled in, so the log and the email say the same thing.
        var (subject, body) = await ComposeAsync(request, rems, formLink, cancellationToken);

        // Mint a stable outbound Message-ID and store it as the ProviderMessageId on the Sent event so a
        // delivery provider can echo it back on delivery/open/failed callbacks that WO-121 ingests.
        var messageId = BuildOutboundMessageId();

        // The send is what locks the invite: who sent it, when, and where it went.
        rems.InviteSentOnUtc = now;
        rems.InviteSentByUserId = me;
        rems.InviteRecipientEmail = email;

        // Sending the intake link is what takes a request out of draft — there is no pool in between any
        // more, so this is the initiator's own hand-off to the client.
        if (rems.Status!.Value == RemsRequestStatuses.Draft)
        {
            var fromStatusId = rems.StatusId;
            rems.StatusId = await _codes.RequireRemsIdAsync(
                RemsOptionSetKeys.Status, RemsRequestStatuses.AwaitingCustomer, cancellationToken);
            await _requests.AddStatusHistoryAsync(new RemsStatusHistory
            {
                Id = Guid.NewGuid(),
                TenantId = rems.TenantId,
                REMSId = rems.Id,
                FromStatusId = fromStatusId,
                ToStatusId = rems.StatusId,
                ChangedByUserId = me,
                ChangedOnUtc = now,
            }, cancellationToken);
        }
        _requests.Update(rems);

        // Record the Sent delivery event (later delivery/open/failed events are ingested by WO-121, matched
        // to this row by ProviderMessageId).
        await _requests.AddEmailEventAsync(new RemsEmailEvent
        {
            Id = Guid.NewGuid(),
            TenantId = rems.TenantId,
            REMSId = rems.Id,
            EmailTemplateId = await EffectiveTemplateIdAsync(tenantId, EmailTemplateKey.RemsFormLink, cancellationToken),
            ProviderMessageId = messageId,
            EventType = RemsFormEmailEventType.Sent,
            RecipientEmail = email,
            OccurredOnUtc = now,
            // What the client is about to read, kept alongside the fact that we sent it.
            Subject = subject,
            Body = body,
        }, cancellationToken);

        await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, remsId, ActivityEventTypes.RemsFormSent), cancellationToken);
        // Sender, CSE, and whoever raised the request — the requester is tracking their client's onboarding
        // and this is the step that puts the ball in the customer's court.
        foreach (var recipient in new[] { me, rems.CSEId, rems.CreatedById }
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct())
        {
            await _notifications.DispatchAsync(FormSentNotification(recipient, rems), cancellationToken);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Enqueue the email only after the Sent state is durably persisted.
        _emailNotifier.SendComposedFormLink(
            tenantId, email, new RemsFormLinkEmail(ClientNameOf(rems), formLink, rems.RemsNumber),
            subject, body, messageId);

        var refreshed = await LoadAsync(remsId, cancellationToken) ?? rems;
        var screen = await BuildScreenAsync(refreshed, cancellationToken);
        return Ok(ApiResponseFactory.Success(screen, "REMS form sent."));
    }

    // -------------------- Reminder --------------------

    /// <summary>The pre-send preview for a REMINDER, rendered from the <c>RemsFormReminder</c> template.</summary>
    [HttpGet("{remsId:guid}/form/reminder/preview")]
    [RequirePermission(Permissions.RemsFormsSend)]
    [ProducesResponseType<ApiResponse<RemsFormPreview>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReminderPreview(Guid remsId, CancellationToken cancellationToken)
    {
        var context = await LoadRemindableAsync(remsId, confirmAddress: false, cancellationToken);
        if (context.Failure is not null)
        {
            return context.Failure;
        }

        var rems = context.Rems!;
        var formLink = BuildFormLink(rems.InviteCode!);
        var rendered = User.GetActiveTenantId() is { } tenantId
            ? await _templates.RenderEffectiveAsync(
                tenantId, EmailTemplateKey.RemsFormReminder,
                await FormLinkModelAsync(rems, formLink, cancellationToken), cancellationToken)
            : null;

        var preview = new RemsFormPreview(
            Normalize(rems.Client!.ClientEmail), formLink, rendered?.Subject, rendered?.Body);
        return Ok(ApiResponseFactory.Success(preview, "REMS form reminder preview retrieved."));
    }

    /// <summary>Re-send the form link to a client who has not submitted yet.</summary>
    [HttpPost("{remsId:guid}/form/reminder")]
    [EnableRateLimiting(RateLimitPolicies.Email)]
    [RequirePermission(Permissions.RemsFormsSend)]
    [ProducesResponseType<ApiResponse<RemsFormBuildScreen>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendReminder(
        Guid remsId, [FromBody] SendRemsFormRequest? request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me || User.GetActiveTenantId() is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("No active tenant/user context."));
        }

        var context = await LoadRemindableAsync(remsId, confirmAddress: true, cancellationToken);
        if (context.Failure is not null)
        {
            return context.Failure;
        }

        var rems = context.Rems!;
        var email = Normalize(rems.Client!.ClientEmail)!; // guaranteed by LoadRemindableAsync
        var formLink = BuildFormLink(rems.InviteCode!);
        var messageId = BuildOutboundMessageId();
        var (subject, body) = await ComposeAsync(request, rems, formLink, cancellationToken);

        // A reminder changes nothing about the request itself: no status, no history, just the event.
        await _requests.AddEmailEventAsync(new RemsEmailEvent
        {
            Id = Guid.NewGuid(),
            TenantId = rems.TenantId,
            REMSId = rems.Id,
            EmailTemplateId = await EffectiveTemplateIdAsync(tenantId, EmailTemplateKey.RemsFormReminder, cancellationToken),
            ProviderMessageId = messageId,
            EventType = RemsFormEmailEventType.Reminder,
            RecipientEmail = email,
            OccurredOnUtc = DateTime.UtcNow,
            Subject = subject,
            Body = body,
        }, cancellationToken);

        await _activity.WriteAsync(
            new CreateActivityEventDto(EntityType.Rems, remsId, ActivityEventTypes.RemsFormReminderSent), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Enqueued after the event row is durable, as the first send does. Best-effort on a Hangfire
        // worker; a delivery failure must not roll back the record that we tried.
        _emailNotifier.SendComposedFormReminder(
            tenantId, email, new RemsFormLinkEmail(ClientNameOf(rems), formLink, rems.RemsNumber),
            subject, body, messageId);

        // Nobody in-app is notified: this is the admin chasing the client, and telling the team each time
        // somebody clicks Remind would be noise about their own action.
        _ = me;

        var refreshed = await LoadAsync(remsId, cancellationToken) ?? rems;
        var screen = await BuildScreenAsync(refreshed, cancellationToken);
        return Ok(ApiResponseFactory.Success(screen, "Reminder sent."));
    }

    /// <summary>Resolves a request whose client can legitimately be reminded, or the error explaining why not.</summary>
    private async Task<(RemsRequest? Rems, IActionResult? Failure)> LoadRemindableAsync(
        Guid remsId, bool confirmAddress, CancellationToken cancellationToken)
    {
        var rems = await LoadAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return (null, NotFound(ApiResponseFactory.NotFound("REMS request not found.")));
        }

        if (await GuardSetupOwnerAsync(rems, cancellationToken) is { } denied)
        {
            return (null, denied);
        }

        if (RemindBlocked(rems) is { } blocked)
        {
            return (null, FormConflict(blocked.Code, blocked.Reason));
        }
        // Only a real send confirms the address with Maconomy; the preview shows what is held.
        if (confirmAddress && await ClientEmailBlockedAsync(rems, cancellationToken) is { } noAddress)
        {
            return (null, FormConflict(noAddress.Code, noAddress.Reason));
        }

        return (rems, null);
    }

    /// <summary>
    /// Why the client cannot be written to right now, or null when they can. A Maconomy customer's
    /// address is read from Maconomy at this moment, so the email goes where Maconomy says today.
    /// </summary>
    private async Task<(string Code, string Reason)?> ClientEmailBlockedAsync(RemsRequest rems, CancellationToken cancellationToken)
    {
        var client = rems.Client!;
        if (!client.IsMaconomyClient)
        {
            return Normalize(client.ClientEmail) is null
                ? (CodeClientEmailMissing, "The client has no email address on file; add one before sending.")
                : null;
        }

        var number = client.MaconomyCustomerNumber!;
        try
        {
            if (await _maconomy.ReadAsync(rems.TenantId, number, cancellationToken) is not { } live)
            {
                return (CodeClientEmailUnconfirmed,
                    $"Maconomy no longer has a customer {number}, so there is no address to send to.");
            }
            client.Maconomy = live;
        }
        catch (MaconomyException)
        {
            // What is held and recent will do; anything older is not what an email should go out on.
            if (client.Maconomy is null || !_maconomy.IsFresh(rems.TenantId))
            {
                return (CodeClientEmailUnconfirmed,
                    "Maconomy could not be reached, so the client's email address could not be confirmed. Try again in a moment.");
            }
        }

        var email = Normalize(client.ClientEmail);
        if (email is null)
        {
            return (CodeClientEmailMissing,
                $"Maconomy holds no email address for customer {number}. Add one in Maconomy, then send again.");
        }
        return System.Net.Mail.MailAddress.TryCreate(email, out _) && email.IndexOfAny([' ', ',', ';']) < 0
            ? null
            : (CodeClientEmailInvalid,
                $"The email address Maconomy holds for customer {number} is not one valid address. Correct it in Maconomy, then send again.");
    }

    /// <summary>Why this request's client cannot be reminded right now, or null when they can.</summary>
    private static (string Code, string Reason)? RemindBlocked(RemsRequest rems)
    {
        if (rems.InviteCode is null)
        {
            return (CodeFormNotBuilt, "The form has not been built yet.");
        }
        if (rems.InviteSentOnUtc is null)
        {
            return (CodeFormNotSendable, "The form has not been sent yet, so there is nothing to remind the client about.");
        }
        if (rems.FormSubmittedOnUtc is not null)
        {
            return (CodeFormAlreadySubmitted, "The client has already submitted this form.");
        }
        var client = rems.Client!;
        if (Normalize(client.ClientEmail) is null)
        {
            return client switch
            {
                // Not known while Maconomy cannot be read; the send itself asks Maconomy and says so.
                { IsMaconomyClient: true, Maconomy: null } => null,
                { IsMaconomyClient: true } => (CodeClientEmailMissing,
                    "Maconomy holds no email address for this customer. Add one in Maconomy before sending."),
                _ => (CodeClientEmailMissing, "The client has no email address on file; add one before sending."),
            };
        }

        return null;
    }

    // -------------------- Email log --------------------

    /// <summary>
    /// The request's email history, newest first (AC-REMS-008.6), together with whether this caller
    /// can nudge the client from it.
    /// </summary>
    [HttpGet("{remsId:guid}/email-log")]
    [RequirePermission(Permissions.RemsEmailLogRead)]
    [ProducesResponseType<ApiResponse<RemsEmailLog>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EmailLog(Guid remsId, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var rems = await LoadAsync(remsId, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        if (!RemsSetupAccess.CanRead(User, rems, me))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("This request's email log is only open to the people named on it."));
        }

        var events = await _requests.ListEmailEventsAsync(remsId, cancellationToken);

        // Who pressed Send / Remind. Resolved in one lookup for the whole page rather than per row, and
        // only over the events that name somebody — provider callbacks carry no actor.
        var senders = await _users.GetFullNamesAsync(
            events.Select(e => e.CreatedById).Where(id => id.HasValue).Select(id => id!.Value).Distinct(),
            cancellationToken);

        // Sends recorded before the wording was rendered at send time still hold the template's tokens.
        // Filled in here with the same values, so the preview reads as the email rather than its template.
        var model = rems.InviteCode is null
            ? null
            : await FormLinkModelAsync(rems, BuildFormLink(rems.InviteCode), cancellationToken);
        var rows = events.Select(e =>
        {
            var (subject, body) = model is null || (e.Subject is null && e.Body is null)
                ? (e.Subject, e.Body)
                : RenderComposed(e.Subject, e.Body, model);
            return new RemsEmailEventRow(
                e.Id, e.EventType.ToString(), e.RecipientEmail, e.OccurredOnUtc,
                DescribeFailure(e),
                e.CreatedById is { } actor && senders.TryGetValue(actor, out var name) ? name : null,
                subject, body);
        }).ToList();

        // Exactly what POST .../form/reminder would decide, asked ahead of the click.
        var blocked = RemindBlocked(rems);
        var maySend = User.HasPermission(Permissions.RemsFormsSend);
        var isOwner = RemsSetupAccess.CanWork(
            User, rems, me, await RemsSetupAccess.CoverForWorkAsync(_delegations, rems, me, cancellationToken));
        var reason = !maySend
            ? null
            : blocked?.Reason ?? (isOwner ? null : RemsSetupAccess.WorkDeniedReason(rems));

        // The client's link, only while it is genuinely theirs to follow: sent, and not yet answered.
        var linkable = rems.InviteCode is not null
            && rems.InviteSentOnUtc is not null
            && rems.FormSubmittedOnUtc is null;
        var clientFormLink = linkable ? BuildFormLink(rems.InviteCode!) : null;

        var log = new RemsEmailLog(blocked is null && maySend && isOwner, reason, rows, clientFormLink);
        return Ok(ApiResponseFactory.Success(log, "REMS form email log retrieved."));
    }

    // -------------------- EMS Inbox --------------------

    /// <summary>
    /// The EMS Inbox: every request that has a form, paginated and newest-modified first, with request
    /// context, form state, creator, and latest send/delivery/open info (AC-REMS-009).
    /// </summary>
    [HttpGet("/api/rems/inbox")]
    [RequireAnyPermission(Permissions.RemsRequestsRead, Permissions.RemsFormsManage)]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsInboxRow>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Inbox(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? formState = null,
        [FromQuery] string? search = null,
        [FromQuery] string? requestStatus = null,
        CancellationToken cancellationToken = default)
    {
        RemsFormStatus? state = Enum.TryParse<RemsFormStatus>(formState, ignoreCase: true, out var parsed) ? parsed : null;

        var (items, total) = await _requests.ListInboxAsync(
            new RemsInboxQuery(state, search, requestStatus, page, limit), cancellationToken);
        var names = await _users.GetFullNamesAsync(
            items.Select(i => i.FormCreatedByUserId)
                .Concat(items.SelectMany(i => new[] { i.CreatedById, i.UpdatedById, i.AdminAssignedToId })
                    .Where(id => id.HasValue).Select(id => id!.Value)),
            cancellationToken);

        string? NameOf(Guid? id) => id is { } uid && names.TryGetValue(uid, out var name) ? name : null;

        var rows = items.Select(i => new RemsInboxRow(
            i.RemsId, i.RemsNumber, i.ClientName, i.EngagementType, i.RequestStatus,
            i.FormStatus.ToString(),
            new RemsUserRef(i.FormCreatedByUserId, names.TryGetValue(i.FormCreatedByUserId, out var n) ? n : string.Empty),
            i.FormSentOnUtc,
            i.LatestEmailEventType?.ToString(),
            i.LatestEmailEventOnUtc,
            RemsWorkspaceMapper.UserRef(i.AdminAssignedToId, names),
            NameOf(i.CreatedById), i.CreatedOnUtc, NameOf(i.UpdatedById), i.UpdatedOnUtc));

        return Ok(ApiResponseFactory.Paginated(rows, "REMS EMS inbox retrieved.", page, limit, total));
    }

    // -------------------- Helpers --------------------

    /// <summary>
    /// The request with its client fit to read. A Maconomy customer's name and address live in the
    /// directory, not the row, so they are attached here.
    /// </summary>
    private async Task<RemsRequest?> LoadAsync(Guid remsId, CancellationToken cancellationToken, bool withWorkspace = false)
    {
        var rems = withWorkspace
            ? await _requests.GetWorkspaceAsync(remsId, cancellationToken)
            : await _requests.GetByIdAsync(remsId, cancellationToken);
        await _maconomy.HydrateAsync(rems?.Client, cancellationToken);
        return rems;
    }

    /// <summary>The refusal for reading or writing this request's form, or null to carry on.</summary>
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

    /// <summary>The build screen for a request and the form its invite columns describe.</summary>
    private async Task<RemsFormBuildScreen> BuildScreenAsync(RemsRequest rems, CancellationToken cancellationToken)
    {
        var client = rems.Client!;
        var names = await _users.GetFullNamesAsync(
            rems.CSEId is { } cse ? new[] { cse } : Array.Empty<Guid>(), cancellationToken);

        var cseRef = rems.CSEId is { } cseId
            ? new RemsUserRef(cseId, names.TryGetValue(cseId, out var name) ? name : string.Empty)
            : null;

        // The codes come off the loaded option items; the resolver only steps in where a navigation is
        // missing, e.g. an item that has since been retired.
        var requestStatus = rems.Status?.Value
            ?? await _codes.CodeOfAsync(rems.StatusId, cancellationToken)
            ?? string.Empty;
        RemsFormInfo? formInfo = null;
        if (rems.InviteCode is { } inviteCode)
        {
            var entityType = client.EntityType?.Value
                ?? await _codes.CodeOfAsync(client.EntityTypeId, cancellationToken)
                ?? string.Empty;
            formInfo = new RemsFormInfo(
                rems.Id,
                entityType,
                inviteCode,
                BuildFormLink(inviteCode),
                RemsDerived.FormStatusOf(rems)?.ToString() ?? string.Empty,
                rems.InviteSentOnUtc,
                rems.FormSubmittedOnUtc,
                // The send is what locks the invite.
                rems.InviteSentOnUtc,
                rems.InviteSentOnUtc is not null);
        }

        return new RemsFormBuildScreen(
            rems.Id, rems.RemsNumber, client.ClientName, requestStatus,
            client.ClientEmail, client.ClientPhone, cseRef, formInfo);
    }

    /// <summary>
    /// The public EMS-form URL (the route WO-113/116 serve), built from <c>App:BaseUrl</c> as the
    /// email pipeline does.
    /// </summary>
    private string BuildFormLink(string inviteCode)
        => $"{_baseUrl.TrimEnd('/')}/rems/form/{inviteCode}";

    private static string ClientNameOf(RemsRequest rems) => rems.Client?.ClientName ?? string.Empty;

    private static Dictionary<string, string?> FormLinkModel(RemsRequest rems, string formLink) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["ClientName"] = ClientNameOf(rems),
        ["FormLink"] = formLink,
        ["RemsNumber"] = rems.RemsNumber,
    };

    /// <summary>
    /// The form-link model plus the common placeholders the send pipeline adds — the tenant's name, the
    /// portal's URL — so a preview fills in exactly the tokens the email will.
    /// </summary>
    private async Task<Dictionary<string, string?>> FormLinkModelAsync(
        RemsRequest rems, string formLink, CancellationToken cancellationToken)
    {
        var tenant = User.GetActiveTenantId() is { } tenantId
            ? await _tenants.GetByIdAsync(tenantId, cancellationToken)
            : null;
        return CommonEmailPlaceholders.Merge(FormLinkModel(rems, formLink), _baseUrl, tenant?.Name);
    }

    /// <summary>
    /// The subject and body as the sender left them, with every placeholder filled in — what is stored
    /// against the send and what the client is emailed. Null where the sender left a field blank, so the
    /// template's own wording still applies downstream.
    /// </summary>
    private async Task<(string? Subject, string? Body)> ComposeAsync(
        SendRemsFormRequest? request, RemsRequest rems, string formLink, CancellationToken cancellationToken)
    {
        var subject = Normalize(request?.Subject);
        var body = Normalize(request?.Body);
        if (subject is null && body is null)
        {
            return (null, null);
        }

        return RenderComposed(subject, body, await FormLinkModelAsync(rems, formLink, cancellationToken));
    }

    private (string? Subject, string? Body) RenderComposed(
        string? subject, string? body, IReadOnlyDictionary<string, string?> model)
    {
        var rendered = _templates.Render(subject ?? string.Empty, body ?? string.Empty, model);
        return (subject is null ? null : rendered.Subject, body is null ? null : rendered.Body);
    }

    /// <summary>The template row a send of this key goes out on in the tenant, for the email log; null where there is none.</summary>
    private async Task<Guid?> EffectiveTemplateIdAsync(Guid tenantId, EmailTemplateKey key, CancellationToken cancellationToken)
        => (await _templateRows.GetEffectiveAsync(tenantId, key, cancellationToken))?.Id;

    /// <summary>
    /// A stable, RFC-5322-style Message-ID (<c>{guid:N}@{host}</c>, no angle brackets) minted at send
    /// time (WO-121).
    /// </summary>
    private string BuildOutboundMessageId()
    {
        var host = Uri.TryCreate(_baseUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : "emsportal.local";
        return $"{Guid.NewGuid():N}@{host}";
    }

    /// <summary>Mints an opaque, URL-safe invite code (22 chars, 128 bits) unique per tenant.</summary>
    private async Task<string> GenerateUniqueInviteCodeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = GenerateInviteCode();
            if (!await _requests.InviteCodeExistsAsync(tenantId, code, cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Unable to generate a unique REMS invite code.");
    }

    private static string GenerateInviteCode()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        // base64url, no padding — 16 bytes -> 22 URL-safe chars.
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static CreateNotificationDto CseAssignedNotification(Guid recipientId, RemsRequest rems)
        => new(
            recipientId,
            NotificationType.RemsCseAssigned,
            "You were assigned as CSE on a REMS request",
            $"{rems.RemsNumber} — {ClientNameOf(rems)}",
            EntityType.Rems,
            rems.Id);

    private static CreateNotificationDto FormSentNotification(Guid recipientId, RemsRequest rems)
        => new(
            recipientId,
            NotificationType.RemsFormSent,
            "A REMS onboarding form was sent to the client",
            $"{rems.RemsNumber} — {ClientNameOf(rems)}",
            EntityType.Rems,
            rems.Id);

    private IActionResult FormConflict(string code, string message)
        => StatusCode(StatusCodes.Status409Conflict, ApiResponseFactory.Error(code, message, message));

    /// <summary>
    /// Why a request for a client ALREADY ON FILE cannot be filed under this entity type, or null where
    /// it can. A client no other request names is this request's own: its type is whatever the form says.
    /// </summary>
    private async Task<string?> EntityTypeOffRecordAsync(
        RemsRequest rems, string entityType, CancellationToken cancellationToken)
    {
        var client = rems.Client!;
        if (!await _requests.IsClientSharedAsync(client.Id, rems.Id, cancellationToken))
        {
            return null;
        }

        var held = HeldEntityTypeOf(client);
        if (held is not null)
        {
            return string.Equals(held, entityType, StringComparison.Ordinal)
                ? null
                : client.IsMaconomyClient
                    ? $"“{client.ClientName}” is held in Maconomy under a different entity type. A request for a "
                        + "Maconomy customer is filed under the entity type Maconomy gives them."
                    : $"“{client.ClientName}” is already on file under a different entity type. A request "
                        + "for a client on file is filed under the entity type their record holds.";
        }

        // A Maconomy customer Maconomy gives no type: whichever the initiator chooses.
        if (client.IsMaconomyClient)
        {
            return null;
        }

        // An organisation no form has classified yet: any type but a person's.
        return RemsClientEntityType.IsIndividual(entityType)
            ? $"“{client.ClientName}” is on file as an organisation, so it cannot be filed as an Individual."
            : null;
    }

    /// <summary>
    /// The type a client on file holds — RemsClientEntityType.Of, read off the client row: Maconomy's word
    /// for a linked customer, Individual for a person, and an organisation's recorded type unless that
    /// contradicts its own record.
    /// </summary>
    private static string? HeldEntityTypeOf(Client client)
    {
        var recorded = client.EntityType?.Value;
        if (client.IsMaconomyClient)
        {
            return client.Maconomy?.EntityType ?? recorded;
        }
        if (!client.IsOrganisation)
        {
            return RemsFormPayloadValidator.Individual;
        }
        return RemsClientEntityType.IsIndividual(recorded) ? null : recorded;
    }

    /// <summary>The human-readable reason for a Failed event, for the Email Log.</summary>
    private static string? DescribeFailure(RemsEmailEvent emailEvent)
        => emailEvent.EventType == RemsFormEmailEventType.Failed ? Normalize(emailEvent.FailureReason) : null;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
