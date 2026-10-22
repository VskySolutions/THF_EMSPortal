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
/// Sync now: posting a request's brand-new client to Maconomy by hand, inline, so the caller sees the
/// outcome at once and it works without the Workers process. For the approvers on the request — it is
/// offered on their Approvals list — and for REMS Admins, Tenant Admins and Super Admins, in the caller's
/// active tenant.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/rems/requests/{id:guid}/maconomy-sync")]
[Produces("application/json")]
[Tags("REMS Maconomy Sync")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class RemsMaconomySyncController : ControllerBase
{
    private readonly IMaconomyCustomerSyncService _sync;
    private readonly IRemsApprovalRepository _approvals;

    public RemsMaconomySyncController(IMaconomyCustomerSyncService sync, IRemsApprovalRepository approvals)
    {
        _sync = sync;
        _approvals = approvals;
    }

    /// <summary>
    /// One attempt, now. Returns the row as it stands afterwards whatever happened: a failed post is a
    /// 200 with the reason on the row, not an error — the sync and the request are separate things.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ApiResponse<RemsMaconomySyncView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SyncNow(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller."));
        }
        // An approver on the request may press it from their Approvals list without holding any REMS
        // permission; everyone else needs the engagement permission.
        if (!User.HasPermission(Permissions.RemsEngagementsManage)
            && !await _approvals.IsApproverOnRequestAsync(id, me, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("You are not an approver on this request."));
        }

        try
        {
            var sync = await _sync.SyncNowAsync(id, tenantId, me, cancellationToken);
            var view = RemsMaconomySyncViews.From(sync, _sync.IsStale(sync), enabled: true, canManage: true);
            return Ok(ApiResponseFactory.Success(view, MessageFor(view)));
        }
        catch (MaconomyCustomerSyncException ex)
        {
            return Refusal(ex);
        }
    }

    /// <summary>How a refusal reads to the caller. Shared with the settings page's run endpoint.</summary>
    public static IActionResult RefusalFor(ControllerBase controller, MaconomyCustomerSyncException ex) => ex.Refusal switch
    {
        MaconomyCustomerSyncRefusal.NotFound => controller.NotFound(ApiResponseFactory.NotFound(ex.Message)),
        MaconomyCustomerSyncRefusal.NotConnected => controller.Conflict(ApiResponseFactory.Error(ApiErrorCodes.MaconomyNotConfigured, "Maconomy is not connected.", ex.Message)),
        MaconomyCustomerSyncRefusal.SyncOff => controller.Conflict(ApiResponseFactory.Error(ApiErrorCodes.MaconomySyncOff, "The customer sync is switched off.", ex.Message)),
        MaconomyCustomerSyncRefusal.Running => controller.Conflict(ApiResponseFactory.Error(ApiErrorCodes.MaconomySyncRunning, "An attempt is already under way.", ex.Message)),
        _ => controller.Conflict(ApiResponseFactory.Error(ApiErrorCodes.MaconomySyncNotEligible, "This request's client is not posted to Maconomy.", ex.Message)),
    };

    public static string MessageFor(RemsMaconomySyncView view) => view.Status switch
    {
        RemsMaconomySyncStatuses.Synced => $"Maconomy customer {view.CustomerNumber} created.",
        RemsMaconomySyncStatuses.Linked => $"Linked to the existing Maconomy customer {view.CustomerNumber}.",
        RemsMaconomySyncStatuses.Failed => $"The sync failed: {view.FailureReason}",
        _ => "Sync recorded.",
    };

    private IActionResult Refusal(MaconomyCustomerSyncException ex) => RefusalFor(this, ex);
}
