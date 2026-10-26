using System.Text.Json;
using EmsPortal.Api.Approval;
using EmsPortal.Api.Filters;
using EmsPortal.Api.Models;
using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
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
using Microsoft.AspNetCore.RateLimiting;
using EmsPortal.Api.RateLimiting;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// REMS approval workflow backend (WO-114 Part C). The engagement is the request now, so every
/// <c>engagements/{id}</c> route takes the REMS id, and a round is the tasks that share a round number.
/// </summary>
[ApiController]
[LoadMaconomyClients]
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
    // STATIC-APPROVAL-POLICY
    private const string CodeRouteBlocked = "REMS_ROUTE_BLOCKED";
    private const string CodeApproverReserved = "REMS_APPROVER_RESERVED";

    private const string MarketingSetKey = "REMSMarketing_MarketingMethods.MarketingMethodId";
    private const string TaxFormSetKey = "REMS.TaxForm";

    private readonly IRemsRequestRepository _requests;
    /// <summary>Only to answer whether an initiator has cover arranged — see RemsSetupAccess.CanWork.</summary>
    private readonly IRemsDelegationRepository _delegations;
    private readonly IRemsApprovalTaskRepository _tasks;
    private readonly IClientRepository _clients;
    private readonly IRemsSettingsRepository _settings;
    private readonly IOptionSetRepository _optionSets;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActivityEventWriter _activity;
    private readonly INotificationDispatcher _notifications;
    private readonly IOptionCodeResolver _codes;
    // STATIC-APPROVAL-POLICY
    private readonly IRemsApprovalPolicy _policy;
    private readonly IMaconomyCustomerSyncService _customerSync;

    public RemsApprovalController(
        IRemsRequestRepository requests,
        IRemsDelegationRepository delegations,
        IRemsApprovalTaskRepository tasks,
        IClientRepository clients,
        IRemsSettingsRepository settings,
        IOptionSetRepository optionSets,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IActivityEventWriter activity,
        INotificationDispatcher notifications,
        IOptionCodeResolver codes,
        IRemsApprovalPolicy policy,
        IMaconomyCustomerSyncService customerSync)
    {
        _policy = policy;
        _customerSync = customerSync;
        _requests = requests;
        _delegations = delegations;
        _tasks = tasks;
        _clients = clients;
        _settings = settings;
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
        var request = await _requests.GetWorkspaceAsync(id, cancellationToken);
        if (request is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }

        var route = await BuildRouteAsync(request, cancellationToken);
        var list = await ToApproverListAsync(request, route, cancellationToken);
        return Ok(ApiResponseFactory.Success(list, "REMS approvers retrieved."));
    }

    /// <summary>STATIC-APPROVAL-POLICY. The fixed rules as they resolve in the caller's tenant.</summary>
    [HttpGet("approval-policy")]
    [Authorize]
    [ProducesResponseType<ApiResponse<RemsApprovalPolicyView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ApprovalPolicy(CancellationToken cancellationToken)
    {
        var policy = await _policy.ForTenantAsync(User.GetActiveTenantId(), cancellationToken);
        var ids = policy.TaxExceptionCses.Concat(policy.Shareholders).Select(u => u.Id).ToList();
        var names = await _users.GetFullNamesAsync(ids, cancellationToken);

        var view = new RemsApprovalPolicyView(
            policy.StaticRouting,
            policy.Shareholders.Select(u => RemsWorkspaceMapper.UserRef(u.Id, names)!).ToList(),
            policy.TaxExceptionCses.Select(u => RemsWorkspaceMapper.UserRef(u.Id, names)!).ToList());
        return Ok(ApiResponseFactory.Success(view, "REMS approval policy retrieved."));
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
        if (await _requests.GetByIdAsync(id, cancellationToken) is null)
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
        var rems = await _requests.GetWorkspaceAsync(id, cancellationToken);
        if (rems is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }
        if (RemsDerived.EngagementStatusOf(rems) is not (RemsEngagementStatus.Draft or RemsEngagementStatus.Rejected))
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

            // STATIC-APPROVAL-POLICY: the seats already approve; picking one of them again is refused.
            var policy = await _policy.ForTenantAsync(tenantId, cancellationToken);
            if (policy.StaticRouting && requested.Intersect(RemsStaticApprovalRoute.ReservedUserIds(rems, policy)).Any())
            {
                return ConflictResult(CodeApproverReserved,
                    "The CSE, the Department Director and the Shareholders already approve and cannot be added again.");
            }
        }

        // Reconcile to exactly the requested set.
        var existing = await _requests.ListApproversAsync(id, cancellationToken);
        foreach (var row in existing.Where(r => !requested.Contains(r.UserId)))
        {
            _requests.RemoveApprover(row);
        }
        foreach (var userId in requested.Where(uid => existing.All(r => r.UserId != uid)))
        {
            await _requests.AddApproverAsync(new RemsApprover
            {
                Id = Guid.NewGuid(),
                TenantId = rems.TenantId,
                REMSId = id,
                UserId = userId,
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var route = await BuildRouteAsync(rems, cancellationToken);
        var list = await ToApproverListAsync(rems, route, cancellationToken);
        return Ok(ApiResponseFactory.Success(list, "REMS approvers updated."));
    }

    // -------------------- Send / resubmit --------------------

    /// <summary>Route the engagement for approval (AC-REMS-018/019).</summary>
    [HttpPost("engagements/{id:guid}/approval/send")]
    [EnableRateLimiting(RateLimitPolicies.Email)]
    [RequirePermission(Permissions.RemsApprovalsSend)]
    [ProducesResponseType<ApiResponse<RemsApproverList>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Send(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var request = await _requests.GetWorkspaceAsync(id, cancellationToken);
        if (request is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }
        if (RemsDerived.EngagementStatusOf(request) != RemsEngagementStatus.Draft)
        {
            return ConflictResult(CodeNotSendable, "Only a draft engagement can be sent for approval; a rejected one must be resubmitted.");
        }

        if (await GuardSetupOwnerAsync(request, cancellationToken) is { } notOwner)
        {
            return notOwner;
        }

        if (ValidateApprovalPrerequisites(request) is { } prereqError)
        {
            return prereqError;
        }

        var route = await BuildRouteAsync(request, cancellationToken);
        if (route.BlockedReason is { } blocked)
        {
            return ConflictResult(CodeRouteBlocked, blocked);
        }
        var approvers = route.Approvers;
        if (approvers.Count == 0)
        {
            return ConflictResult(CodeNoApprovers, "There are no approvers for this engagement; name a CSE, a department director or a commission recipient first, or add approvers on the Approval tab.");
        }

        await CreateRoundAsync(request, approvers, me, isResubmission: false, cancellationToken);

        var list = await ToApproverListAsync(request, route, cancellationToken);
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

        var request = await _requests.GetWorkspaceAsync(id, cancellationToken);
        if (request is null)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }
        if (RemsDerived.EngagementStatusOf(request) != RemsEngagementStatus.Rejected)
        {
            return ConflictResult(CodeNotRejected, "Only a rejected engagement can be resubmitted.");
        }

        if (await GuardSetupOwnerAsync(request, cancellationToken) is { } notOwner)
        {
            return notOwner;
        }

        if (ValidateApprovalPrerequisites(request) is { } prereqError)
        {
            return prereqError;
        }

        var route = await BuildRouteAsync(request, cancellationToken);
        if (route.BlockedReason is { } blocked)
        {
            return ConflictResult(CodeRouteBlocked, blocked);
        }
        var approvers = route.Approvers;
        if (approvers.Count == 0)
        {
            return ConflictResult(CodeNoApprovers, "There are no approvers for this engagement.");
        }

        await CreateRoundAsync(request, approvers, me, isResubmission: true, cancellationToken);

        var list = await ToApproverListAsync(request, route, cancellationToken);
        return Ok(ApiResponseFactory.Success(list, "REMS engagement resubmitted for approval."));
    }

    /// <summary>
    /// The refusal for routing an engagement whose request is not this caller's to work, or null to
    /// carry on.
    /// </summary>
    private async Task<IActionResult?> GuardSetupOwnerAsync(RemsRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        return RemsSetupAccess.CanWork(
            User, request, me, await RemsSetupAccess.CoverForWorkAsync(_delegations, request, me, cancellationToken))
            ? null
            : StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden(RemsSetupAccess.WorkDeniedReason(request)));
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

        if (await _requests.GetByIdAsync(id, cancellationToken) is not { } request)
        {
            return NotFound(ApiResponseFactory.NotFound("REMS engagement not found."));
        }

        // The permission first: it is free, and it is what the staff reading this hold. The approver check
        // is a query, so only a caller the permission did not cover pays for it.
        if (!User.HasPermission(Permissions.RemsRequestsRead)
            && !await _tasks.IsApproverOnRequestAsync(request.Id, me, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("Not permitted to view this engagement's approval history."));
        }

        var tasks = await _tasks.ListByRequestAsync(id, cancellationToken);
        var threshold = await DeclineThresholdAsync(cancellationToken);
        var rounds = Rounds(tasks, threshold);
        var names = await _users.GetFullNamesAsync(
            tasks.Select(t => t.ApproverId)
                .Concat(rounds.Where(r => r.SentByUserId.HasValue).Select(r => r.SentByUserId!.Value)),
            cancellationToken);

        string Name(Guid userId) => names.TryGetValue(userId, out var n) ? n : "Unknown user";

        // Newest round first — 3, 2, 1.
        var history = rounds
            .Select(r => new RemsApprovalRoundHistory(
                r.Id, r.RoundNumber, r.Status.ToString(), r.SentOnUtc,
                r.SentByUserId is { } sender ? Name(sender) : null,
                r.CompletedOnUtc,
                EffectiveThreshold(threshold, r.Tasks.Count),
                r.DeclineCount,
                r.Tasks
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
                    .ToList()))
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
    /// <para>
    /// A Tenant Admin or Super Admin reads the whole tenant instead: one row per REQUEST on the round it is
    /// on now, with every approver's standing on it, and <paramref name="approverUserId"/> narrows to the
    /// requests one approver is on. Reading is all it grants — a task is still decided only by the approver
    /// who holds it.
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
        // The round's standing as the badge says it, and the CSE named on the request.
        [FromQuery] string? roundStatus = null,
        [FromQuery] Guid? cseUserId = null,
        // The Client column's dropdown — any of the chosen clients.
        [FromQuery] Guid[]? clientPersonIds = null,
        // The Approver column's dropdown on the tenant-wide inbox.
        [FromQuery] Guid? approverUserId = null,
        [FromQuery] DateTime? sentFrom = null,
        [FromQuery] DateTime? sentTo = null,
        [FromQuery] DateTime? decidedFrom = null,
        [FromQuery] DateTime? decidedTo = null,
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

        // An unparseable role/status is treated as "no filter" rather than an error: these arrive from a
        // picker, and a stale value should show everything, not fail the page.
        RemsApproverRole? roleFilter = Enum.TryParse<RemsApproverRole>(role, ignoreCase: true, out var r) ? r : null;
        RemsApprovalTaskStatus? statusFilter =
            Enum.TryParse<RemsApprovalTaskStatus>(status, ignoreCase: true, out var s) ? s : null;

        var tenantScope = InboxTenantScope();
        var (tasks, total) = await _tasks.ListTasksByApproverAsync(
            new RemsApprovalTaskQuery(
                me, search, roleFilter, statusFilter, new SortRequest(sortBy, descending), page, limit,
                roundStatus, cseUserId, clientPersonIds, sentFrom, sentTo, decidedFrom, decidedTo,
                createdFrom, createdTo, updatedFrom, updatedTo,
                TenantId: tenantScope, ApproverUserId: approverUserId),
            cancellationToken);

        // The audit actors, each request's CSE and each row's approvers, resolved in one read: a name
        // lookup per row would be one round trip per task. A tenant-wide row names its whole round.
        var auditNames = await _users.GetFullNamesAsync(
            tasks.SelectMany(t => new[] { t.CreatedById, t.UpdatedById, t.Request?.CSEId, (Guid?)t.ApproverId }
                    .Concat(tenantScope is null
                        ? Array.Empty<Guid?>()
                        : t.Request!.ApprovalTasks.Where(o => o.RoundNumber == t.RoundNumber).Select(o => (Guid?)o.ApproverId)))
                .Where(id => id.HasValue).Select(id => id!.Value),
            cancellationToken);
        string? NameOf(Guid? id) => id is { } uid && auditNames.TryGetValue(uid, out var n) ? n : null;

        var threshold = await DeclineThresholdAsync(cancellationToken);

        // Sync now lives on this list. A row that is the caller's own task entitles them by itself; on the
        // tenant-wide inbox another approver's row needs the permission the Sync now endpoint asks for.
        // The view is null for a request whose client is not posted at all.
        var syncEnabled = User.GetActiveTenantId() is { } tenantId
            && await _customerSync.IsEnabledAsync(tenantId, cancellationToken);
        var maySyncAny = User.HasPermission(Permissions.RemsEngagementsManage);

        var rows = new List<RemsApprovalTaskRow>(tasks.Count);
        foreach (var t in tasks)
        {
            var request = t.Request!;
            var client = request.Client;
            var round = RoundOf(request, t, threshold);
            var maconomySync = RemsMaconomySyncViews.For(
                request, client is not null && _customerSync.IsStale(client), syncEnabled,
                canManage: t.ApproverId == me || maySyncAny);
            // A tenant-wide row is the request: Decided is when its round closed, and every approver is on it.
            var approvers = tenantScope is null ? null : round.Tasks
                .OrderBy(x => DisplayRank(x.ApproverRole))
                .ThenBy(x => x.CreatedOnUtc)
                .Select(x => new RemsApprovalDecisionView(
                    x.Id, RemsWorkspaceMapper.UserRef(x.ApproverId, auditNames)!, x.ApproverRole.ToString(),
                    x.Status.ToString(), x.DecidedOnUtc, x.RejectionReason, IsYou: x.ApproverId == me))
                .ToList();
            rows.Add(new RemsApprovalTaskRow(
                t.Id, round.Id, round.RoundNumber, t.ApproverRole.ToString(), t.Status.ToString(),
                round.SentOnUtc, tenantScope is null ? t.DecidedOnUtc : round.CompletedOnUtc, round.Status.ToString(),
                request.Id, request.Id, request.RemsNumber,
                client?.ClientName ?? string.Empty,
                client?.Person?.Suffix,
                RemsWorkspaceMapper.UserRef(request.CSEId, auditNames),
                round.Tasks.Count(x => x.Status == RemsApprovalTaskStatus.Approved),
                round.Tasks.Count(x => x.Status == RemsApprovalTaskStatus.Rejected),
                round.Tasks.Count,
                NameOf(t.CreatedById), t.CreatedOnUtc, NameOf(t.UpdatedById), t.UpdatedOnUtc,
                MaconomySync: maconomySync,
                Approver: RemsWorkspaceMapper.UserRef(t.ApproverId, auditNames),
                Approvers: approvers));
        }
        return Ok(ApiResponseFactory.Paginated(rows, "REMS approval tasks retrieved.", page, limit, total));
    }

    /// <summary>
    /// The tenant whose every approval task the caller may read: a Tenant Admin or Super Admin reads the
    /// whole of their active tenant. Anyone else reads their own tasks only, and gets null.
    /// </summary>
    private Guid? InboxTenantScope() => RemsSetupAccess.IsElevated(User) ? User.GetActiveTenantId() : null;

    /// <summary>
    /// The CSEs across the caller's inbox, for its CSE filter. Read off the inbox rather than the tenant's
    /// CSE directory: an approver may hold no REMS permission, so the directory is not theirs to read — and
    /// a filter offering people none of their requests name would be noise anyway.
    /// </summary>
    [HttpGet("approval-tasks/cses")]
    [Authorize]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsUserRef>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyTaskCses(CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var ids = await _tasks.ListInboxCseIdsAsync(me, InboxTenantScope(), cancellationToken);
        return Ok(ApiResponseFactory.Success(await UserRefsAsync(ids, cancellationToken), "Approval inbox CSEs retrieved."));
    }

    /// <summary>The clients across the caller's inbox, for its Client filter — read off the inbox, like the CSEs.</summary>
    [HttpGet("approval-tasks/clients")]
    [Authorize]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsClientChoice>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyTaskClients(CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var clients = await _tasks.ListInboxClientsAsync(me, InboxTenantScope(), cancellationToken);
        return Ok(ApiResponseFactory.Success(clients, "Approval inbox clients retrieved."));
    }

    /// <summary>
    /// The approvers across the caller's inbox, for its Approver filter: the caller alone, unless they
    /// read the whole tenant.
    /// </summary>
    [HttpGet("approval-tasks/approvers")]
    [Authorize]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsUserRef>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyTaskApprovers(CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var ids = await _tasks.ListInboxApproverIdsAsync(me, InboxTenantScope(), cancellationToken);
        return Ok(ApiResponseFactory.Success(await UserRefsAsync(ids, cancellationToken), "Approval inbox approvers retrieved."));
    }

    /// <summary>The users as a filter offers them: named, A to Z.</summary>
    private async Task<List<RemsUserRef>> UserRefsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var names = await _users.GetFullNamesAsync(ids, cancellationToken);
        return ids
            .Select(id => RemsWorkspaceMapper.UserRef(id, names)!)
            .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The quick-filter counts for the inbox, under the same search and filters.</summary>
    [HttpGet("approval-tasks/quick-counts")]
    [Authorize]
    [ProducesResponseType<ApiResponse<RemsApprovalTaskQuickCounts>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyTaskQuickCounts(
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] string? status = null,
        [FromQuery] string? roundStatus = null,
        [FromQuery] Guid? cseUserId = null,
        // The Client column's dropdown — any of the chosen clients.
        [FromQuery] Guid[]? clientPersonIds = null,
        [FromQuery] Guid? approverUserId = null,
        [FromQuery] DateTime? sentFrom = null,
        [FromQuery] DateTime? sentTo = null,
        [FromQuery] DateTime? decidedFrom = null,
        [FromQuery] DateTime? decidedTo = null,
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

        RemsApproverRole? roleFilter = Enum.TryParse<RemsApproverRole>(role, ignoreCase: true, out var r) ? r : null;
        RemsApprovalTaskStatus? statusFilter =
            Enum.TryParse<RemsApprovalTaskStatus>(status, ignoreCase: true, out var s) ? s : null;
        var counts = await _tasks.CountInboxQuickFiltersAsync(
            new RemsApprovalTaskQuery(
                me, search, roleFilter, statusFilter, SortRequest.Default, Page: 1, Limit: 1,
                roundStatus, cseUserId, clientPersonIds, sentFrom, sentTo, decidedFrom, decidedTo,
                createdFrom, createdTo, updatedFrom, updatedTo,
                TenantId: InboxTenantScope(), ApproverUserId: approverUserId),
            cancellationToken);
        return Ok(ApiResponseFactory.Success(counts, "Approval inbox quick-filter counts retrieved."));
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

        var taskId = await _tasks.GetCurrentTaskIdOnRequestAsync(remsId, me, cancellationToken);
        if (taskId is not { } id)
        {
            return NotFound(ApiResponseFactory.NotFound("You hold no approval task on this request."));
        }

        return Ok(ApiResponseFactory.Success(new RemsApprovalTaskRef(id), "Approval task resolved."));
    }

    /// <summary>
    /// The caller's own task with the full review packet (AC-REMS-019.9): the originating request, the
    /// client, the entity under review. A Tenant Admin or Super Admin may read any task of their tenant;
    /// the packet then says it is not theirs (<c>isYours</c>), and nothing on it can be decided by them.
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

        var task = await _tasks.GetTaskWithContextAsync(taskId, cancellationToken);
        // Record-level: an approver may read ONLY their own task; anything else is a 404 (never revealed).
        // The one exception reads the whole tenant, and reads it only.
        var readsTenant = InboxTenantScope() is { } tenant && task?.TenantId == tenant;
        if (task is null || (task.ApproverId != me && !readsTenant))
        {
            return NotFound(ApiResponseFactory.NotFound("Approval task not found."));
        }

        var view = await BuildTaskViewAsync(task, me, cancellationToken);
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

        var task = await _tasks.GetTaskByIdAsync(taskId, cancellationToken);
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
        _tasks.UpdateChecklistItem(item);
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

        var task = await _tasks.GetTaskWithContextAsync(taskId, cancellationToken);
        if (task is null || task.ApproverId != me)
        {
            return NotFound(ApiResponseFactory.NotFound("Approval task not found."));
        }

        var request = task.Request!;
        var threshold = await DeclineThresholdAsync(cancellationToken);
        if (task.Status != RemsApprovalTaskStatus.Pending)
        {
            return ConflictResult(CodeTaskDecided, "This task has already been decided.");
        }
        if (RoundOf(request, task, threshold).Status != RemsApprovalRoundStatus.Pending)
        {
            return ConflictResult(CodeRoundClosed, "This approval is already closed.");
        }

        // Re-verify server-side that every checklist item is completed (AC-REMS-019.7/8).
        if (task.ChecklistItems.Any(i => !i.Deleted && !i.IsCompleted))
        {
            return ConflictResult(CodeChecklistIncomplete, "All checklist items must be completed before approving.");
        }

        var now = DateTime.UtcNow;
        var client = request.Client;

        task.Status = RemsApprovalTaskStatus.Approved;
        task.DecidedOnUtc = now;
        _tasks.UpdateTask(task);
        await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, request.Id, ActivityEventTypes.RemsApproved, null, task.ApproverRole.ToString()), cancellationToken);

        // The round is settled once nobody is still deciding.
        var round = RoundOf(request, task, threshold);
        var fullyApproved = round.Status == RemsApprovalRoundStatus.Approved;
        Client? pendingSync = null;
        if (fullyApproved)
        {
            await ChangeStatusAsync(request, RemsRequestStatuses.Approved, me, now, cancellationToken);

            // A prospect until its first request is approved; from here the client is one the firm has.
            if (client is not null)
            {
                client.Status = ClientStatus.Active;
                _clients.Update(client);
            }

            // A brand-new client is posted to Maconomy from here — recorded with the approval, queued once
            // it is committed, and never anything the approval waits on or fails because of.
            pendingSync = await _customerSync.RecordOnApprovalAsync(request, cancellationToken);

            var involved = new HashSet<Guid>(round.Tasks.Select(t => t.ApproverId));
            if (round.SentByUserId is { } sender)
            {
                involved.Add(sender);
            }
            if (request.CSEId is { } cse)
            {
                involved.Add(cse);
            }
            // Final approval is the outcome the requester has been waiting for since they submitted.
            // (Rejections stay internal — they are a rework loop between staff, not a status for the requester.)
            if (request.CreatedById is { } requester)
            {
                involved.Add(requester);
            }
            foreach (var userId in involved)
            {
                await _notifications.DispatchAsync(new CreateNotificationDto(
                    userId, NotificationType.RemsEngagementApproved,
                    "A REMS engagement was fully approved",
                    $"{request.RemsNumber} — {ClientNameOf(request)}", EntityType.Rems, request.Id), cancellationToken);
            }
            await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, request.Id, ActivityEventTypes.RemsFullyApproved), cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (pendingSync is not null)
        {
            _customerSync.Enqueue(pendingSync);
        }

        var view = await BuildTaskViewAsync(await ReloadAsync(task, cancellationToken), me, cancellationToken);
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

        var task = await _tasks.GetTaskWithContextAsync(taskId, cancellationToken);
        if (task is null || task.ApproverId != me)
        {
            return NotFound(ApiResponseFactory.NotFound("Approval task not found."));
        }

        var rems = task.Request!;
        var threshold = await DeclineThresholdAsync(cancellationToken);
        if (task.Status != RemsApprovalTaskStatus.Pending)
        {
            return ConflictResult(CodeTaskDecided, "This task has already been decided.");
        }
        if (RoundOf(rems, task, threshold).Status != RemsApprovalRoundStatus.Pending)
        {
            return ConflictResult(CodeRoundClosed, "This approval is already closed.");
        }

        var now = DateTime.UtcNow;
        var reason = request.Reason.Trim();

        task.Status = RemsApprovalTaskStatus.Rejected;
        task.DecidedOnUtc = now;
        task.RejectionReason = reason;
        _tasks.UpdateTask(task);
        await _activity.WriteAsync(new CreateActivityEventDto(EntityType.Rems, rems.Id, ActivityEventTypes.RemsRejected, null, reason), cancellationToken);

        // Read off the round's own tasks with this decision on it: it is not committed yet, but the summary
        // sees the instance in hand.
        var round = RoundOf(rems, task, threshold);
        var effective = EffectiveThreshold(threshold, round.Tasks.Count);

        // Inert while the threshold is one decline, and deliberately kept: what decides is the threshold,
        // not this branch. Raise the tenant's decline threshold and a round has to stay open again.
        if (round.Status != RemsApprovalRoundStatus.Rejected)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            var openView = await BuildTaskViewAsync(await ReloadAsync(task, cancellationToken), me, cancellationToken);
            return Ok(ApiResponseFactory.Success(
                openView,
                $"Declined. {round.DeclineCount} of {effective} declines needed to send this back — the approval is still open."));
        }

        // Everyone who had not decided by the time the round closed. Their task is over, but they did not
        // decline it — Superseded is the difference.
        foreach (var pending in round.Tasks.Where(t => t.Id != task.Id && t.Status == RemsApprovalTaskStatus.Pending))
        {
            pending.Status = RemsApprovalTaskStatus.Superseded;
            pending.DecidedOnUtc = now;
            _tasks.UpdateTask(pending);
        }

        await ChangeStatusAsync(rems, RemsRequestStatuses.ChangesRequested, me, now, cancellationToken);

        // Every decline's own reason, so the notice explains the round rather than only its last vote.
        var reasons = round.Tasks
            .Where(t => t.Status == RemsApprovalTaskStatus.Rejected)
            .Select(t => t.RejectionReason)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToList();

        // The initiator owns the rework now, so they are told first; the sender and CSE still need to know
        // the round is over.
        var recipients = new HashSet<Guid>();
        if (round.SentByUserId is { } sender)
        {
            recipients.Add(sender);
        }
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
                $"{rems.RemsNumber} — {ClientNameOf(rems)}: {string.Join(" | ", reasons)}", EntityType.Rems, rems.Id), cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var view = await BuildTaskViewAsync(await ReloadAsync(task, cancellationToken), me, cancellationToken);
        return Ok(ApiResponseFactory.Success(view, "Declined. The request is back with its initiator."));
    }

    // -------------------- Status transitions and rounds --------------------

    /// <summary>Moves the request to a status code and records the transition; nothing happens when it is there already.</summary>
    private async Task ChangeStatusAsync(RemsRequest request, string code, Guid actorId, DateTime now, CancellationToken cancellationToken)
    {
        // The status column is a foreign key, so the transition names a CODE and this resolves it to the
        // item the tenant's own REMS.Status list holds for it.
        var toId = await _codes.RequireRemsIdAsync(RemsOptionSetKeys.Status, code, cancellationToken);
        if (toId == request.StatusId)
        {
            return;
        }

        await _requests.AddStatusHistoryAsync(new RemsStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            REMSId = request.Id,
            FromStatusId = request.StatusId,
            ToStatusId = toId,
            ChangedByUserId = actorId,
            ChangedOnUtc = now,
        }, cancellationToken);
        request.StatusId = toId;
        _requests.Update(request);
    }

    /// <summary>
    /// The task read back after a commit. A status change swaps the request's status item, and the packet
    /// reads the item, not the key, so it is loaded afresh rather than off the graph in hand.
    /// </summary>
    private async Task<RemsApprovalTask> ReloadAsync(RemsApprovalTask task, CancellationToken cancellationToken)
        => await _tasks.GetTaskWithContextAsync(task.Id, cancellationToken) ?? task;

    /// <summary>The tenant's decline threshold; one when nothing is set.</summary>
    private async Task<int> DeclineThresholdAsync(CancellationToken cancellationToken)
        => Math.Max(1, (await _settings.GetAsync(cancellationToken))?.DeclineThreshold ?? 1);

    /// <summary>
    /// Never more declines than there are approvers to give them: a raised threshold must not strand a
    /// small round with no pending task left to close it.
    /// </summary>
    private static int EffectiveThreshold(int declineThreshold, int approverCount)
        => approverCount < 1 ? 1 : Math.Min(declineThreshold, approverCount);

    /// <summary>Every round of a request, latest first, each judged against the threshold its own size allows.</summary>
    private static IReadOnlyList<RemsApprovalRoundSummary> Rounds(IEnumerable<RemsApprovalTask> tasks, int declineThreshold)
        => tasks
            .Where(t => !t.Deleted)
            .GroupBy(t => t.RoundNumber)
            .OrderByDescending(g => g.Key)
            .Select(g => RemsApprovalRounds.Summarize(g, EffectiveThreshold(declineThreshold, g.Count())).Single())
            .ToList();

    /// <summary>
    /// The round a task sits on, read off its siblings on the request. The task in hand is counted by
    /// instance, so a decision set on it but not yet committed is what the summary sees.
    /// </summary>
    private static RemsApprovalRoundSummary RoundOf(RemsRequest request, RemsApprovalTask task, int declineThreshold)
    {
        var round = request.ApprovalTasks
            .Where(t => !t.Deleted && t.RoundNumber == task.RoundNumber && t.Id != task.Id)
            .Append(task)
            .ToList();
        return RemsApprovalRounds.Summarize(round, EffectiveThreshold(declineThreshold, round.Count)).Single();
    }

    /// <summary>The client's name for a notice; the Maconomy card's where the client is one of theirs.</summary>
    private static string ClientNameOf(RemsRequest request) => request.Client?.ClientName ?? string.Empty;

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
    /// The approver set to route to. STATIC-APPROVAL-POLICY: THF's fixed rules while the policy is on;
    /// otherwise the platform's own — the automatic approvers (AC-REMS-018) plus whoever was added on the
    /// Approval tab. Either way everyone is asked at the same time.
    /// </summary>
    private async Task<RemsApprovalRoute> BuildRouteAsync(RemsRequest request, CancellationToken cancellationToken)
    {
        var picked = (await _requests.ListApproversAsync(request.Id, cancellationToken)).Select(a => a.UserId).ToList();

        var policy = await _policy.ForTenantAsync(User.GetActiveTenantId(), cancellationToken);
        if (policy.StaticRouting)
        {
            var fixedRoute = RemsStaticApprovalRoute.Build(request, picked, policy);
            return fixedRoute with { Approvers = fixedRoute.Approvers.OrderBy(a => DisplayRank(a.Role)).ToList() };
        }

        var shareholders = await ShareholderIdsAsync(cancellationToken);

        // Added approvers come ON TOP of the automatic ones — picking somebody never removes an approver
        // who holds their place by standing on the engagement.
        var approvers = AutomaticApproverIds(request, shareholders).Concat(picked)
            .Distinct()
            .Select(userId => (UserId: userId, Role: RoleFor(request, shareholders, userId)))
            .OrderBy(a => DisplayRank(a.Role))
            .ToList();

        return new RemsApprovalRoute(approvers, null);
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

    /// <summary>
    /// The approvers every engagement routes to whatever is picked on the Approval tab: the firm's
    /// shareholders, the Department Director and CSE from the request's own setup.
    /// </summary>
    private static List<Guid> AutomaticApproverIds(RemsRequest request, IReadOnlyList<Guid> shareholderIds)
    {
        var ids = new List<Guid>(shareholderIds);
        if (request.DepartmentDirectorId is { } director)
        {
            ids.Add(director);
        }
        if (request.CSEId is { } cse)
        {
            ids.Add(cse);
        }
        ids.AddRange(request.CommissionSplits.Where(s => !s.Deleted).Select(s => s.EmployeeId));
        return ids;
    }

    /// <summary>The role a user acts under on this engagement, most specific first.</summary>
    private static RemsApproverRole RoleFor(
        RemsRequest request, IReadOnlyList<Guid> shareholderIds, Guid userId)
    {
        if (request.CSEId == userId)
        {
            return RemsApproverRole.CSE;
        }
        if (request.DepartmentDirectorId == userId)
        {
            return RemsApproverRole.DepartmentDirector;
        }
        if (request.CommissionSplits.Any(s => !s.Deleted && s.EmployeeId == userId))
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
        RemsRequest request, RemsApprovalRoute route, CancellationToken cancellationToken)
    {
        var approvers = route.Approvers;
        var names = await _users.GetFullNamesAsync(approvers.Select(a => a.UserId), cancellationToken);
        var suggestions = approvers
            .Select(a => new RemsApproverSuggestion(
                new RemsUserRef(a.UserId, names.TryGetValue(a.UserId, out var n) ? n : string.Empty), a.Role.ToString()))
            .ToList();

        // Only the approvers somebody ADDED. The automatic ones — shareholders, director, CSE, commission
        // recipients — are left out: the picker must not show back somebody who is on the list anyway.
        var selected = (await _requests.ListApproversAsync(request.Id, cancellationToken))
            .Select(a => a.UserId)
            .ToList();

        // STATIC-APPROVAL-POLICY
        var policy = await _policy.ForTenantAsync(User.GetActiveTenantId(), cancellationToken);
        var reserved = policy.StaticRouting ? RemsStaticApprovalRoute.ReservedUserIds(request, policy) : Array.Empty<Guid>();

        return new RemsApproverList(
            request.Id, RemsDerived.EngagementStatusOf(request).ToString(), suggestions, selected,
            policy.StaticRouting, reserved, route.BlockedReason);
    }

    /// <summary>
    /// Validates the pre-approval requirements: the core setup and whatever the chosen department is
    /// asked; a marketing tag; a commission split that adds up to 100% where there is one at all.
    /// </summary>
    private IActionResult? ValidateApprovalPrerequisites(RemsRequest request)
    {
        // The engagement's core placement + team + realization are mandatory. The workspace marks these
        // on its Setup step too; this is the backstop for anything reaching the API another way.
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Department?.Value)) missing.Add("Department");
        // The service the firm is actually engaged to do.
        if (string.IsNullOrWhiteSpace(request.ServiceLine?.Value)) missing.Add("Service Line");
        if (request.EngagementExecutiveId is null) missing.Add("Engagement Executive");
        if (request.BillingManagerId is null) missing.Add("Billing Manager");
        if (request.RealizationPercentage is null) missing.Add("% Realization");

        // What each department is asked on top of that (EngagementSetupForm.vue marks the same boxes).
        // Tax and CAS price the first year; Assurance prices the engagement. CAS says how it bills.
        var department = request.Department?.Value;
        if (RemsEngagementCodes.IsTax(department) || RemsEngagementCodes.IsCas(department))
        {
            if (request.FirstYearFeeEstimate is null) missing.Add("First-Year Fee Estimate");
        }
        if (RemsEngagementCodes.IsCas(department))
        {
            if (request.BillingPeriodId is null) missing.Add("Billing Frequency");
            if (string.IsNullOrWhiteSpace(request.BillingProcessDescription)) missing.Add("Description of Billing Process");
        }
        if (RemsEngagementCodes.IsAssurance(department))
        {
            if (request.EngagementFee is null) missing.Add("Engagement Fee");
        }

        // Audit AND Assurance keep the client-acceptance form as a file of its own kind; Assurance adds the
        // client's year end, which lives on the client now.
        var acceptance = RemsEngagementCodes.RequiresClientAcceptanceForm(department)
            ? RemsWorkspaceMapper.FileOfKind(request, RemsFileKind.ClientAcceptanceForm)
            : null;
        if (RemsEngagementCodes.IsAssurance(department))
        {
            if (request.Client?.FiscalYearEnd is null) missing.Add("Fiscal Year End of Client");
        }

        // Tax: the whole fiscal-year card — the year end, the two due dates that follow from it (each a row,
        // computed or typed over), and at least one form on the checklist.
        if (RemsEngagementCodes.IsTax(department))
        {
            if (request.Client?.FiscalYearEnd is null) missing.Add("Fiscal Year End");
            if (!request.TaxDueDates.Any(d => !d.Deleted && d.Kind == RemsTaxDueDateKind.Original)) missing.Add("Original Due Date");
            if (!request.TaxDueDates.Any(d => !d.Deleted && d.Kind == RemsTaxDueDateKind.FirstExtension)) missing.Add("First Extension Due Date");
            if (!request.TaxForms.Any(f => !f.Deleted)) missing.Add("at least one Tax Form");
        }

        if (missing.Count > 0)
        {
            return ConflictResult(CodeSetupIncomplete, $"Complete the engagement setup first — missing: {string.Join(", ", missing)}.");
        }

        if (!request.MarketingMethods.Any(m => !m.Deleted))
        {
            return ConflictResult(CodeMarketingRequired, "At least one marketing tag is required before sending for approval.");
        }

        // Commission is optional; a split somebody started has to be settled before it is signed off.
        var splits = request.CommissionSplits.Where(s => !s.Deleted).ToList();
        var allocated = Math.Round(splits.Sum(s => s.CommissionPercentage), 2, MidpointRounding.AwayFromZero);
        if (splits.Count > 0 && allocated != 100m)
        {
            return ConflictResult(
                CodeCommissionNotFullyAllocated,
                $"Commission totals {allocated:0.##}% — the recipients must add up to 100% before this " +
                "engagement can be sent for approval.");
        }

        // Audit AND Assurance: the client-acceptance form is the same compliance artifact under both, and a
        // card that shows it without anything enforcing it is a card people learn to scroll past.
        if (RemsEngagementCodes.RequiresClientAcceptanceForm(department) && acceptance is null)
        {
            return ConflictResult(CodeCafRequired,
                $"A signed client-acceptance form is required for {(RemsEngagementCodes.IsAssurance(department) ? "an assurance" : "an audit")} engagement.");
        }

        // The entity type is the client's. Same source the workspace and the approval packet show it from.
        var entityType = request.Client?.EntityType?.Value;
        if (RemsEngagementCodes.IsGovernmentAudit(department, entityType)
            && (string.IsNullOrWhiteSpace(request.ContractNumber) || request.IsFloridaOnePercentFeeApplicable is null))
        {
            return ConflictResult(CodeGovDetailRequired, "A government audit requires a contract number and the Florida 1% state-fee flag.");
        }

        return null;
    }

    // -------------------- Round + task + checklist creation --------------------

    /// <summary>
    /// Opens a new round: a pending task per approver (each with its role checklist) under the next round
    /// number, the request moved to Pending Approval, every approver notified once.
    /// </summary>
    private async Task CreateRoundAsync(
        RemsRequest request, IReadOnlyList<(Guid UserId, RemsApproverRole Role)> approvers,
        Guid actorId, bool isResubmission, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var settings = await _settings.GetAsync(cancellationToken);
        var templates = settings?.ChecklistTemplates.Where(t => !t.Deleted).ToList() ?? new List<RemsChecklistTemplate>();

        // A round still open when a new one goes out is over: whoever had not decided is superseded, not
        // left pending beside the round that replaced theirs.
        foreach (var pending in (await _tasks.ListByRequestAsync(request.Id, cancellationToken))
                     .Where(t => t.Status == RemsApprovalTaskStatus.Pending))
        {
            pending.Status = RemsApprovalTaskStatus.Superseded;
            pending.DecidedOnUtc = now;
            _tasks.UpdateTask(pending);
        }

        var roundNumber = await _tasks.GetNextRoundNumberAsync(request.Id, cancellationToken);
        foreach (var (userId, role) in approvers)
        {
            var task = new RemsApprovalTask
            {
                Id = Guid.NewGuid(),
                TenantId = request.TenantId,
                REMSId = request.Id,
                RoundNumber = roundNumber,
                ApproverId = userId,
                ApproverRole = role,
                Status = RemsApprovalTaskStatus.Pending,
            };
            await _tasks.AddTaskAsync(task, cancellationToken);

            var order = 1;
            foreach (var label in ChecklistFor(role, templates))
            {
                await _tasks.AddChecklistItemAsync(new RemsApprovalChecklistItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = request.TenantId,
                    TaskId = task.Id,
                    DisplayOrder = order++,
                    Label = label,
                    IsCompleted = false,
                }, cancellationToken);
            }
        }

        await ChangeStatusAsync(request, RemsRequestStatuses.PendingApproval, actorId, now, cancellationToken);

        // Notify every approver (once per user, even if they hold multiple role tasks).
        foreach (var userId in approvers.Select(a => a.UserId).Distinct())
        {
            await _notifications.DispatchAsync(new CreateNotificationDto(
                userId, NotificationType.RemsApprovalRequested,
                isResubmission ? "A REMS engagement was resubmitted for your approval" : "A REMS engagement needs your approval",
                $"{request.RemsNumber} — {ClientNameOf(request)}", EntityType.Rems, request.Id), cancellationToken);
        }

        var listText = string.Join(", ", approvers.Select(a => $"{a.UserId}:{a.Role}"));
        await _activity.WriteAsync(new CreateActivityEventDto(
            EntityType.Rems, request.Id,
            isResubmission ? ActivityEventTypes.RemsApprovalResubmitted : ActivityEventTypes.RemsApprovalSent,
            null, listText), cancellationToken);

        // One atomic commit: tasks + checklists + request status + history + notifications + activity.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The checklist an approver in a role works through: the tenant's own lines for the role, or the
    /// catalog's for every role while the tenant has written none at all.
    /// </summary>
    private static IReadOnlyList<string> ChecklistFor(RemsApproverRole role, IReadOnlyList<RemsChecklistTemplate> templates)
        => templates.Count == 0
            ? RemsApprovalChecklistCatalog.For(role)
            : templates
                .Where(t => t.ApproverRole == role)
                .OrderBy(t => t.DisplayOrder)
                .ThenBy(t => t.Label, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.Label)
                .ToList();

    // -------------------- Task review packet --------------------

    /// <summary>
    /// Builds the approver's review packet: the request that started it, the client, the entity under
    /// review with its addresses and contacts. The task-context load carries the whole graph.
    /// </summary>
    // `me` is the reader: the packet is the same for the approver and for an admin reading over their
    // shoulder, except that only the approver can decide it.
    private async Task<RemsApprovalTaskView> BuildTaskViewAsync(RemsApprovalTask task, Guid me, CancellationToken cancellationToken)
    {
        var request = task.Request!;
        var client = request.Client!;
        var threshold = await DeclineThresholdAsync(cancellationToken);
        var round = RoundOf(request, task, threshold);

        var names = await _users.GetFullNamesAsync(
            EngagementUserIds(request)
                .Concat(round.Tasks.Select(t => t.ApproverId))
                .Concat(new[] { round.SentByUserId, request.AdminAssignedToId, request.CSEId, request.CreatedById, task.CreatedById, task.UpdatedById }
                    .Where(id => id.HasValue).Select(id => id!.Value)),
            cancellationToken);

        var (emsFormState, clientSubmissionState) = RemsWorkspaceMapper.FormState(RemsWorkspaceMapper.FormStateOf(request));
        var requestView = new RemsApprovalRequestView(
            request.Id, request.RemsNumber,
            // TODO(rems-v2): the request no longer carries a description; the packet hands out null until the contract drops the field.
            null,
            client.ClientName, client.Person?.Suffix,
            request.Type!.Value, request.Status!.Value, client.ClientEmail, client.ClientPhone,
            client.EntityType?.Value, emsFormState, clientSubmissionState,
            RemsWorkspaceMapper.UserRef(request.AdminAssignedToId, names),
            RemsWorkspaceMapper.UserRef(request.CSEId, names),
            request.CreatedById is { } creator && names.TryGetValue(creator, out var creatorName) ? creatorName : null,
            request.CreatedOnUtc,
            request.Files.Where(f => !f.Deleted && f.Kind == RemsFileKind.Attachment)
                .Select(f => new RemsFileRef(
                    f.Id, f.MediaId, f.Media?.OriginalFileName, f.Media?.MimeType, f.Media?.FileSize, f.Media?.PublicUrl))
                .ToList());

        var engagementView = await BuildApprovalEngagementViewAsync(task, request, client, names, cancellationToken);

        // By ROLE — shareholder, director, CSE, commission recipient, then anyone added by hand — which is
        // the order the Approval tab lists and the order the history reads in.
        var decisions = round.Tasks
            .OrderBy(t => DisplayRank(t.ApproverRole))
            .ThenBy(t => t.CreatedOnUtc)
            .Select(t => new RemsApprovalDecisionView(
                t.Id, RemsWorkspaceMapper.UserRef(t.ApproverId, names)!, t.ApproverRole.ToString(),
                t.Status.ToString(), t.DecidedOnUtc, t.RejectionReason, IsYou: t.ApproverId == me))
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

        var isYours = task.ApproverId == me;
        var canDecide = isYours
            && task.Status == RemsApprovalTaskStatus.Pending && round.Status == RemsApprovalRoundStatus.Pending;

        return new RemsApprovalTaskView(
            task.Id, round.Id, round.RoundNumber, task.ApproverRole.ToString(), task.Status.ToString(),
            task.DecidedOnUtc, task.RejectionReason, canDecide, checklist, requestView, engagementView, roundView,
            RecordAudit.From(task, RecordAudit.Names(names)),
            Approver: RemsWorkspaceMapper.UserRef(task.ApproverId, names),
            IsYours: isYours);
    }

    /// <summary>
    /// The engagement as the approver reads it: the setup off the request, the client as the one entity,
    /// and the money withheld from a role that may not see the fee.
    /// </summary>
    private async Task<RemsApprovalEngagementView> BuildApprovalEngagementViewAsync(
        RemsApprovalTask task,
        RemsRequest request,
        Client client,
        IReadOnlyDictionary<Guid, string> names,
        CancellationToken cancellationToken)
    {
        var maySeeFinancials = MaySeeFinancials(task.ApproverRole);
        var setup = RemsWorkspaceMapper.Engagement(request, names);

        var marketing = await ResolveOptionRefsAsync(MarketingSetKey, setup.MarketingMethodIds, cancellationToken);

        // The signed CAF resolved to something the approver can actually open — "a media id is on file"
        // is not a document anyone can review.
        var acceptance = RemsWorkspaceMapper.FileOfKind(request, RemsFileKind.ClientAcceptanceForm);
        var auditView = setup.Audit is { } audit
            ? new RemsApprovalAuditDetailView(
                audit.Id, audit.ClientAcceptanceFormMediaId, audit.FileName, acceptance?.Media?.PublicUrl, audit.ClientFiscalYearEnd)
            : null;

        RemsApprovalTaxDetailView? taxView = null;
        if (setup.Tax is { } tax)
        {
            // The rows the engagement was actually sent with, not a fresh calculation: a typed-over date is
            // the same row flagged overridden, so the rows are the schedule.
            var original = request.TaxDueDates.FirstOrDefault(d => !d.Deleted && d.Kind == RemsTaxDueDateKind.Original);
            var extension = request.TaxDueDates.FirstOrDefault(d => !d.Deleted && d.Kind == RemsTaxDueDateKind.FirstExtension);
            var dueDates = tax.FiscalYearEnd is { } fiscalYearEnd
                ? RemsTaxDueDates.Effective(fiscalYearEnd, original?.DueOn, extension?.DueOn)
                : null;
            taxView = new RemsApprovalTaxDetailView(
                tax.Id, tax.FiscalYearEnd, dueDates,
                await ResolveOptionRefsAsync(TaxFormSetKey, tax.TaxFormIds, cancellationToken));
        }

        // The PO's value and the rates are money, so a role that may not see the fee reads which levels
        // are staffed, not at what.
        var governmentView = setup.Government is { } government
            ? government with
            {
                PurchaseOrderAmount = maySeeFinancials ? government.PurchaseOrderAmount : null,
                PersonnelRates = RemsWorkspaceMapper.PersonnelRates(request, withRates: maySeeFinancials),
            }
            : null;

        // The client is the one entity under review: the entity list has one row, and it is the client's.
        var entity = RemsWorkspaceMapper.Entities(client)[0];
        var clientCard = RemsWorkspaceMapper.ClientView(client);
        var clientView = new RemsApprovalClientView(
            clientCard.Id, clientCard.Name, clientCard.Email, clientCard.MobileNumber,
            clientCard.ReferralSource, clientCard.BillingContactName, clientCard.BillingEmail,
            new[] { new RemsApprovalEntitySummary(entity.Id, entity.Name, entity.Ein, entity.IsMainEntity) });
        var entityView = new RemsApprovalEntityView(
            entity.Id, entity.Name, entity.Ein, entity.IsMainEntity, entity.Addresses, entity.Contacts);

        return new RemsApprovalEngagementView(
            request.Id,
            setup.Status,
            setup.Department,
            setup.ServiceLine,
            setup.JobTemplate,
            setup.Industry,
            clientView,
            entityView,
            setup.DepartmentDirector,
            setup.EngagementExecutive,
            setup.BillingManager,
            maySeeFinancials ? setup.FirstYearFeeEstimate : null,
            maySeeFinancials ? setup.EngagementFee : null,
            maySeeFinancials ? setup.RealizationPercentage : null,
            FinancialsRestricted: !maySeeFinancials,
            // Not withheld from any role: how often a client is invoiced and how the billing runs are terms
            // of the arrangement, not the fee.
            setup.BillingPeriod,
            setup.BillingProcessDescription,
            auditView,
            governmentView,
            taxView,
            marketing,
            setup.CommissionSplits);
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

    private static IReadOnlyCollection<Guid> EngagementUserIds(RemsRequest request)
    {
        var ids = new HashSet<Guid>();
        if (request.DepartmentDirectorId is { } d) ids.Add(d);
        if (request.EngagementExecutiveId is { } x) ids.Add(x);
        if (request.BillingManagerId is { } b) ids.Add(b);
        foreach (var s in request.CommissionSplits.Where(s => !s.Deleted))
        {
            ids.Add(s.EmployeeId);
        }
        return ids;
    }

    private IActionResult ConflictResult(string code, string message)
        => StatusCode(StatusCodes.Status409Conflict, ApiResponseFactory.Error(code, message, message));
}
