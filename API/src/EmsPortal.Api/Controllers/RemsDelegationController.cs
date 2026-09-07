using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>REMS delegation: a shareholder or CSE naming someone to work their requests, modelled on Concur.</summary>
[ApiController]
[Route("api/rems/delegations")]
[Produces("application/json")]
[Tags("REMS Delegation")]
[Authorize]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class RemsDelegationController : ControllerBase
{
    private readonly IRemsDelegationRepository _delegations;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public RemsDelegationController(
        IRemsDelegationRepository delegations,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _delegations = delegations;
        _users = users;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    /// <summary>The delegates I have named, in force or not.</summary>
    [HttpGet("mine")]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsDelegationView>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var rows = await _delegations.ListForPrincipalAsync(me, cancellationToken);
        return Ok(ApiResponseFactory.Success(rows.Select(ToView).ToList(), "REMS delegates retrieved."));
    }

    /// <summary>Who I may act for right now.</summary>
    [HttpGet("acting-for")]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsActingForView>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ActingFor(CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await _delegations.ListActiveForDelegateAsync(me, today, cancellationToken);
        var views = rows
            .Select(d => new RemsActingForView(
                d.PrincipalUserId, d.Principal?.DisplayName ?? "Unknown user", d.CanPrepare, d.CanSend))
            .ToList();
        return Ok(ApiResponseFactory.Success(views, "REMS delegations retrieved."));
    }

    /// <summary>Name a delegate, or change what an existing one may do.</summary>
    [HttpPut]
    [ProducesResponseType<ApiResponse<RemsDelegationView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Upsert([FromBody] SaveRemsDelegationRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var invalid = await ValidateAsync(me, request, cancellationToken);
        if (invalid is not null)
        {
            return invalid;
        }

        var saved = await UpsertAsync(me, request, cancellationToken);
        return Ok(ApiResponseFactory.Success(ToView(saved), "REMS delegate saved."));
    }

    /// <summary>Withdraw a delegation.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        // Not-mine is a 404 rather than a 403: whether somebody else's delegation exists is not the
        // caller's business either way.
        var row = await _delegations.GetByIdAsync(id, cancellationToken);
        if (row is null || row.PrincipalUserId != me)
        {
            return NotFound(ApiResponseFactory.NotFound("Delegation not found."));
        }

        _delegations.Remove(row);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success<object?>(null, "REMS delegate removed."));
    }

    // -------------------- Administration: somebody else's delegates --------------------

    /// <summary>The delegates a given user has named.</summary>
    [HttpGet("~/api/admin/users/{userId:guid}/rems-delegates")]
    [RequirePermission(Permissions.RemsDelegationsManage)]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsDelegationView>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListForUser(Guid userId, CancellationToken cancellationToken)
    {
        if (await AdminAccessErrorAsync(userId, cancellationToken) is { } error)
        {
            return error;
        }

        var rows = await _delegations.ListForPrincipalAsync(userId, cancellationToken);
        return Ok(ApiResponseFactory.Success(rows.Select(ToView).ToList(), "REMS delegates retrieved."));
    }

    /// <summary>
    /// Who the user's work could be delegated to: the active users of the caller's tenant, minus the
    /// user themselves.
    /// </summary>
    [HttpGet("~/api/admin/users/{userId:guid}/rems-delegates/candidates")]
    [RequirePermission(Permissions.RemsDelegationsManage)]
    [ProducesResponseType<ApiResponse<IEnumerable<RemsAdminOption>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DelegateCandidates(Guid userId, CancellationToken cancellationToken)
    {
        if (await AdminAccessErrorAsync(userId, cancellationToken) is { } error)
        {
            return error;
        }

        var candidates = (await _users.ListActiveByTenantAsync(User.GetActiveTenantId()!.Value, cancellationToken))
            .Where(u => u.Id != userId)
            .ToList();
        var names = await _users.GetFullNamesAsync(candidates.Select(u => u.Id), cancellationToken);
        var options = candidates
            .Select(u => new RemsAdminOption(u.Id, names.TryGetValue(u.Id, out var n) ? n : u.DisplayName, u.Email))
            .OrderBy(o => o.Name)
            .ToList();
        return Ok(ApiResponseFactory.Success(options, "Delegate candidates retrieved."));
    }

    /// <summary>Name a delegate for the user, or change what an existing one may do.</summary>
    [HttpPut("~/api/admin/users/{userId:guid}/rems-delegates")]
    [RequirePermission(Permissions.RemsDelegationsManage)]
    [ProducesResponseType<ApiResponse<RemsDelegationView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertForUser(
        Guid userId, [FromBody] SaveRemsDelegationRequest request, CancellationToken cancellationToken)
    {
        if (await AdminAccessErrorAsync(userId, cancellationToken) is { } error)
        {
            return error;
        }
        if (await ValidateAsync(userId, request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var saved = await UpsertAsync(userId, request, cancellationToken);
        return Ok(ApiResponseFactory.Success(ToView(saved), "REMS delegate saved."));
    }

    /// <summary>Withdraw one of the user's delegations.</summary>
    [HttpDelete("~/api/admin/users/{userId:guid}/rems-delegates/{id:guid}")]
    [RequirePermission(Permissions.RemsDelegationsManage)]
    public async Task<IActionResult> RemoveForUser(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        if (await AdminAccessErrorAsync(userId, cancellationToken) is { } error)
        {
            return error;
        }

        var row = await _delegations.GetByIdAsync(id, cancellationToken);
        if (row is null || row.PrincipalUserId != userId)
        {
            return NotFound(ApiResponseFactory.NotFound("Delegation not found."));
        }

        _delegations.Remove(row);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success<object?>(null, "REMS delegate removed."));
    }

    // -------------------- Shared --------------------

    /// <summary>The boundary on the administrative door: the caller works in a tenant, the user belongs.</summary>
    private async Task<IActionResult?> AdminAccessErrorAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller."));
        }

        var user = await _users.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.TenantRoles.Any(r => !r.Deleted && r.TenantId == tenantId))
        {
            return NotFound(ApiResponseFactory.NotFound("User not found."));
        }
        if (!User.IsSuperAdmin() && user.TenantRoles.Any(r => r.Role == UserRole.SuperAdmin))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("Not permitted to manage this user."));
        }

        return null;
    }

    /// <summary>What makes a delegation valid, whoever is arranging it.</summary>
    private async Task<IActionResult?> ValidateAsync(
        Guid principalUserId, SaveRemsDelegationRequest request, CancellationToken cancellationToken)
    {
        if (request.DelegateUserId == principalUserId)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", "A user cannot be their own delegate."));
        }
        if (await _users.GetByIdAsync(request.DelegateUserId, cancellationToken) is null)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", "Unknown delegateUserId."));
        }
        if (request.StartsOn is { } from && request.EndsOn is { } to && to < from)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", "The end date is before the start date."));
        }

        return null;
    }

    /// <summary>
    /// Upserts on the (principal, delegate) pair rather than adding a second grant: two live grants
    /// for one pair would leave "which rights apply?" unanswerable.
    /// </summary>
    private async Task<REMSDelegation> UpsertAsync(
        Guid principalUserId, SaveRemsDelegationRequest request, CancellationToken cancellationToken)
    {
        var existing = await _delegations.GetAsync(principalUserId, request.DelegateUserId, cancellationToken);
        var isNew = existing is null;

        existing ??= new REMSDelegation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            PrincipalUserId = principalUserId,
            DelegateUserId = request.DelegateUserId,
        };

        existing.CanPrepare = request.CanPrepare;
        existing.CanSend = request.CanSend;
        existing.StartsOn = request.StartsOn;
        existing.EndsOn = request.EndsOn;

        // Added OR Modified, never both.
        if (isNew)
        {
            await _delegations.AddAsync(existing, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var saved = await _delegations.ListForPrincipalAsync(principalUserId, cancellationToken);
        return saved.FirstOrDefault(d => d.Id == existing.Id) ?? existing;
    }

    private static RemsDelegationView ToView(REMSDelegation d)
        => new(d.Id, d.DelegateUserId, d.Delegate?.DisplayName ?? "Unknown user",
            d.CanPrepare, d.CanSend, d.StartsOn, d.EndsOn,
            d.IsActiveOn(DateOnly.FromDateTime(DateTime.UtcNow)));
}
