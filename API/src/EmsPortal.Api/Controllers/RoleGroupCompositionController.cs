using EmsPortal.Api.Models.PermissionGroups;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Auditing;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Role ↔ Permission Group composition — the bridge in the Permission Keys → Groups → Roles
/// → Users hierarchy.
/// </summary>
[ApiController]
[Route("/api/admin/roles/{roleId:guid}")]
[RequirePermission(Permissions.RolesWrite)]
[Produces("application/json")]
[Tags("Role Composition")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class RoleGroupCompositionController : ControllerBase
{
    private readonly IRoleRepository _roles;
    private readonly IPermissionGroupRepository _groups;
    private readonly IPermissionGroupEffectivePermissionService _effective;
    private readonly IAuditTrailService _audit;
    private readonly IUnitOfWork _unitOfWork;

    public RoleGroupCompositionController(
        IRoleRepository roles,
        IPermissionGroupRepository groups,
        IPermissionGroupEffectivePermissionService effective,
        IAuditTrailService audit,
        IUnitOfWork unitOfWork)
    {
        _roles = roles;
        _groups = groups;
        _effective = effective;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    [HttpGet("groups")]
    [ProducesResponseType<ApiResponse<RoleGroupsResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListGroups(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await _roles.GetByIdAsync(roleId, cancellationToken);
        if (role is null || !RoleAccess.CanSee(User, role))
        {
            return NotFound(ApiResponseFactory.NotFound("Role not found."));
        }

        var groups = await _groups.GetByRoleAsync(roleId, cancellationToken);
        var preview = await _effective.PreviewForRoleAsync(roleId, cancellationToken);
        var summaries = groups
            // Role composition, not an audited list: the counts are placeholders here and the audit *By
            // names are left unresolved rather than paying for a lookup this surface never shows.
            .Select(g => new PermissionGroupSummaryResponse(
                g.Id, g.Name, g.Description, g.Permissions.Count, 0, g.IsActive, g.TenantId, g.Tenant?.Name,
                g.CapacityLimit, 0, false, null, g.CreatedOnUtc, null, g.UpdatedOnUtc))
            .ToList();
        return Ok(ApiResponseFactory.Success(new RoleGroupsResponse(roleId, summaries, preview.Permissions), "Role groups retrieved."));
    }

    [HttpGet("permissions/preview")]
    [ProducesResponseType<ApiResponse<RolePermissionPreviewResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await _roles.GetByIdAsync(roleId, cancellationToken);
        if (role is null || !RoleAccess.CanSee(User, role))
        {
            return NotFound(ApiResponseFactory.NotFound("Role not found."));
        }

        var preview = await _effective.PreviewForRoleAsync(roleId, cancellationToken);
        var sources = preview.Sources
            .Select(s => new EffectivePermissionSourceResponse(s.GroupId, s.GroupName, s.IsActive, s.Keys))
            .ToList();
        return Ok(ApiResponseFactory.Success(new RolePermissionPreviewResponse(preview.Permissions, sources), "Preview computed."));
    }

    [HttpPost("groups")]
    [ProducesResponseType<ApiResponse<RoleGroupsResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignGroups(Guid roleId, [FromBody] AssignGroupsRequest body, CancellationToken cancellationToken)
    {
        var role = await _roles.GetByIdAsync(roleId, cancellationToken);
        if (role is null || !RoleAccess.CanSee(User, role))
        {
            return NotFound(ApiResponseFactory.NotFound("Role not found."));
        }
        // Composing a group in or out rewrites what the role grants, so it IS an edit of the role and
        // follows the role rule: a platform role is a Super Admin's to change, whoever owns the groups.
        if (!RoleAccess.CanManage(User, role))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden(
                "This role belongs to the platform; only a Super Admin can change it."));
        }

        // Load and validate every requested group: must exist, belong to one tenant, and be in the
        // caller's tenant unless they are a Super Admin.
        var loaded = new List<PermissionGroup>();
        foreach (var groupId in body.GroupIds.Distinct())
        {
            var group = await _groups.GetByIdUnscopedAsync(groupId, cancellationToken);
            if (group is null)
            {
                return NotFound(ApiResponseFactory.NotFound($"Permission group {groupId} not found."));
            }
            loaded.Add(group);
        }
        if (loaded.Select(g => g.TenantId).Distinct().Count() > 1)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "All groups must belong to the same tenant.", string.Empty));
        }
        if (!User.IsSuperAdmin() && loaded.Any(g => g.TenantId != User.GetActiveTenantId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("Groups must belong to your tenant."));
        }

        // Capacity (WO-119): composing this role into a group that already has users must not push the group's
        // usage past its limit (AC-PG-013.2).
        foreach (var group in loaded)
        {
            if (group.CapacityLimit is not { } limit)
            {
                continue;
            }
            if (await _groups.GetRoleLinkAsync(roleId, group.Id, cancellationToken) is not null)
            {
                continue; // already composed — no new members
            }
            var projected = await _groups.CountActiveMembersAsync(group.Id, group.TenantId, new[] { roleId }, cancellationToken);
            if (projected > limit)
            {
                await _audit.AddAsync(nameof(PermissionGroup), group.Id.ToString(), "CapacityLimitReached",
                    details: $"Composing role '{role.Name}' would raise usage to {projected}, above the limit of {limit}.", cancellationToken: cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return BadRequest(ApiResponseFactory.Error(
                    ApiErrorCodes.CapacityLimitReached,
                    $"Cannot compose group '{group.Name}' into this role: its capacity limit ({limit}) would be exceeded (projected usage {projected}).",
                    group.Name));
            }
        }

        foreach (var group in loaded)
        {
            if (await _groups.GetRoleLinkAsync(roleId, group.Id, cancellationToken) is null)
            {
                await _groups.AddRoleLinkAsync(new RolePermissionGroup { Id = Guid.NewGuid(), RoleId = roleId, PermissionGroupId = group.Id }, cancellationToken);
                await _audit.AddAsync(nameof(Role), roleId.ToString(), "PermissionGroupAssigned", details: group.Name, cancellationToken: cancellationToken);
                await _audit.AddAsync(nameof(PermissionGroup), group.Id.ToString(), "AssignedToRole", details: role.Name, cancellationToken: cancellationToken);
            }
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _effective.RecomputeForRoleAsync(roleId, cancellationToken);

        var preview = await _effective.PreviewForRoleAsync(roleId, cancellationToken);
        var groups = await _groups.GetByRoleAsync(roleId, cancellationToken);
        var summaries = groups
            // Role composition, not an audited list: the counts are placeholders here and the audit *By
            // names are left unresolved rather than paying for a lookup this surface never shows.
            .Select(g => new PermissionGroupSummaryResponse(
                g.Id, g.Name, g.Description, g.Permissions.Count, 0, g.IsActive, g.TenantId, g.Tenant?.Name,
                g.CapacityLimit, 0, false, null, g.CreatedOnUtc, null, g.UpdatedOnUtc))
            .ToList();
        return Ok(ApiResponseFactory.Success(new RoleGroupsResponse(roleId, summaries, preview.Permissions), "Groups assigned to role."));
    }

    [HttpDelete("groups/{groupId:guid}")]
    [ProducesResponseType<ApiResponse<RemoveGroupFromRoleResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveGroup(Guid roleId, Guid groupId, CancellationToken cancellationToken)
    {
        var role = await _roles.GetByIdAsync(roleId, cancellationToken);
        if (role is null || !RoleAccess.CanSee(User, role))
        {
            return NotFound(ApiResponseFactory.NotFound("Role not found."));
        }
        // Composing a group in or out rewrites what the role grants, so it IS an edit of the role and
        // follows the role rule: a platform role is a Super Admin's to change, whoever owns the groups.
        if (!RoleAccess.CanManage(User, role))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden(
                "This role belongs to the platform; only a Super Admin can change it."));
        }
        var link = await _groups.GetRoleLinkAsync(roleId, groupId, cancellationToken);
        if (link is null)
        {
            return NotFound(ApiResponseFactory.NotFound("The group is not assigned to this role."));
        }

        var removedGroup = await _groups.GetByIdUnscopedAsync(groupId, cancellationToken);
        var removedKeys = removedGroup is { IsActive: true }
            ? removedGroup.Permissions.Select(p => p.PermissionKey).Distinct(StringComparer.Ordinal).ToList()
            : new List<string>();

        _groups.RemoveRoleLink(link);
        await _audit.AddAsync(nameof(Role), roleId.ToString(), "PermissionGroupRemoved", details: removedGroup?.Name, cancellationToken: cancellationToken);
        await _audit.AddAsync(nameof(PermissionGroup), groupId.ToString(), "RemovedFromRole", details: role.Name, cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _effective.RecomputeForRoleAsync(roleId, cancellationToken);

        var preview = await _effective.PreviewForRoleAsync(roleId, cancellationToken);
        // Keys that were the removed group's and are no longer provided by any remaining group.
        var lost = removedKeys.Where(k => !preview.Permissions.Contains(k)).ToList();
        return Ok(ApiResponseFactory.Success(new RemoveGroupFromRoleResponse(preview.Permissions, lost), "Group removed from role."));
    }
}
