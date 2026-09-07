using System.Text.Json;
using EmsPortal.Api.Models;
using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.OptionSets;
using EmsPortal.Application.Common;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>REMS approval workflow backend (WO-114 Part C).</summary>
[ApiController]
[Route("api/rems")]
[Produces("application/json")]
[Tags("REMS Approval")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class RemsApprovalController : ControllerBase
{
    private const string CodeSetupIncomplete = "REMS_SETUP_INCOMPLETE";
    private const string CodeMarketingRequired = "REMS_MARKETING_REQUIRED";
    private const string CodeCommissionNotFullyAllocated = "REMS_COMMISSION_NOT_FULLY_ALLOCATED";
    private const string CodeCafRequired = "REMS_CAF_REQUIRED";
    private const string CodeGovDetailRequired = "REMS_GOV_DETAIL_REQUIRED";
    private const string CodeNoApprovers = "REMS_NO_APPROVERS";
    private const string CodeNotSendable = "REMS_NOT_SENDABLE";
    private const string CodeNotRejected = "REMS_NOT_REJECTED";
    private const string CodeTaskDecided = "REMS_TASK_ALREADY_DECIDED";
    private const string CodeRoundClosed = "REMS_ROUND_CLOSED";
    private const string CodeChecklistIncomplete = "REMS_CHECKLIST_INCOMPLETE";
    private const string CodeApproversLocked = "REMS_APPROVERS_LOCKED";

    private const string MarketingSetKey = "REMSMarketing_MarketingMethods.MarketingMethodId";
    private const string TaxFormSetKey = "REMS.TaxForm";

    private readonly IRemsRepository _rems;
    /// <summary>Only to answer whether an initiator has cover arranged — see RemsSetupAccess.CanWork.</summary>
    private readonly IRemsDelegationRepository _delegations;
    private readonly IRemsEngagementRepository _engagements;
    private readonly IRemsApprovalRepository _approvals;
    private readonly IRemsClientRepository _clients;
    private readonly IMediaRepository _media;
    private readonly IOptionSetRepository _optionSets;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityEventWriter _activity;
    private readonly INotificationDispatcher _notifications;
    private readonly IOptionCodeResolver _codes;

    public RemsApprovalController(
        IRemsRepository rems,
        IRemsDelegationRepository delegations,
        IRemsEngagementRepository engagements,
        IRemsApprovalRepository approvals,
        IRemsClientRepository clients,
        IMediaRepository media,
        IOptionSetRepository optionSets,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IActivityEventWriter activity,
        INotificationDispatcher notifications,
        IOptionCodeResolver codes)
    {
        _rems = rems;
        _delegations = delegations;
        _engagements = engagements;
        _approvals = approvals;
        _clients = clients;
        _media = media;
        _optionSets = optionSets;
        _users = users;
        _unitOfWork = unitOfWork;
        _activity = activity;
        _notifications = notifications;
        _codes = codes;
    }

    // -------------------- Suggested approvers (live) --------------------

    /// <summary>
    /// The engagement's approver list (AC-REMS-018): the automatic approvers — the firm's
    /// shareholders, the Department Director.
    /// </summary>
    [HttpGet("engagements/{id:guid}/approvers")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<RemsApproverList>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Approvers(Guid id, CancellationToken cancellationToken)
    {
        var engagement = await _engagements.GetWithContextAsync(id, cancellationToken);
        if (engagement is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }

        var approvers = await BuildApproverListAsync(engagement, cancellationToken);
        var list = await ToApproverListAsync(engagement, approvers, cancellationToken);
        return Ok(ApiResponseFactory.Success(list, "REMS approvers retrieved."));
    }

    /// <summary>
    /// The users selectable as extra approvers: every active user in the tenant, with their email for
    /// the picker label.
    /// </summary>
    [HttpGet("engagements/{id:guid}/approver-options")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsApproverOption>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ApproverOptions(Guid id, CancellationToken cancellationToken)
    {
        if (await _engagements.GetWithContextAsync(id, cancellationToken) is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return Ok(ApiResponseFactory.Success(Array.Empty<RemsApproverOption>(), "No active tenant."));
        }

        var options = await ApproverOptionsAsync(tenantId, cancellationToken);
        return Ok(ApiResponseFactory.Success(options, "REMS approver options retrieved."));
    }

    /// <summary>Replaces the engagement's ADDED approvers (AC-REMS-018).</summary>
    [HttpPut("engagements/{id:guid}/approvers")]
    [RequirePermission(Permissions.RemsEngagementsManage)]
    [ProducesResponseType<ApiResponse<RemsApproverList>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetApprovers(Guid id, [FromBody] SetRemsApproversRequest request, CancellationToken cancellationToken)
    {
        var engagement = await _engagements.GetWithContextAsync(id, cancellationToken);
        if (engagement is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }
        if (engagement.Status is not (RemsEngagementStatus.Draft or RemsEngagementStatus.Rejected))
        {
            return ConflictResult(CodeApproversLocked, "The approver list is locked once the engagement has been sent for approval.");
        }

        var requested = (request.UserIds ?? new List<Guid>()).Distinct().ToList();

        // Every pick must be one the picker actually offered. Validating against the option set rather than
        // "is a real user" keeps the choice inside this tenant: an arbitrary id must never become a task.
        if (requested.Count > 0)
        {
            if (User.GetActiveTenantId() is not { } tenantId)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant context."));
            }

            var allowed = (await ApproverOptionsAsync(tenantId, cancellationToken)).Select(o => o.UserId).ToHashSet();
            if (requested.Any(uid => !allowed.Contains(uid)))
            {
                return BadRequest(ApiResponseFactory.Error(
                    ApiErrorCodes.ValidationFailed, "Validation failed.", "One or more selected approvers are not active users of this tenant."));
            }
        }

        // Reconcile to exactly the requested set.
        var existing = await _engagements.ListApproversAsync(id, cancellationToken);
        foreach (var row in existing.Where(r => !requested.Contains(r.UserId)))
        {
            _engagements.RemoveApprover(row);
        }
        foreach (var userId in requested.Where(uid => existing.All(r => r.UserId != uid)))
        {
            await _engagements.AddApproverAsync(new REMSEngagementApprover
            {
                Id = Guid.NewGuid(),
                REMSEngagementId = id,
                UserId = userId,
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var approvers = await BuildApproverListAsync(engagement, cancellationToken);
        var list = await ToApproverListAsync(engagement, approvers, cancellationToken);
        return Ok(ApiResponseFactory.Success(list, "REMS approvers updated."));
    }

    // -------------------- Send / resubmit --------------------

    /// <summary>Route the engagement for approval (AC-REMS-018/019).</summary>
    [HttpPost("engagements/{id:guid}/approval/send")]
    [RequirePermission(Permissions.RemsApprovalsSend)]
    [ProducesResponseType<ApiResponse<RemsApproverList>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Send(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var engagement = await _engagements.GetWithContextAsync(id, cancellationToken);
        if (engagement is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }
        if (engagement.Status != RemsEngagementStatus.Draft)
        {
            return ConflictResult(CodeNotSendable, "Only a draft engagement can be sent for approval; a rejected one must be resubmitted.");
        }

        if (await GuardSetupOwnerAsync(engagement, cancellationToken) is { } notOwner)
        {
            return notOwner;
        }

        if (await ValidateApprovalPrerequisitesAsync(engagement, cancellationToken) is { } prereqError)
        {
            return prereqError;
        }

        var approvers = await BuildApproverListAsync(engagement, cancellationToken);
        if (approvers.Count == 0)
        {
            return ConflictResult(CodeNoApprovers, "There are no approvers for this engagement; name a CSE, a department director or a commission recipient first, or add approvers on the Approval tab.");
        }

        await CreateRoundAsync(engagement, approvers, me, isResubmission: false, cancellationToken);

        var list = await ToApproverListAsync(engagement, approvers, cancellationToken);
        return Ok(ApiResponseFactory.Success(list, "REMS engagement sent for approval."));
    }

    /// <summary>Resubmit a rejected engagement (AC-REMS-020): allowed only after a rejected round.</summary>
    [HttpPost("engagements/{id:guid}/approval/resubmit")]
    [RequirePermission(Permissions.RemsApprovalsSend)]
    [ProducesResponseType<ApiResponse<RemsApproverList>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Resubmit(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var engagement = await _engagements.GetWithContextAsync(id, cancellationToken);
        if (engagement is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }
        if (engagement.Status != RemsEngagementStatus.Rejected)
        {
            return ConflictResult(CodeNotRejected, "Only a rejected engagement can be resubmitted.");
        }

        if (await GuardSetupOwnerAsync(engagement, cancellationToken) is { } notOwner)
        {
            return notOwner;
        }

        if (await ValidateApprovalPrerequisitesAsync(engagement, cancellationToken) is { } prereqError)
        {
            return prereqError;
        }

        var approvers = await BuildApproverListAsync(engagement, cancellationToken);
        if (approvers.Count == 0)
        {
            return ConflictResult(CodeNoApprovers, "There are no approvers for this engagement.");
        }

        await CreateRoundAsync(engagement, approvers, me, isResubmission: true, cancellationToken);

        var list = await ToApproverListAsync(engagement, approvers, cancellationToken);
        return Ok(ApiResponseFactory.Success(list, "REMS engagement resubmitted for approval."));
    }

    /// <summary>
    /// The refusal for routing an engagement whose request is not this caller's to work, or null to
    /// carry on.
    /// </summary>
    private async Task<IActionResult?> GuardSetupOwnerAsync(REMSEngagement engagement, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        // An engagement with no request behind it cannot be reasoned about; the loader always supplies one,
        // so this is a guard against a graph nobody expects rather than a case with a rule of its own.
        if (engagement.Rems is not { } rems)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS request not found."));
        }

        return RemsSetupAccess.CanWork(
            User, rems, me, await RemsSetupAccess.CoverForWorkAsync(_delegations, rems, me, cancellationToken))
            ? null
            : StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden(RemsSetupAccess.WorkDeniedReason(rems)));
    }

    // -------------------- Approver's own tasks --------------------

    /// <summary>
    /// Every approval round on an engagement, newest first — who sent it, what each approver
    /// decided, why they declined, how far their checklist got.
    /// </summary>
    [HttpGet("engagements/{id:guid}/approval/history")]
    [Authorize]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsApprovalRoundHistory>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> History(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        if (await _engagements.GetByIdAsync(id, cancellationToken) is not { } engagement)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }

        // The permission first: it is free, and it is what the staff reading this hold. The approver check
        // is a query, so only a caller the permission did not cover pays for it.
        if (!User.HasPermission(Permissions.RemsRequestsRead)
            && !await _approvals.IsApproverOnRequestAsync(engagement.REMSId, me, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("Not permitted to view this engagement's approval history."));
        }

        var rounds = await _approvals.GetRoundsByEngagementAsync(id, cancellationToken);
        var names = await _users.GetFullNamesAsync(
            rounds.SelectMany(r => r.Tasks.Select(t => t.ApproverId)).Concat(rounds.Select(r => r.SentByUserId)),
            cancellationToken);

        string Name(Guid userId) => names.TryGetValue(userId, out var n) ? n : "Unknown user";

        // Newest round first — 3, 2, 1.
        var history = rounds
            .OrderByDescending(r => r.RoundNumber)
            .Select(r =>
            {
                var tasks = r.Tasks.Where(t => !t.Deleted).ToList();
                return new RemsApprovalRoundHistory(
                    r.Id, r.RoundNumber, r.Status.ToString(), r.SentOnUtc, Name(r.SentByUserId), r.CompletedOnUtc,
                    RemsApprovalThreshold.EffectiveFor(tasks.Count),
                    tasks.Count(t => t.Status == RemsApprovalTaskStatus.Rejected),
                    tasks
                        // The firm's own order — shareholder, director, CSE, commission recipient, then
                        // anyone added by hand — and NOT the enum's declaration order.
                        .OrderBy(t => DisplayRank(t.ApproverRole))
                        .ThenBy(t => t.CreatedOnUtc)
                        .Select(t =>
                        {
                            var items = t.ChecklistItems.Where(i => !i.Deleted).ToList();
                            return new RemsApprovalRoundDecision(
                                t.Id, Name(t.ApproverId), t.ApproverRole.ToString(), t.Status.ToString(),
                                t.DecidedOnUtc, t.RejectionReason,
                                items.Count(i => i.IsCompleted), items.Count);
                        })
                        .ToList());
            })
            .ToList();

        return Ok(ApiResponseFactory.Success(history, "REMS approval history retrieved."));
    }

    /// <summary>
    /// The caller's approvals inbox (AC-REMS-019): one row per REQUEST routed to them, paged and filtered
    /// server-side — <paramref name="search"/> over the REMS number and client name, plus optional
    /// <paramref name="role"/> / <paramref name="status"/>.
    /// <para>
    /// A request re-routed after a rejection opens a NEW round with a new task, so a caller asked three
    /// times held three tasks and this listed all three — the round that wanted them buried between two
    /// that were finished. The row is their task on the LATEST round of each request; the rounds before it
    /// are read on the task detail, which lists them under the round being decided.
    /// </para>
    /// <para>
    /// <paramref name="role"/> and <paramref name="status"/> therefore narrow on what the caller is to the
    /// request NOW, not on anything they were on a round that has since been superseded.
    /// </para>
    /// <para>
    /// Each row carries the round's approved/rejected/total counts so the inbox can show its progress — the
    /// row is still the caller's own task, and no other approver's identity is exposed here.
    /// </para>
    /// </summary>
    [HttpGet("approval-tasks")]
    [Authorize]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsApprovalTaskRow>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyTasks(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] string? status = null,
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

        // An unparseable role/status is treated as "no filter" rather than an error: these arrive from a
        // picker, and a stale value should show everything, not fail the page.
        RemsApproverRole? roleFilter = Enum.TryParse<RemsApproverRole>(role, ignoreCase: true, out var r) ? r : null;
        RemsApprovalTaskStatus? statusFilter =
            Enum.TryParse<RemsApprovalTaskStatus>(status, ignoreCase: true, out var s) ? s : null;

        var (tasks, total) = await _approvals.ListTasksByApproverAsync(
            new RemsApprovalTaskQuery(me, search, roleFilter, statusFilter, new SortRequest(sortBy, descending), page, limit), cancellationToken);

        // The audit actors AND each request's CSE, resolved in one read: the CSE is a column on this list
        // now, and a name lookup per row would be one round trip per task.
        var auditNames = await _users.GetFullNamesAsync(
            tasks.SelectMany(t => new[]
                {
                    t.CreatedById, t.UpdatedById, t.Round?.Engagement?.Rems?.CSEId,
                })
                .Where(id => id.HasValue).Select(id => id!.Value),
            cancellationToken);
        string? NameOf(Guid? id) => id is { } uid && auditNames.TryGetValue(uid, out var n) ? n : null;

        var rows = tasks.Select(t =>
        {
            var round = t.Round!;
            var engagement = round.Engagement!;
            var rems = engagement.Rems!;
            var client = ClientOf(engagement);
            return new RemsApprovalTaskRow(
                t.Id, round.Id, round.RoundNumber, t.ApproverRole.ToString(), t.Status.ToString(),
                round.SentOnUtc, t.DecidedOnUtc, round.Status.ToString(),
                // The client's name as they gave it on intake.
                engagement.Id, rems.Id, rems.REMSNumber,
                rems.WithClientSuffix(client?.Name),
                rems.ClientNameSuffix,
                RemsWorkspaceMapper.UserRef(rems.CSEId, auditNames),
                round.Tasks.Count(x => x.Status == RemsApprovalTaskStatus.Approved),
                round.Tasks.Count(x => x.Status == RemsApprovalTaskStatus.Rejected),
                round.Tasks.Count,
                NameOf(t.CreatedById), t.CreatedOnUtc, NameOf(t.UpdatedById), t.UpdatedOnUtc);
        });
        return Ok(ApiResponseFactory.Paginated(rows, "REMS approval tasks retrieved.", page, limit, total));
    }

    /// <summary>
    /// The caller's own approval task on a REMS request, so an approver following a notification lands
    /// on the task rather than on the request.
    /// </summary>
    [HttpGet("approval-tasks/for-request/{remsId:guid}")]
    [Authorize]
    [ProducesResponseType<ApiResponse<RemsApprovalTaskRef>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyTaskForRequest(Guid remsId, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var taskId = await _approvals.GetCurrentTaskIdOnRequestAsync(remsId, me, cancellationToken);
        if (taskId is not { } id)
        {
            return NotFound(ApiResponseFactory.NotFound("You hold no approval task on this request."));
        }

        return Ok(ApiResponseFactory.Success(new RemsApprovalTaskRef(id), "Approval task resolved."));
    }

    /// <summary>
    /// The caller's own task with the full review packet (AC-REMS-019.9): the originating request, the
    /// client, the entity under review.
    /// </summary>
    [HttpGet("approval-tasks/{taskId:guid}")]
    [Authorize]
    [ProducesResponseType<ApiResponse<RemsApprovalTaskView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTask(Guid taskId, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var task = await _approvals.GetTaskWithContextAsync(taskId, cancellationToken);
        // Record-level: an approver may read ONLY their own task; anything else is a 404 (never revealed).
        if (task is null || task.ApproverId != me)
        {
            return NotFound(ApiResponseFactory.NotFound("Approval task not found."));
        }

        var view = await BuildTaskViewAsync(task, cancellationToken);
        return Ok(ApiResponseFactory.Success(view, "REMS approval task retrieved."));
    }

    /// <summary>Check / uncheck a checklist item on the caller's own task (AC-REMS-019).</summary>
    [HttpPut("approval-tasks/{taskId:guid}/checklist/{itemId:guid}")]
    [Authorize]
    [ProducesResponseType<ApiResponse<RemsChecklistItemView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetChecklistItem(
        Guid taskId, Guid itemId, [FromBody] SetChecklistItemRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var task = await _approvals.GetTaskByIdAsync(taskId, cancellationToken);
        if (task is null || task.ApproverId != me)
        {
            return NotFound(ApiResponseFactory.NotFound("Approval task not found."));
        }
        if (task.Status != RemsApprovalTaskStatus.Pending)
        {
            return ConflictResult(CodeTaskDecided, "This task has already been decided.");
        }

        var item = task.ChecklistItems.FirstOrDefault(i => i.Id == itemId && !i.Deleted);
        if (item is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Checklist item not found."));
        }

        item.IsCompleted = request.IsCompleted;
        item.CompletedOnUtc = request.IsCompleted ? DateTime.UtcNow : null;
        _approvals.UpdateChecklistItem(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = new RemsChecklistItemView(item.Id, item.DisplayOrder, item.Label, item.IsCompleted, item.CompletedOnUtc);
        return Ok(ApiResponseFactory.Success(view, "Checklist item updated."));
    }

    /// <summary>Approve the caller's own task (AC-REMS-019).</summary>
    [HttpPost("approval-tasks/{taskId:guid}/approve")]
    [Authorize]
    [ProducesResponseType<ApiResponse<RemsApprovalTaskView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid taskId, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var task = await _approvals.GetTaskWithContextAsync(taskId, cancellationToken);
        if (task is null || task.ApproverId != me)
        {
            return NotFound(ApiResponseFactory.NotFound("Approval task not found."));
        }

        var round = task.Round!;
        if (task.Status != RemsApprovalTaskStatus.Pending)
        {
            return ConflictResult(CodeTaskDecided, "This task has already been decided.");
        }
        if (round.Status != RemsApprovalRoundStatus.Pending)
        {
            return ConflictResult(CodeRoundClosed, "This approval is already closed.");
        }

        // Re-verify server-side that every checklist item is completed (AC-REMS-019.7/8).
        if (task.ChecklistItems.Any(i => !i.Deleted && !i.IsCompleted))
        {
            return ConflictResult(CodeChecklistIncomplete, "All checklist items must be completed before approving.");
        }

        var now = DateTime.UtcNow;
        var engagement = round.Engagement!;
        var rems = engagement.Rems!;

        task.Status = RemsApprovalTaskStatus.Approved;
        task.DecidedOnUtc = now;
        _approvals.UpdateTask(task);
        await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, rems.Id, ActivityEventTypes.RemsApproved, null, task.ApproverRole.ToString()), cancellationToken);

        // The round is settled once nobody is still deciding.
        var fullyApproved = round.Tasks.All(t => t.Id == task.Id || t.Status != RemsApprovalTaskStatus.Pending);
        if (fullyApproved)
        {
            round.Status = RemsApprovalRoundStatus.Approved;
            round.CompletedOnUtc = now;
            _approvals.UpdateRound(round);

            engagement.Status = RemsEngagementStatus.Approved;
            _engagements.Update(engagement);
            await SyncRequestStatusAsync(rems, engagement, cancellationToken);

            var involved = new HashSet<Guid>(round.Tasks.Select(t => t.ApproverId)) { round.SentByUserId };
            if (rems.CSEId is { } cse)
            {
                involved.Add(cse);
            }
            // Final approval is the outcome the requester has been waiting for since they submitted.
            // (Rejections stay internal — they are a rework loop between staff, not a status for the requester.)
            if (rems.CreatedById is { } requester)
            {
                involved.Add(requester);
            }
            foreach (var userId in involved)
            {
                await _notifications.DispatchAsync(new CreateNotificationDto(
                    userId, NotificationType.RemsEngagementApproved,
                    "A REMS engagement was fully approved",
                    $"{rems.REMSNumber} — {rems.ClientDisplayName}", EntityType.Rems, rems.Id), cancellationToken);
            }
            await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, rems.Id, ActivityEventTypes.RemsFullyApproved), cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await BuildTaskViewAsync(task, cancellationToken);
        return Ok(ApiResponseFactory.Success(view, fullyApproved ? "Task approved; engagement fully approved." : "Task approved."));
    }

    /// <summary>Decline the caller's own task with a required reason (AC-REMS-020).</summary>
    [HttpPost("approval-tasks/{taskId:guid}/reject")]
    [Authorize]
    [ProducesResponseType<ApiResponse<RemsApprovalTaskView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid taskId, [FromBody] RejectApprovalTaskRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var task = await _approvals.GetTaskWithContextAsync(taskId, cancellationToken);
        if (task is null || task.ApproverId != me)
        {
            return NotFound(ApiResponseFactory.NotFound("Approval task not found."));
        }

        var round = task.Round!;
        if (task.Status != RemsApprovalTaskStatus.Pending)
        {
            return ConflictResult(CodeTaskDecided, "This task has already been decided.");
        }
        if (round.Status != RemsApprovalRoundStatus.Pending)
        {
            return ConflictResult(CodeRoundClosed, "This approval is already closed.");
        }

        var now = DateTime.UtcNow;
        var reason = request.Reason.Trim();
        var engagement = round.Engagement!;
        var rems = engagement.Rems!;

        task.Status = RemsApprovalTaskStatus.Rejected;
        task.DecidedOnUtc = now;
        task.RejectionReason = reason;
        _approvals.UpdateTask(task);
        await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, rems.Id, ActivityEventTypes.RemsRejected, null, reason), cancellationToken);

        // Counted over the round's own tasks, with this one substituted: it is not committed yet, so the
        // stored copy still reads Pending.
        var declines = round.Tasks.Count(t => t.Id == task.Id || t.Status == RemsApprovalTaskStatus.Rejected);
        var threshold = RemsApprovalThreshold.EffectiveFor(round.Tasks.Count);
        var closesRound = declines >= threshold;

        // Inert while the threshold is one decline, and deliberately kept: what decides is the threshold,
        // not this branch. Raise RemsApprovalThreshold.Declines and a round has to stay open again.
        if (!closesRound)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var openView = await BuildTaskViewAsync(task, cancellationToken);
            return Ok(ApiResponseFactory.Success(
                openView,
                $"Declined. {declines} of {threshold} declines needed to send this back — the approval is still open."));
        }

        round.Status = RemsApprovalRoundStatus.Rejected;
        round.CompletedOnUtc = now;
        round.RejectionReason = reason;
        _approvals.UpdateRound(round);

        // Everyone who had not decided by the time the round closed. Their task is over, but they did not
        // decline it — Superseded is the difference.
        foreach (var pending in round.Tasks.Where(t => t.Id != task.Id && t.Status == RemsApprovalTaskStatus.Pending))
        {
            pending.Status = RemsApprovalTaskStatus.Superseded;
            pending.DecidedOnUtc = now;
            _approvals.UpdateTask(pending);
        }

        engagement.Status = RemsEngagementStatus.Rejected;
        _engagements.Update(engagement);
        await SyncRequestStatusAsync(rems, engagement, cancellationToken);

        // Every decline's own reason, so the notice explains the round rather than only its last vote.
        var reasons = round.Tasks
            .Where(t => t.Id == task.Id || t.Status == RemsApprovalTaskStatus.Rejected)
            .Select(t => t.Id == task.Id ? reason : t.RejectionReason)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();

        // The initiator owns the rework now, so they are told first; the sender and CSE still need to know
        // the round is over.
        var recipients = new HashSet<Guid> { round.SentByUserId };
        if (rems.CSEId is { } cse)
        {
            recipients.Add(cse);
        }
        if (rems.CreatedById is { } initiator)
        {
            recipients.Add(initiator);
        }
        if (rems.AdminAssignedToId is { } admin)
        {
            recipients.Add(admin);
        }

        // And everyone the round was routed to — the shareholders, the department director, the CSE and the
        // commission recipients, plus anyone added by hand.
        foreach (var approverId in round.Tasks.Select(t => t.ApproverId))
        {
            recipients.Add(approverId);
        }
        foreach (var userId in recipients)
        {
            await _notifications.DispatchAsync(new CreateNotificationDto(
                userId, NotificationType.RemsEngagementRejected,
                "A REMS engagement was declined",
                $"{rems.REMSNumber} — {rems.ClientDisplayName}: {string.Join(" | ", reasons)}", EntityType.Rems, rems.Id), cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await BuildTaskViewAsync(task, cancellationToken);
        return Ok(ApiResponseFactory.Success(view, "Declined. The request is back with its initiator."));
    }

    // -------------------- Request-status roll-up --------------------

    /// <summary>Re-derives the REQUEST status from the engagements underneath.</summary>
    private async Task SyncRequestStatusAsync(REMS rems, REMSEngagement current, CancellationToken cancellationToken)
    {
        var code = current.Status switch
        {
            RemsEngagementStatus.Approved => RemsRequestStatuses.Approved,
            RemsEngagementStatus.Rejected => RemsRequestStatuses.ChangesRequested,
            RemsEngagementStatus.PendingApproval => RemsRequestStatuses.PendingApproval,
            // Back to Draft only happens if an engagement is reset, which nothing does today. Admin Review
            // is where such a request belongs: the client's answers are in and somebody has to look at it.
            _ => RemsRequestStatuses.AdminReview,
        };

        // The status column is a foreign key now, so the transition names a CODE and this resolves it to
        // the item the tenant's own REMS.Status list holds for it.
        rems.StatusId = await _codes.RequireRemsIdAsync(RemsOptionSetKeys.Status, code, cancellationToken);
    }

    // -------------------- Approver-list generation --------------------

    /// <summary>The firm's shareholders: everyone holding the <c>Shareholder</c> role in the caller's tenant.</summary>
    private async Task<IReadOnlyList<Guid>> ShareholderIdsAsync(CancellationToken cancellationToken)
    {
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return Array.Empty<Guid>();
        }

        var holders = await _users.ListByTenantRolesAsync(
            tenantId, new[] { Roles.Shareholder }, cancellationToken);
        return holders.Select(u => u.Id).Distinct().ToList();
    }

    /// <summary>
    /// The approver set to route to (AC-REMS-018): the automatic approvers, plus whoever was added on
    /// the Approval tab.
    /// </summary>
    private async Task<IReadOnlyList<(Guid UserId, RemsApproverRole Role)>> BuildApproverListAsync(
        REMSEngagement engagement, CancellationToken cancellationToken)
    {
        var shareholders = await ShareholderIdsAsync(cancellationToken);
        var picked = await _engagements.ListApproversAsync(engagement.Id, cancellationToken);

        // Added approvers come ON TOP of the automatic ones — picking somebody never removes an approver
        // who holds their place by standing on the engagement.
        var userIds = AutomaticApproverIds(engagement, shareholders).Concat(picked.Select(a => a.UserId));

        return userIds
            .Distinct()
            .Select(userId => (UserId: userId, Role: RoleFor(engagement, shareholders, userId)))
            .OrderBy(a => DisplayRank(a.Role))
            .ToList();
    }

    /// <summary>Where each role sits in the approver list, most senior first.</summary>
    private static int DisplayRank(RemsApproverRole role) => role switch
    {
        RemsApproverRole.Shareholder => 0,
        RemsApproverRole.DepartmentDirector => 1,
        RemsApproverRole.CSE => 2,
        RemsApproverRole.CommissionRecipient => 3,
        _ => 4,
    };

    /// <summary>The users the Approval tab offers as EXTRA approvers: EVERY active user in the tenant.</summary>
    private async Task<IReadOnlyList<RemsApproverOption>> ApproverOptionsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var candidates = await _users.ListActiveByTenantAsync(tenantId, cancellationToken);
        var names = await _users.GetFullNamesAsync(candidates.Select(u => u.Id), cancellationToken);

        return candidates
            .Select(u => new RemsApproverOption(
                u.Id,
                names.TryGetValue(u.Id, out var n) ? n : u.DisplayName,
                u.Email,
                // The roles held HERE. The repository loads each user's assignments already filtered to this
                // tenant, so another firm's roles cannot leak into the label.
                u.TenantRoles
                    .Where(r => !r.Deleted && r.TenantId == tenantId)
                    .Select(r => r.RoleEntity?.Name ?? r.Role.ToString())
                    .Distinct()
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .OrderBy(o => o.Name)
            .ToList();
    }

    /// <summary>The client materialised from the request's intake, or null before the client has submitted.</summary>
    private static REMSClient? ClientOf(REMSEngagement engagement)
        => engagement.Rems?.Clients.FirstOrDefault(c => !c.Deleted);

    /// <summary>
    /// The approvers every engagement routes to whatever is picked on the Approval tab: the firm's
    /// shareholders, the Department Director and CSE from the engagement's own setup.
    /// </summary>
    private static List<Guid> AutomaticApproverIds(REMSEngagement engagement, IReadOnlyList<Guid> shareholderIds)
    {
        var ids = new List<Guid>(shareholderIds);
        if (engagement.DepartmentDirectorId is { } director)
        {
            ids.Add(director);
        }
        if (engagement.Rems?.CSEId is { } cse)
        {
            ids.Add(cse);
        }
        ids.AddRange(engagement.CommissionSplits.Where(s => !s.Deleted).Select(s => s.EmployeeId));
        return ids;
    }

    /// <summary>The role a user acts under on this engagement, most specific first.</summary>
    private static RemsApproverRole RoleFor(
        REMSEngagement engagement, IReadOnlyList<Guid> shareholderIds, Guid userId)
    {
        if (engagement.Rems?.CSEId == userId)
        {
            return RemsApproverRole.CSE;
        }
        if (engagement.DepartmentDirectorId == userId)
        {
            return RemsApproverRole.DepartmentDirector;
        }
        if (engagement.CommissionSplits.Any(s => !s.Deleted && s.EmployeeId == userId))
        {
            return RemsApproverRole.CommissionRecipient;
        }
        if (shareholderIds.Contains(userId))
        {
            return RemsApproverRole.Shareholder;
        }
        return RemsApproverRole.Approver;
    }

    private async Task<RemsApproverList> ToApproverListAsync(
        REMSEngagement engagement, IReadOnlyList<(Guid UserId, RemsApproverRole Role)> approvers,
        CancellationToken cancellationToken)
    {
        var names = await _users.GetFullNamesAsync(approvers.Select(a => a.UserId), cancellationToken);
        var suggestions = approvers
            .Select(a => new RemsApproverSuggestion(
                new RemsUserRef(a.UserId, names.TryGetValue(a.UserId, out var n) ? n : string.Empty), a.Role.ToString()))
            .ToList();

        // Only the approvers somebody ADDED. The automatic ones — shareholders, director, CSE, commission
        // recipients — are left out: the picker must not show back somebody who is on the list anyway.
        var selected = (await _engagements.ListApproversAsync(engagement.Id, cancellationToken))
            .Select(a => a.UserId)
            .ToList();

        return new RemsApproverList(engagement.Id, engagement.Status.ToString(), suggestions, selected);
    }

    /// <summary>
    /// Validates the pre-approval requirements: the core setup; a marketing tag; a commission split
    /// that adds up to 100% where there is one at all.
    /// </summary>
    private async Task<IActionResult?> ValidateApprovalPrerequisitesAsync(REMSEngagement engagement, CancellationToken cancellationToken)
    {
        // The engagement's core placement + team + realization are mandatory. The workspace enforces this
        // on its Setup step too; this is the backstop for anything reaching the API another way.
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(engagement.Department?.Value)) missing.Add("Department");
        // The service the firm is actually engaged to do.
        if (string.IsNullOrWhiteSpace(engagement.ServiceLine?.Value)) missing.Add("Service Line");
        if (engagement.EngagementExecutiveId is null) missing.Add("Engagement Executive");
        if (engagement.BillingManagerId is null) missing.Add("Billing Manager");
        if (engagement.RealizationPercentage is null) missing.Add("% Realization");
        if (missing.Count > 0)
        {
            return ConflictResult(CodeSetupIncomplete, $"Complete the engagement setup first — missing: {string.Join(", ", missing)}.");
        }

        if (!engagement.MarketingMethods.Any(m => !m.Deleted))
        {
            return ConflictResult(CodeMarketingRequired, "At least one marketing tag is required before sending for approval.");
        }

        // The commission has to be settled before it is signed off.
        var allocated = Math.Round(
            engagement.CommissionSplits.Where(s => !s.Deleted).Sum(s => s.CommissionPercentage),
            2, MidpointRounding.AwayFromZero);
        if (allocated != 100m)
        {
            return ConflictResult(
                CodeCommissionNotFullyAllocated,
                $"Commission totals {allocated:0.##}% — the recipients must add up to 100% before this " +
                "engagement can be sent for approval.");
        }

        // Audit AND Assurance: the client-acceptance form is the same compliance artifact under both, and a
        // card that shows it without anything enforcing it is a card people learn to scroll past.
        if (RemsEngagementCodes.RequiresClientAcceptanceForm(engagement.Department?.Value))
        {
            var audit = await _engagements.GetAuditDetailAsync(engagement.Id, cancellationToken);
            if (audit?.ClientAcceptanceFormMediaId is null)
            {
                return ConflictResult(CodeCafRequired,
                    $"A signed client-acceptance form is required for {(RemsEngagementCodes.IsAssurance(engagement.Department?.Value) ? "an assurance" : "an audit")} engagement.");
            }
        }

        // The entity type lives on the request's FORM record, not on the engagement, so it takes a read of
        // its own. Same source the workspace and the approval packet show it from.
        var entityType = (await _rems.GetFormStatesAsync(new[] { engagement.REMSId }, cancellationToken))
            .FirstOrDefault()?.EntityType;
        if (RemsEngagementCodes.IsGovernmentAudit(engagement.Department?.Value, entityType))
        {
            var government = await _engagements.GetGovernmentDetailAsync(engagement.Id, cancellationToken);
            if (government is null || string.IsNullOrWhiteSpace(government.ContractNumber) || government.FloridaOnePercentStateFeeApplies is null)
            {
                return ConflictResult(CodeGovDetailRequired, "A government audit requires a contract number and the Florida 1% state-fee flag.");
            }
        }

        return null;
    }

    // -------------------- Round + task + checklist creation --------------------

    /// <summary>
    /// Creates a new approval round with a pending task per approver (each with its role checklist),
    /// locks the list, sets the engagement to PendingApproval, notifies every approver once.
    /// </summary>
    private async Task CreateRoundAsync(
        REMSEngagement engagement, IReadOnlyList<(Guid UserId, RemsApproverRole Role)> approvers,
        Guid actorId, bool isResubmission, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var rems = engagement.Rems!;

        var roundNumber = await _approvals.GetNextRoundNumberAsync(engagement.Id, cancellationToken);
        var round = new REMSApprovalRound
        {
            Id = Guid.NewGuid(),
            REMSEngagementId = engagement.Id,
            RoundNumber = roundNumber,
            Status = RemsApprovalRoundStatus.Pending,
            SentOnUtc = now,
            SentByUserId = actorId,
        };
        await _approvals.AddRoundAsync(round, cancellationToken);

        foreach (var (userId, role) in approvers)
        {
            var task = new REMSApprovalTask
            {
                Id = Guid.NewGuid(),
                REMSApprovalRoundId = round.Id,
                ApproverId = userId,
                ApproverRole = role,
                Status = RemsApprovalTaskStatus.Pending,
            };
            await _approvals.AddTaskAsync(task, cancellationToken);

            var order = 1;
            foreach (var label in RemsApprovalChecklistCatalog.For(role))
            {
                await _approvals.AddChecklistItemAsync(new REMSApprovalChecklistItem
                {
                    Id = Guid.NewGuid(),
                    REMSApprovalTaskId = task.Id,
                    DisplayOrder = order++,
                    Label = label,
                    IsCompleted = false,
                }, cancellationToken);
            }
        }

        engagement.Status = RemsEngagementStatus.PendingApproval;
        _engagements.Update(engagement);
        await SyncRequestStatusAsync(rems, engagement, cancellationToken);

        // Notify every approver (once per user, even if they hold multiple role tasks).
        foreach (var userId in approvers.Select(a => a.UserId).Distinct())
        {
            await _notifications.DispatchAsync(new CreateNotificationDto(
                userId, NotificationType.RemsApprovalRequested,
                isResubmission ? "A REMS engagement was resubmitted for your approval" : "A REMS engagement needs your approval",
                $"{rems.REMSNumber} — {rems.ClientDisplayName}", EntityType.Rems, rems.Id), cancellationToken);
        }

        var listText = string.Join(", ", approvers.Select(a => $"{a.UserId}:{a.Role}"));
        await _activity.WriteAsync(new CreateActivityEventDto(
            EntityType.Rems, rems.Id,
            isResubmission ? ActivityEventTypes.RemsApprovalResubmitted : ActivityEventTypes.RemsApprovalSent,
            null, listText), cancellationToken);

        // One atomic commit: round + tasks + checklists + engagement status + notifications + activity.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // -------------------- Task review packet --------------------

    /// <summary>
    /// Builds the approver's review packet: the request that started it, the client, the entity under
    /// review with its addresses and contacts.
    /// </summary>
    private async Task<RemsApprovalTaskView> BuildTaskViewAsync(REMSApprovalTask task, CancellationToken cancellationToken)
    {
        var round = task.Round!;
        var engagement = round.Engagement!;
        var client = ClientOf(engagement)!;

        // The task-context graph stops at the client's entities: their addresses/contacts, the conditional
        // engagement detail and the request's files each need their own load.
        var mainEntity = client.Entities.FirstOrDefault(e => !e.Deleted && e.IsMainEntity)
            ?? client.Entities.FirstOrDefault(e => !e.Deleted);
        var entity = mainEntity is null
            ? null
            : await _clients.GetEntityAsync(mainEntity.Id, cancellationToken) ?? mainEntity;
        var rems = await _rems.GetByIdAsync(client.REMSId, cancellationToken) ?? engagement.Rems!;
        var audit = await _engagements.GetAuditDetailAsync(engagement.Id, cancellationToken);
        var government = await _engagements.GetGovernmentDetailAsync(engagement.Id, cancellationToken);
        var tax = await _engagements.GetTaxDetailAsync(engagement.Id, cancellationToken);
        var formState = (await _rems.GetFormStatesAsync(new[] { rems.Id }, cancellationToken)).FirstOrDefault();

        var names = await _users.GetFullNamesAsync(
            EngagementUserIds(engagement)
                .Concat(round.Tasks.Select(t => t.ApproverId))
                .Append(round.SentByUserId)
                .Concat(new[] { rems.AdminAssignedToId, rems.CSEId, rems.CreatedById, task.CreatedById, task.UpdatedById }
                    .Where(id => id.HasValue).Select(id => id!.Value)),
            cancellationToken);

        var (emsFormState, clientSubmissionState) = RemsWorkspaceMapper.FormState(formState);
        var requestView = new RemsApprovalRequestView(
            rems.Id, rems.REMSNumber, rems.Description,
            rems.ClientDisplayName, rems.ClientNameSuffix,
            rems.Type!.Value, rems.Status!.Value, rems.CustomerEmail, rems.CustomerMobileNumber,
            formState?.EntityType, emsFormState, clientSubmissionState,
            RemsWorkspaceMapper.UserRef(rems.AdminAssignedToId, names),
            RemsWorkspaceMapper.UserRef(rems.CSEId, names),
            rems.CreatedById is { } creator && names.TryGetValue(creator, out var creatorName) ? creatorName : null,
            rems.CreatedOnUtc,
            rems.Files.Where(f => !f.Deleted)
                .Select(f => new RemsFileRef(
                    f.Id, f.MediaId, f.Media?.OriginalFileName, f.Media?.MimeType, f.Media?.FileSize, f.Media?.PublicUrl))
                .ToList());

        var engagementView = await BuildApprovalEngagementViewAsync(
            task, rems, engagement, client, entity, audit, government, tax, names, cancellationToken);

        // By ROLE — shareholder, director, CSE, commission recipient, then anyone added by hand — which is
        // the order the Approval tab lists and the order the history reads in.
        var decisions = round.Tasks
            .OrderBy(t => DisplayRank(t.ApproverRole))
            .ThenBy(t => t.CreatedOnUtc)
            .Select(t => new RemsApprovalDecisionView(
                t.Id, RemsWorkspaceMapper.UserRef(t.ApproverId, names)!, t.ApproverRole.ToString(),
                t.Status.ToString(), t.DecidedOnUtc, t.RejectionReason, t.Id == task.Id))
            .ToList();

        var roundView = new RemsApprovalRoundView(
            round.Id, round.RoundNumber, round.Status.ToString(), round.SentOnUtc,
            RemsWorkspaceMapper.UserRef(round.SentByUserId, names), round.CompletedOnUtc, round.RejectionReason,
            decisions);

        var checklist = task.ChecklistItems
            .Where(i => !i.Deleted)
            .OrderBy(i => i.DisplayOrder)
            .Select(i => new RemsChecklistItemView(i.Id, i.DisplayOrder, i.Label, i.IsCompleted, i.CompletedOnUtc))
            .ToList();

        var canDecide = task.Status == RemsApprovalTaskStatus.Pending && round.Status == RemsApprovalRoundStatus.Pending;

        return new RemsApprovalTaskView(
            task.Id, round.Id, round.RoundNumber, task.ApproverRole.ToString(), task.Status.ToString(),
            task.DecidedOnUtc, task.RejectionReason, canDecide, checklist, requestView, engagementView, roundView,
            RecordAudit.From(task, RecordAudit.Names(names)));
    }

    private async Task<RemsApprovalEngagementView> BuildApprovalEngagementViewAsync(
        REMSApprovalTask task,
        // The originating request. Needed for the client's generational suffix, which lives on the
        // request and on nothing the client themselves filled in — see REMS.WithClientSuffix.
        REMS rems,
        REMSEngagement engagement,
        REMSClient client,
        REMSEntity entity,
        REMSEngagementAuditDetail? audit,
        REMSEngagementGovernmentDetail? government,
        REMSEngagementTaxDetail? tax,
        IReadOnlyDictionary<Guid, string> names,
        CancellationToken cancellationToken)
    {
        var maySeeFinancials = MaySeeFinancials(task.ApproverRole);

        var marketingIds = engagement.MarketingMethods.Where(m => !m.Deleted).Select(m => m.MarketingMethodId).ToList();
        var marketing = await ResolveOptionRefsAsync(MarketingSetKey, marketingIds, cancellationToken);

        RemsApprovalAuditDetailView? auditView = null;
        if (audit is not null)
        {
            // Resolve the signed CAF to something the approver can actually open — "a media id is on file"
            // is not a document anyone can review.
            var media = audit.ClientAcceptanceFormMediaId is { } mediaId
                ? await _media.GetByIdAsync(mediaId, cancellationToken)
                : null;
            auditView = new RemsApprovalAuditDetailView(
                audit.Id, audit.ClientAcceptanceFormMediaId, media?.OriginalFileName, media?.PublicUrl,
                audit.ClientFiscalYearEnd, audit.AdminFeesApply, audit.AdminFeesAmount);
        }

        RemsApprovalTaxDetailView? taxView = null;
        if (tax is not null)
        {
            var taxFormIds = tax.TaxForms.Where(f => !f.Deleted).Select(f => f.TaxFormId).ToList();
            // The stored schedule, not a fresh calculation: the two dates are editable now, and an approver has
            // to read the ones the engagement was actually sent with.
            var schedule = RemsTaxDueDates.TryDeserialize(tax.CalculatedDueDates);
            if (tax.FiscalYearEnd is { } taxFye && (tax.OriginalDueDate is not null || tax.FirstExtensionDueDate is not null))
            {
                schedule = RemsTaxDueDates.Effective(taxFye, tax.OriginalDueDate, tax.FirstExtensionDueDate);
            }
            taxView = new RemsApprovalTaxDetailView(
                tax.Id, tax.FiscalYearEnd, schedule,
                await ResolveOptionRefsAsync(TaxFormSetKey, taxFormIds, cancellationToken));
        }

        // The client's name, and the MAIN entity's, read with the request's generational suffix on them.
        string EntityName(REMSEntity e) => e.IsMainEntity ? rems.WithClientSuffix(e.Name) : e.Name;

        var clientView = new RemsApprovalClientView(
            client.Id, rems.WithClientSuffix(client.Name), client.Email, client.MobileNumber,
            client.ReferralSource?.Value,
            client.BillingContactName, client.BillingEmail,
            client.Entities
                .Where(e => !e.Deleted)
                .OrderByDescending(e => e.IsMainEntity)
                .ThenBy(e => e.Name)
                .Select(e => new RemsApprovalEntitySummary(e.Id, EntityName(e), e.EIN, e.IsMainEntity))
                .ToList());

        var entityView = new RemsApprovalEntityView(
            entity.Id, EntityName(entity), entity.EIN, entity.IsMainEntity,
            entity.Addresses.Where(a => !a.Deleted)
                .Select(a => new RemsEntityAddressView(a.Id, a.AddressType.ToString(), RemsWorkspaceMapper.Address(a.Address)!))
                .ToList(),
            entity.Contacts.Where(c => !c.Deleted)
                .Select(c => new RemsEntityContactView(
                    c.Id, c.ContactRole, c.IsRequired, c.Person?.DisplayName, c.Person?.PrimaryEmail,
                    c.Person?.MobileNumber, c.Person?.Suffix))
                .ToList());

        return new RemsApprovalEngagementView(
            engagement.Id,
            engagement.Status.ToString(),
            engagement.Department?.Value,
            engagement.ServiceLine?.Value,
            engagement.Industry?.Value,
            clientView,
            entityView,
            RemsWorkspaceMapper.UserRef(engagement.DepartmentDirectorId, names),
            RemsWorkspaceMapper.UserRef(engagement.EngagementExecutiveId, names),
            RemsWorkspaceMapper.UserRef(engagement.BillingManagerId, names),
            maySeeFinancials ? engagement.FirstYearFeeEstimate : null,
            maySeeFinancials ? engagement.EngagementFee : null,
            maySeeFinancials ? engagement.RealizationPercentage : null,
            FinancialsRestricted: !maySeeFinancials,
            // Not withheld from any role: how often a client is invoiced and how the billing runs are terms
            // of the arrangement, not the fee.
            engagement.BillingPeriod?.Value,
            engagement.BillingProcessDescription,
            auditView,
            government is null
                ? null
                : new RemsGovernmentDetailView(
                    government.Id, government.ContractNumber, government.FloridaOnePercentStateFeeApplies,
                    government.ContractStartDate, government.ContractEndDate, government.OriginalTerm,
                    government.RenewalTerms, government.PurchaseOrderStartDate, government.PurchaseOrderEndDate,
                    government.PurchaseOrderNumber,
                    // The PO's value is money, so it is withheld from a role that may not see the fee.
                    maySeeFinancials ? government.PurchaseOrderAmount : null,
                    government.PurchaseOrderMediaId, government.PurchaseOrderMedia?.OriginalFileName,
                    government.PersonnelLevel?.Value,
                    maySeeFinancials ? government.BillRatePerHour : null),
            taxView,
            marketing,
            engagement.CommissionSplits
                .Where(s => !s.Deleted)
                .Select(s => new RemsCommissionSplitView(s.Id, RemsWorkspaceMapper.UserRef(s.EmployeeId, names)!, s.CommissionPercentage))
                .ToList());
    }

    /// <summary>Whether a role sees the first-year fee estimate and % realization.</summary>
    private static bool MaySeeFinancials(RemsApproverRole role)
        => role is RemsApproverRole.DepartmentDirector;

    /// <summary>
    /// Resolves option-set item ids to their labels (and, for marketing, the group tag from
    /// MetadataJson), preserving the set's sort order.
    /// </summary>
    private async Task<IReadOnlyList<RemsApprovalOptionRef>> ResolveOptionRefsAsync(
        string setKey, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<RemsApprovalOptionRef>();
        }

        var set = await _optionSets.GetEffectiveSetAsync(User.GetActiveTenantId(), EntityType.Rems, setKey, cancellationToken);
        if (set is null)
        {
            return Array.Empty<RemsApprovalOptionRef>();
        }

        return set.Items
            .Where(i => !i.Deleted && ids.Contains(i.Id))
            .OrderBy(i => i.SortOrder)
            .Select(i => new RemsApprovalOptionRef(i.Id, i.Label, GroupOf(i.MetadataJson)))
            .ToList();
    }

    /// <summary>The <c>group</c> tag a marketing option carries in its metadata (null for sets without one).</summary>
    private static string? GroupOf(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            return doc.RootElement.TryGetProperty("group", out var group) ? group.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyCollection<Guid> EngagementUserIds(REMSEngagement engagement)
    {
        var ids = new HashSet<Guid>();
        if (engagement.DepartmentDirectorId is { } d) ids.Add(d);
        if (engagement.EngagementExecutiveId is { } x) ids.Add(x);
        if (engagement.BillingManagerId is { } b) ids.Add(b);
        foreach (var s in engagement.CommissionSplits.Where(s => !s.Deleted))
        {
            ids.Add(s.EmployeeId);
        }
        return ids;
    }

    private IActionResult ConflictResult(string code, string message)
        => StatusCode(StatusCodes.Status409Conflict, ApiResponseFactory.Error(code, message, message));
}
