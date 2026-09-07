using EmsPortal.Api.Models;
using EmsPortal.Api.Models.Roles;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Auditing;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>RBAC role management.</summary>
[ApiController]
[Produces("application/json")]
[Tags("Roles")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class RolesController : ControllerBase
{
    private readonly IRoleRepository _roles;
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly IAuditTrailService _audit;
    private readonly IUnitOfWork _unitOfWork;

    public RolesController(
        IRoleRepository roles,
        ITenantRepository tenants,
        IUserRepository users,
        IAuditTrailService audit,
        IUnitOfWork unitOfWork)
    {
        _roles = roles;
        _tenants = tenants;
        _users = users;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Resolves the Created/Updated actor ids across a set of rows to display names, so the list's
    /// audit columns read as people rather than guids.
    /// </summary>
    private async Task<Func<Guid?, string?>> AuditNamesAsync(
        IEnumerable<Role> rows, CancellationToken cancellationToken)
    {
        var names = await _users.GetFullNamesAsync(
            rows.SelectMany(r => new[] { r.CreatedById, r.UpdatedById })
                .Where(id => id.HasValue).Select(id => id!.Value),
            cancellationToken);
        return id => id is { } uid && names.TryGetValue(uid, out var n) ? n : null;
    }

    /// <summary>Names the tenants owning the rows on this page, so the list can say where each role comes from.</summary>
    private async Task<Func<Guid?, string?>> TenantNamesAsync(
        IEnumerable<Role> rows, CancellationToken cancellationToken)
    {
        var owners = rows.Where(r => r.TenantId.HasValue).Select(r => r.TenantId!.Value).ToHashSet();
        if (owners.Count == 0)
        {
            return _ => null;
        }

        var names = (await _tenants.ListAsync(cancellationToken))
            .Where(t => owners.Contains(t.Id))
            .ToDictionary(t => t.Id, t => t.Name);
        return id => id is { } tid && names.TryGetValue(tid, out var n) ? n : null;
    }

    /// <summary>
    /// The permission catalogue the caller may actually build a role from: everything for a Super
    /// Admin, the tenant's ceiling for anyone else.
    /// </summary>
    [HttpGet("/api/admin/permissions")]
    [RequirePermission(Permissions.RolesRead)]
    public async Task<IActionResult> Catalog(CancellationToken cancellationToken)
    {
        if (User.IsSuperAdmin())
        {
            return Ok(ApiResponseFactory.Success(Permissions.All, "Permissions retrieved."));
        }

        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller."));
        }

        var ceiling = await RoleAccess.CeilingAsync(_roles, tenantId, cancellationToken);
        // Filtered through Permissions.All rather than returned as the set, so the catalogue order holds.
        return Ok(ApiResponseFactory.Success(Permissions.All.Where(ceiling.Contains).ToList(), "Permissions retrieved."));
    }

    [HttpGet("/api/admin/roles")]
    [RequirePermission(Permissions.RolesWrite)]
    public async Task<IActionResult> List(
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var (roles, scopeError) = await VisibleRolesAsync(cancellationToken);
        if (scopeError is not null)
        {
            return scopeError;
        }

        IEnumerable<Role> result = roles;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            result = result.Where(r =>
                r.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (r.Description != null && r.Description.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }
        var page = result.ToList();
        var nameOf = await AuditNamesAsync(page, cancellationToken);
        var tenantNameOf = await TenantNamesAsync(page, cancellationToken);
        // Ordered after projecting, because two of the columns the list offers — Scope and Permissions —
        // only exist on the summary.
        var summaries = ListSorts.Apply(page.Select(r => ToSummary(r, nameOf, tenantNameOf)), sortBy, descending);
        return Ok(ApiResponseFactory.Success(summaries, "Roles retrieved."));
    }

    /// <summary>What the Roles list may be ordered by.</summary>
    private static readonly SortMap<RoleSummary> ListSorts = new SortMap<RoleSummary>("updatedOnUtc")
        .Add("name", r => r.Name)
        .Add("description", r => r.Description)
        .Add("isSystem", r => r.IsSystem, r => r.Name)
        .Add("scope", r => r.TenantName ?? "Platform", r => r.Name)
        .Add("permissionCount", r => r.PermissionCount, r => r.Name)
        .Add("createdOnUtc", r => r.CreatedOnUtc)
        .Add("updatedOnUtc", r => r.UpdatedOnUtc);

    [HttpGet("/api/admin/roles/{id:guid}")]
    [RequirePermission(Permissions.RolesWrite)]
    [ProducesResponseType<ApiResponse<RoleResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var role = await _roles.GetByIdAsync(id, cancellationToken);
        // Another tenant's role is not "forbidden" to this caller — it is nothing they can know exists.
        if (role is null || !RoleAccess.CanSee(User, role))
        {
            return NotFound(ApiResponseFactory.NotFound("Role not found."));
        }
        return Ok(ApiResponseFactory.Success(await ToResponseAsync(role, cancellationToken), "Role retrieved."));
    }

    [HttpPost("/api/admin/roles")]
    [RequirePermission(Permissions.RolesWrite)]
    [ProducesResponseType<ApiResponse<RoleResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", "A role name is required."));
        }

        var invalid = InvalidPermissions(request.Permissions);
        if (invalid is not null)
        {
            return invalid;
        }

        // A Super Admin writes a platform role; everybody else writes one owned by their own tenant, and
        // may only put keys in it that their tenant already holds.
        Guid? owner = null;
        if (!User.IsSuperAdmin())
        {
            if (User.GetActiveTenantId() is not { } tenantId)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller."));
            }
            owner = tenantId;

            var ceiling = await CheckCeilingAsync(tenantId, request.Permissions, cancellationToken);
            if (ceiling is not null)
            {
                return ceiling;
            }
        }

        if (await _roles.NameExistsAsync(name, owner, cancellationToken: cancellationToken))
        {
            return Conflict(ApiResponseFactory.Error(ApiErrorCodes.DuplicateIdentifier, "Role name already in use.", name));
        }

        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = owner,
            Name = name,
            Description = request.Description,
            IsSystem = false,
            Permissions = Normalize(request.Permissions),
        };
        await _roles.AddAsync(role, cancellationToken);
        await _audit.AddAsync(nameof(Role), role.Id.ToString(), "Created",
            details: $"name={role.Name}; scope={ScopeOf(role)}", cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ApiResponseFactory.Success(await ToResponseAsync(role, cancellationToken), "Role created."));
    }

    [HttpPut("/api/admin/roles/{id:guid}")]
    [RequirePermission(Permissions.RolesWrite)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        var (role, accessError) = await LoadForWriteAsync(id, cancellationToken);
        if (role is null)
        {
            return accessError!;
        }

        if (request.Permissions is not null)
        {
            var invalid = InvalidPermissions(request.Permissions);
            if (invalid is not null)
            {
                return invalid;
            }
            // Only a tenant's own roles reach here for a non-Super-Admin, so the ceiling is that tenant's.
            if (!User.IsSuperAdmin() && role.TenantId is { } tenantId)
            {
                var ceiling = await CheckCeilingAsync(tenantId, request.Permissions, cancellationToken);
                if (ceiling is not null)
                {
                    return ceiling;
                }
            }
            role.Permissions = Normalize(request.Permissions);
        }

        // System role names are fixed; their permission sets may still be tuned.
        if (!role.IsSystem && !string.IsNullOrWhiteSpace(request.Name))
        {
            var name = request.Name.Trim();
            if (!string.Equals(name, role.Name, StringComparison.OrdinalIgnoreCase)
                && await _roles.NameExistsAsync(name, role.TenantId, role.Id, cancellationToken))
            {
                return Conflict(ApiResponseFactory.Error(ApiErrorCodes.DuplicateIdentifier, "Role name already in use.", name));
            }
            role.Name = name;
        }
        if (request.Description is not null)
        {
            role.Description = request.Description;
        }

        _roles.Update(role);
        await _audit.AddAsync(nameof(Role), role.Id.ToString(), "Updated", cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(await ToResponseAsync(role, cancellationToken), "Role updated."));
    }

    [HttpDelete("/api/admin/roles/{id:guid}")]
    [RequirePermission(Permissions.RolesWrite)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var (role, accessError) = await LoadForWriteAsync(id, cancellationToken);
        if (role is null)
        {
            return accessError!;
        }
        if (role.IsSystem)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("System roles cannot be deleted."));
        }

        _roles.Remove(role); // soft delete via interceptor
        await _audit.AddAsync(nameof(Role), role.Id.ToString(), "Deleted", cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { message = "Role deleted." }, "Role deleted."));
    }

    [HttpGet("/api/admin/roles/{id:guid}/tenants")]
    [RequirePermission(Permissions.RolesWrite)]
    public async Task<IActionResult> ListRoleTenants(Guid id, CancellationToken cancellationToken)
    {
        if (!User.IsSuperAdmin())
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden(PlatformOnly));
        }
        if (await _roles.GetByIdAsync(id, cancellationToken) is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Role not found."));
        }

        var tenantIds = await _roles.ListTenantIdsForRoleAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(tenantIds, "Role tenants retrieved."));
    }

    // ---- Tenant availability (platform roles only, Super Admin only) ----

    [HttpGet("/api/admin/tenants/{tenantId:guid}/roles")]
    [RequirePermission(Permissions.RolesRead)]
    public async Task<IActionResult> ListForTenant(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!User.IsSuperAdmin() && User.GetActiveTenantId() != tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("Not permitted for this tenant."));
        }

        // Everything this tenant's users can hold — the platform roles plus the ones the tenant made for
        // itself — and the authoritative source for the user role pickers.
        var rows = (await _roles.ListVisibleToTenantAsync(tenantId, cancellationToken))
            .Where(r => RoleAccess.CanSee(User, r))
            .OrderBy(r => r.Name)
            .ToList();

        var nameOf = await AuditNamesAsync(rows, cancellationToken);
        var tenantNameOf = await TenantNamesAsync(rows, cancellationToken);
        return Ok(ApiResponseFactory.Success(rows.Select(r => ToSummary(r, nameOf, tenantNameOf)), "Tenant roles retrieved."));
    }

    [HttpPost("/api/admin/tenants/{tenantId:guid}/roles")]
    [RequirePermission(Permissions.RolesWrite)]
    public async Task<IActionResult> AssignToTenant(Guid tenantId, [FromBody] AssignRoleToTenantRequest request, CancellationToken cancellationToken)
    {
        if (!User.IsSuperAdmin())
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden(PlatformOnly));
        }
        if (await _tenants.GetByIdAsync(tenantId, cancellationToken) is null)
        {
            return NotFound(ApiResponseFactory.Error(ApiErrorCodes.TenantNotFound, "Tenant not found.", tenantId.ToString()));
        }
        var role = await _roles.GetByIdAsync(request.RoleId, cancellationToken);
        if (role is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Role not found."));
        }
        // Availability is a platform-role idea. A role a tenant owns is already available there and
        // nowhere else, and lending it out would contradict the ownership that keeps it private.
        if (role.TenantId is not null)
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", "A role owned by a tenant is available only within that tenant."));
        }

        if (await _roles.GetTenantRoleAsync(tenantId, request.RoleId, cancellationToken) is null)
        {
            await _roles.AddTenantRoleAsync(new TenantRole { Id = Guid.NewGuid(), TenantId = tenantId, RoleId = request.RoleId }, cancellationToken);
            await _audit.AddAsync(nameof(TenantRole), tenantId.ToString(), "RoleAssigned", details: request.RoleId.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Ok(ApiResponseFactory.Success(new { tenantId, roleId = request.RoleId }, "Role assigned to tenant."));
    }

    [HttpDelete("/api/admin/tenants/{tenantId:guid}/roles/{roleId:guid}")]
    [RequirePermission(Permissions.RolesWrite)]
    public async Task<IActionResult> UnassignFromTenant(Guid tenantId, Guid roleId, CancellationToken cancellationToken)
    {
        if (!User.IsSuperAdmin())
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden(PlatformOnly));
        }

        var existing = await _roles.GetTenantRoleAsync(tenantId, roleId, cancellationToken);
        if (existing is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Tenant role assignment not found."));
        }

        _roles.RemoveTenantRole(existing);
        await _audit.AddAsync(nameof(TenantRole), tenantId.ToString(), "RoleUnassigned", details: roleId.ToString(), cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { message = "Role unassigned." }, "Role unassigned."));
    }

    // ---- helpers ----

    private const string PlatformOnly = "Only a Super Admin manages which tenants a role is available in.";

    /// <summary>
    /// The roles the caller may see at all: every role for a Super Admin, the platform roles plus
    /// their own tenant's for anyone else.
    /// </summary>
    private async Task<(IReadOnlyList<Role> Roles, IActionResult? Error)> VisibleRolesAsync(CancellationToken cancellationToken)
    {
        if (User.IsSuperAdmin())
        {
            return (await _roles.ListAsync(cancellationToken), null);
        }
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return (Array.Empty<Role>(), StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller.")));
        }

        var visible = (await _roles.ListVisibleToTenantAsync(tenantId, cancellationToken))
            .Where(r => RoleAccess.CanSee(User, r))
            .ToList();
        return (visible, null);
    }

    /// <summary>Loads a role for editing or deletion.</summary>
    private async Task<(Role? Role, IActionResult? Error)> LoadForWriteAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await _roles.GetByIdAsync(id, cancellationToken);
        if (role is null || !RoleAccess.CanSee(User, role))
        {
            return (null, NotFound(ApiResponseFactory.NotFound("Role not found.")));
        }
        if (!RoleAccess.CanManage(User, role))
        {
            return (null, StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden(
                "This role belongs to the platform; only a Super Admin can change it. Create a role of your own instead.")));
        }
        return (role, null);
    }

    private IActionResult? InvalidPermissions(IEnumerable<string> permissions)
    {
        var unknown = permissions.Where(p => !Permissions.All.Contains(p)).ToList();
        return unknown.Count == 0
            ? null
            : BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Unknown permission(s).", string.Join(", ", unknown)));
    }

    /// <summary>
    /// Holds a tenant admin to their tenant's permission ceiling (ADR-003): a role they write can only
    /// hand out authority the tenant already has.
    /// </summary>
    private async Task<IActionResult?> CheckCeilingAsync(Guid tenantId, IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        var ceiling = await RoleAccess.CeilingAsync(_roles, tenantId, cancellationToken);
        var disallowed = keys.Where(k => !ceiling.Contains(k)).Distinct(StringComparer.Ordinal).ToList();
        return disallowed.Count == 0
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Error(
                ApiErrorCodes.PermissionCeilingExceeded, "One or more permissions are outside your tenant's permission ceiling.", string.Join(", ", disallowed)));
    }

    private static List<string> Normalize(IEnumerable<string> permissions)
        => permissions.Where(Permissions.All.Contains).Distinct().ToList();

    private static string ScopeOf(Role role) => role.TenantId?.ToString() ?? "platform";

    /// <summary>The single-role view.</summary>
    private async Task<RoleResponse> ToResponseAsync(Role r, CancellationToken cancellationToken)
    {
        var tenantName = r.TenantId is { } owner
            ? (await _tenants.GetByIdAsync(owner, cancellationToken))?.Name
            : null;
        return new RoleResponse(
            r.Id, r.Name, r.Description, r.IsSystem, r.TenantId, tenantName, RoleAccess.CanManage(User, r),
            r.Permissions, await RecordAudit.ForAsync(_users, r, cancellationToken));
    }

    private RoleSummary ToSummary(Role r, Func<Guid?, string?> nameOf, Func<Guid?, string?> tenantNameOf) => new(
        r.Id, r.Name, r.Description, r.IsSystem, r.TenantId, tenantNameOf(r.TenantId), RoleAccess.CanManage(User, r),
        r.Permissions.Count, nameOf(r.CreatedById), r.CreatedOnUtc, nameOf(r.UpdatedById), r.UpdatedOnUtc);
}
