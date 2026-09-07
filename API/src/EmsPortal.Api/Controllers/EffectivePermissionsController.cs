using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Read-only, profile-facing view of the authenticated user's effective permissions in the active
/// tenant, broken down by source: Role → Permission Group → permission key (WO-120, REQ-PG-014.4).
/// </summary>
[ApiController]
[Authorize]
[Produces("application/json")]
[Tags("Auth")]
public sealed class EffectivePermissionsController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IPermissionGroupRepository _groups;

    public EffectivePermissionsController(IUserRepository users, IPermissionGroupRepository groups)
    {
        _users = users;
        _groups = groups;
    }

    /// <summary>The caller's own effective permissions in the active tenant, grouped by Role → Group → key.</summary>
    [HttpGet("/api/auth/effective-permissions")]
    [ProducesResponseType<ApiResponse<EffectivePermissionsResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var user = await _users.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        // A Super Admin holds every permission platform-wide; present it as a single source (mirrors
        // the token calculation, which short-circuits to the full catalogue for a Super Admin).
        if (user.TenantRoles.Any(r => string.Equals(r.RoleEntity?.Name ?? r.Role.ToString(), Roles.SuperAdmin, StringComparison.Ordinal)))
        {
            var all = Permissions.ForSuperAdmin().Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var superSource = new RolePermissionSourceDto(null, Roles.SuperAdmin, all, Array.Empty<PermissionGroupSourceDto>());
            return Ok(ApiResponseFactory.Success(
                new EffectivePermissionsResponse(new[] { superSource }, all), "Effective permissions."));
        }

        var roleSources = new List<RolePermissionSourceDto>();
        var effective = new HashSet<string>(StringComparer.Ordinal);

        // Only the caller's active-tenant assignments; the union matches JwtTokenService: each role's
        // direct keys ∪ the keys of its active composed Permission Groups.
        if (User.GetActiveTenantId() is { } activeTenantId)
        {
            foreach (var assignment in user.TenantRoles.Where(r => r.TenantId == activeTenantId && r.RoleEntity is not null))
            {
                var role = assignment.RoleEntity!;
                var direct = role.Permissions.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
                foreach (var key in direct)
                {
                    effective.Add(key);
                }

                var groups = await _groups.GetByRoleAsync(role.Id, cancellationToken);
                var groupSources = new List<PermissionGroupSourceDto>();
                foreach (var group in groups.Where(g => g.IsActive))
                {
                    var keys = group.Permissions
                        .Select(p => p.PermissionKey)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(k => k, StringComparer.Ordinal)
                        .ToList();
                    foreach (var key in keys)
                    {
                        effective.Add(key);
                    }

                    groupSources.Add(new PermissionGroupSourceDto(group.Id, group.Name, keys));
                }

                roleSources.Add(new RolePermissionSourceDto(role.Id, role.Name, direct, groupSources));
            }
        }

        var response = new EffectivePermissionsResponse(
            roleSources,
            effective.OrderBy(k => k, StringComparer.Ordinal).ToList());
        return Ok(ApiResponseFactory.Success(response, "Effective permissions."));
    }
}

/// <summary>The caller's effective permissions in the active tenant plus their per-role/per-group sources.</summary>
public sealed record EffectivePermissionsResponse(
    IReadOnlyList<RolePermissionSourceDto> Roles,
    IReadOnlyList<string> EffectivePermissions);

/// <summary>One assigned role and the permission keys it contributes, directly and via Permission Groups.</summary>
public sealed record RolePermissionSourceDto(
    Guid? RoleId,
    string RoleName,
    IReadOnlyList<string> DirectPermissions,
    IReadOnlyList<PermissionGroupSourceDto> PermissionGroups);

/// <summary>One composed Permission Group and the permission keys it contributes.</summary>
public sealed record PermissionGroupSourceDto(
    Guid GroupId,
    string GroupName,
    IReadOnlyList<string> PermissionKeys);
