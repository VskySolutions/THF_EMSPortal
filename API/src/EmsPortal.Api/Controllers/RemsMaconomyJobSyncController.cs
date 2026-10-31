using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Sync job now: posting an approved request's engagement to Maconomy as a job, by hand, inline, so the
/// caller sees the outcome at once. For the approvers on the request — it is offered on their Approvals
/// list — and for anyone who manages engagements or the Maconomy connection, in the caller's active tenant.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/rems/requests/{id:guid}/maconomy-job-sync")]
[Produces("application/json")]
[Tags("REMS Maconomy Sync")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class RemsMaconomyJobSyncController : ControllerBase
{
    private readonly IMaconomyJobSyncService _sync;
    private readonly IMaconomyClientDirectory _directory;
    private readonly IRemsApprovalTaskRepository _approvals;
    private readonly IUserRepository _users;

    public RemsMaconomyJobSyncController(
        IMaconomyJobSyncService sync, IMaconomyClientDirectory directory, IRemsApprovalTaskRepository approvals, IUserRepository users)
    {
        _sync = sync;
        _directory = directory;
        _approvals = approvals;
        _users = users;
    }

    /// <summary>
    /// What a press would send — customer, template job, project manager, job name — for the confirmation
    /// shown before it, with the row's state and why it cannot be pressed when it cannot.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<RemsMaconomyJobSyncPreview>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview(Guid id, CancellationToken cancellationToken)
    {
        var (_, tenantId, error) = await CallerAsync(id, cancellationToken);
        if (error is not null)
        {
            return error;
        }
        var request = await _sync.GetAsync(id, cancellationToken);
        if (request is null || request.TenantId != tenantId)
        {
            return NotFound(ApiResponseFactory.NotFound("Request not found."));
        }
        await _directory.HydrateAsync(request.Client, cancellationToken);
        var context = await _sync.GetContextAsync(tenantId, cancellationToken);
        var preview = RemsMaconomyJobSyncViews.PreviewFor(
            request, context, _sync.IsStale(request), canManage: true, await SyncedByNameAsync(request.MaconomyJobSyncedById, cancellationToken));
        return Ok(ApiResponseFactory.Success(preview, "Maconomy job sync preview retrieved."));
    }

    /// <summary>
    /// One attempt, now. Returns the row as it stands afterwards whatever happened: a failed post is a
    /// 200 with the reason on the row, not an error — the sync and the request are separate things.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ApiResponse<RemsMaconomyJobSyncView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SyncNow(Guid id, CancellationToken cancellationToken)
    {
        var (me, tenantId, error) = await CallerAsync(id, cancellationToken);
        if (error is not null)
        {
            return error;
        }
        try
        {
            var request = await _sync.SyncNowAsync(id, tenantId, me, cancellationToken);
            var context = await _sync.GetContextAsync(tenantId, cancellationToken);
            var view = RemsMaconomyJobSyncViews.For(
                request, context, _sync.IsStale(request), canManage: true, await SyncedByNameAsync(request.MaconomyJobSyncedById, cancellationToken));
            return Ok(ApiResponseFactory.Success(view, RemsMaconomyJobSyncViews.MessageFor(view)));
        }
        catch (MaconomyJobSyncException ex)
        {
            return RefusalFor(this, ex);
        }
    }

    /// <summary>How a refusal reads to the caller. Shared with the settings page's run endpoint; the kinds are the customer sync's.</summary>
    public static IActionResult RefusalFor(ControllerBase controller, MaconomyJobSyncException ex) => ex.Refusal switch
    {
        MaconomyCustomerSyncRefusal.NotFound => controller.NotFound(ApiResponseFactory.NotFound(ex.Message)),
        MaconomyCustomerSyncRefusal.NotConnected => controller.Conflict(ApiResponseFactory.Error(ApiErrorCodes.MaconomyNotConfigured, "Maconomy is not connected.", ex.Message)),
        MaconomyCustomerSyncRefusal.SyncOff => controller.Conflict(ApiResponseFactory.Error(ApiErrorCodes.MaconomySyncOff, "The job sync is switched off.", ex.Message)),
        MaconomyCustomerSyncRefusal.Running => controller.Conflict(ApiResponseFactory.Error(ApiErrorCodes.MaconomySyncRunning, "An attempt is already under way.", ex.Message)),
        _ => controller.Conflict(ApiResponseFactory.Error(ApiErrorCodes.MaconomySyncNotEligible, "This request cannot be posted to Maconomy as a job.", ex.Message)),
    };

    private async Task<string?> SyncedByNameAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is not { } id)
        {
            return null;
        }
        var names = await _users.GetFullNamesAsync(new[] { id }, cancellationToken);
        return names.TryGetValue(id, out var name) ? name : null;
    }

    /// <summary>
    /// The caller and their tenant, or the response that turns them away. An approver on the request may
    /// press it from their Approvals list without holding any REMS permission; everyone else needs the
    /// engagement permission or the Maconomy one.
    /// </summary>
    private async Task<(Guid Me, Guid TenantId, IActionResult? Error)> CallerAsync(Guid remsId, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return (Guid.Empty, Guid.Empty, Unauthorized(ApiResponseFactory.Unauthorized("No user context.")));
        }
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return (me, Guid.Empty, StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller.")));
        }
        if (!User.HasPermission(Permissions.RemsEngagementsManage)
            && !User.HasPermission(Permissions.IntegrationsMaconomyManage)
            && !await _approvals.IsApproverOnRequestAsync(remsId, me, cancellationToken))
        {
            return (me, tenantId, StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("You are not an approver on this request.")));
        }
        return (me, tenantId, null);
    }
}
