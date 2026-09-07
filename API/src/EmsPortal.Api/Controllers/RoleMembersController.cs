using EmsPortal.Api.Models.Roles;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Auditing;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Who holds a role, from the role's side — the same (user, tenant, role) rows the user page
/// maintains one person at a time, reached instead by asking "who has this role, and who else should".
/// </summary>
[ApiController]
[Route("/api/admin/roles/{roleId:guid}/users")]
[RequirePermission(Permissions.RolesAssign)]
[Produces("application/json")]
[Tags("Role Members")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class RoleMembersController : ControllerBase
{
    private readonly IRoleRepository _roles;
    private readonly IUserRepository _users;
    private readonly IPermissionGroupRepository _permissionGroups;
    private readonly IAuditTrailService _audit;
    private readonly IUnitOfWork _unitOfWork;

    public RoleMembersController(
        IRoleRepository roles,
        IUserRepository users,
        IPermissionGroupRepository permissionGroups,
        IAuditTrailService audit,
        IUnitOfWork unitOfWork)
    {
        _roles = roles;
        _users = users;
        _permissionGroups = permissionGroups;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IEnumerable<RoleMemberResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid roleId, CancellationToken cancellationToken)
    {
        var (role, tenantId, error) = await ResolveAsync(roleId, cancellationToken);
        if (role is null)
        {
            return error!;
        }

        var holders = await _users.ListByTenantRoleAsync(tenantId, roleId, cancellationToken);
        var rows = holders.Select(u =>
        {
            var others = u.TenantRoles
                .Where(r => !r.Deleted && r.TenantId == tenantId && r.RoleId != roleId)
                .Select(r => r.RoleEntity?.Name ?? r.Role.ToString())
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new RoleMemberResponse(u.Id, NameOf(u), u.Email, u.IsActive, others, others.Count == 0);
        });

        return Ok(ApiResponseFactory.Success(rows, "Role members retrieved."));
    }

    /// <summary>Who this role could still be given to: the tenant's active users who do not hold it yet.</summary>
    [HttpGet("candidates")]
    [ProducesResponseType<ApiResponse<IEnumerable<RoleMemberCandidateResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Candidates(Guid roleId, CancellationToken cancellationToken)
    {
        var (role, tenantId, error) = await ResolveAsync(roleId, cancellationToken);
        if (role is null)
        {
            return error!;
        }

        var holders = (await _users.ListByTenantRoleAsync(tenantId, roleId, cancellationToken))
            .Select(u => u.Id).ToHashSet();
        var candidates = (await _users.ListActiveByTenantAsync(tenantId, cancellationToken))
            .Where(u => !holders.Contains(u.Id))
            .Select(u => new RoleMemberCandidateResponse(u.Id, NameOf(u), u.Email));

        return Ok(ApiResponseFactory.Success(candidates, "Candidates retrieved."));
    }

    /// <summary>Grants the role to each of the given users in the caller's active tenant.</summary>
    [HttpPost]
    public async Task<IActionResult> Add(Guid roleId, [FromBody] AddRoleMembersRequest request, CancellationToken cancellationToken)
    {
        var (role, tenantId, error) = await ResolveAsync(roleId, cancellationToken);
        if (role is null)
        {
            return error!;
        }

        var userIds = request.UserIds?.Distinct().ToList() ?? new List<Guid>();
        if (userIds.Count == 0)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", "Select at least one user."));
        }

        var granted = new List<string>();
        foreach (var userId in userIds)
        {
            var user = await _users.GetByIdAsync(userId, cancellationToken);
            if (user is null)
            {
                return NotFound(ApiResponseFactory.NotFound("User not found."));
            }
            // A Super Admin target is off limits to everyone else — the same guard the user page keeps,
            // so a tenant admin cannot rewrite the roles of the account that polices them.
            if (!User.IsSuperAdmin() && user.TenantRoles.Any(r => r.Role == UserRole.SuperAdmin))
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("Not permitted to manage this user."));
            }

            var here = await _users.GetAssignmentsAsync(userId, tenantId, cancellationToken);
            if (here.Count == 0)
            {
                return BadRequest(ApiResponseFactory.Error(
                    ApiErrorCodes.ValidationFailed, "Validation failed.",
                    $"{NameOf(user)} has no access to this tenant. Give them their first role on the Users page."));
            }
            if (here.Any(a => a.RoleId == roleId))
            {
                continue; // already holds it — nothing to do, and not an error
            }

            var block = await RoleAssignment.FindCapacityBlockAsync(
                _permissionGroups, _audit, userId, user.IsActive, tenantId, new[] { roleId }, cancellationToken);
            if (block is not null)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken); // the rejection is audited
                return BadRequest(ApiResponseFactory.Error(
                    ApiErrorCodes.CapacityLimitReached,
                    granted.Count == 0
                        ? block.Message
                        : $"{string.Join(", ", granted)} added. {NameOf(user)} could not be: {block.Message}",
                    block.GroupName));
            }

            await _users.AddAssignmentAsync(new UserTenantRole
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenantId,
                Role = RoleAssignment.MapLegacyRole(role, null),
                RoleId = roleId,
            }, cancellationToken);
            await _audit.AddAsync(nameof(User), userId.ToString(), "TenantRoleAssigned",
                details: $"tenant={tenantId}; roles={role.Name}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            granted.Add(NameOf(user));
        }

        return Ok(ApiResponseFactory.Success(
            new { roleId, tenantId, added = granted },
            granted.Count == 0 ? "Nothing to add — they already hold this role." : $"Role granted to {granted.Count} user(s)."));
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Remove(Guid roleId, Guid userId, CancellationToken cancellationToken)
    {
        var (role, tenantId, error) = await ResolveAsync(roleId, cancellationToken);
        if (role is null)
        {
            return error!;
        }

        var user = await _users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return NotFound(ApiResponseFactory.NotFound("User not found."));
        }
        if (!User.IsSuperAdmin() && user.TenantRoles.Any(r => r.Role == UserRole.SuperAdmin))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("Not permitted to manage this user."));
        }

        var here = await _users.GetAssignmentsAsync(userId, tenantId, cancellationToken);
        var assignment = here.FirstOrDefault(a => a.RoleId == roleId);
        if (assignment is null)
        {
            return NotFound(ApiResponseFactory.NotFound("This user does not hold that role in your tenant."));
        }

        // Their last role in a tenant IS their access to it (AC-ADM-006.3).
        if (here.Count == 1)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.",
                $"{NameOf(user)} holds no other role in this tenant, so removing this one would remove their access. Do that from the Users page."));
        }

        _users.RemoveAssignment(assignment);
        await _audit.AddAsync(nameof(User), userId.ToString(), "TenantRoleRemoved",
            details: $"tenant={tenantId}; role={role.Name}", cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(new { roleId, userId }, "Role removed from user."));
    }

    // ---- helpers ----

    /// <summary>Resolves the role whose membership is being managed, and the tenant it is managed in.</summary>
    private async Task<(Role? Role, Guid TenantId, IActionResult? Error)> ResolveAsync(Guid roleId, CancellationToken cancellationToken)
    {
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return (null, Guid.Empty, StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller.")));
        }

        var role = await _roles.GetByIdAsync(roleId, cancellationToken);
        if (role is null || !RoleAccess.CanSee(User, role))
        {
            return (null, tenantId, NotFound(ApiResponseFactory.NotFound("Role not found.")));
        }
        if (role.TenantId is { } owner && owner != tenantId)
        {
            return (null, tenantId, BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.",
                "That role belongs to another tenant, so nobody here can hold it.")));
        }
        if (role.IsSystem && string.Equals(role.Name, Roles.SuperAdmin, StringComparison.Ordinal))
        {
            return (null, tenantId, StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden(
                "Super Admin is granted with the account itself, on the user's own page.")));
        }

        return (role, tenantId, null);
    }

    /// <summary>The person's name as every other list shows it: the Person master record, else the login.</summary>
    private static string NameOf(User user) => user.Person?.FullName ?? user.DisplayName;
}
