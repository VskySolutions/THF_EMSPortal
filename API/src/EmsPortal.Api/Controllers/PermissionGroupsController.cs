using EmsPortal.Api.Models;
using EmsPortal.Api.Models.PermissionGroups;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Auditing;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Permission Group management (RBAC composition layer: Permission Keys → Permission Groups →
/// Roles → Users).
/// </summary>
[ApiController]
[Route("/api/admin/permission-groups")]
[RequirePermission(Permissions.GroupsManage)]
[Produces("application/json")]
[Tags("Permission Groups")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class PermissionGroupsController : ControllerBase
{
    private readonly IPermissionGroupRepository _groups;
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly ITenantRepository _tenants;
    private readonly IPermissionGroupEffectivePermissionService _effective;
    private readonly IAuditTrailService _audit;
    private readonly IAuditTrailRepository _auditRead;
    private readonly IUnitOfWork _unitOfWork;

    public PermissionGroupsController(
        IPermissionGroupRepository groups,
        IRoleRepository roles,
        ITenantRepository tenants,
        IUserRepository users,
        IPermissionGroupEffectivePermissionService effective,
        IAuditTrailService audit,
        IAuditTrailRepository auditRead,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _groups = groups;
        _roles = roles;
        _tenants = tenants;
        _effective = effective;
        _audit = audit;
        _auditRead = auditRead;
        _unitOfWork = unitOfWork;
    }

    // ---- Templates (declared before {id} routes so "templates" isn't captured as an id) ----

    [HttpGet("templates")]
    public async Task<IActionResult> Templates(CancellationToken cancellationToken)
    {
        var templates = await _groups.GetTemplatesAsync(cancellationToken);
        var data = templates.Select(t => new PermissionGroupTemplateResponse(t.Id, t.Name, t.Description, t.PermissionKeys, t.IsSeeded));
        return Ok(ApiResponseFactory.Success(data, "Templates retrieved."));
    }

    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] SaveTemplateRequest request, CancellationToken cancellationToken)
    {
        if (!User.IsSuperAdmin())
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("Only a Super Admin may manage templates."));
        }
        var keys = NormalizeKnown(request.PermissionKeys, out var unknown);
        if (unknown.Count > 0)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Unknown permission key(s).", string.Join(", ", unknown)));
        }

        var template = new PermissionGroupTemplate
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsSeeded = false,
            PermissionKeysJson = System.Text.Json.JsonSerializer.Serialize(keys),
        };
        await _groups.AddTemplateAsync(template, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(
            new PermissionGroupTemplateResponse(template.Id, template.Name, template.Description, keys, template.IsSeeded), "Template created."));
    }

    // ---- List ----

    [HttpGet]
    [ProducesResponseType<ApiResponse<IEnumerable<PermissionGroupSummaryResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] bool? usedByRoles,
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        Guid? scopeTenant = User.IsSuperAdmin() && tenantId is { } tid ? tid : null;

        var (items, total) = await _groups.ListAsync(
            scopeTenant, search, isActive, usedByRoles, category, new SortRequest(sortBy, descending),
            Math.Max(1, page), Math.Clamp(limit, 1, 100), cancellationToken);

        // Batch the per-group current usage (distinct active members) in one query to avoid N+1.
        var usageByGroup = await _groups.CountActiveMembersForGroupsAsync(items.Select(i => i.Id).ToList(), cancellationToken);

        // One name lookup for the page, so the audit columns read as people rather than guids.
        var names = await _users.GetFullNamesAsync(
            items.SelectMany(g => new[] { g.CreatedById, g.UpdatedById })
                .Where(id => id.HasValue).Select(id => id!.Value),
            cancellationToken);
        string? NameOf(Guid? id) => id is { } uid && names.TryGetValue(uid, out var n) ? n : null;

        var data = new List<PermissionGroupSummaryResponse>(items.Count);
        foreach (var g in items)
        {
            var rolesUsing = await _groups.CountRolesUsingGroupAsync(g.Id, cancellationToken);
            var usage = usageByGroup.TryGetValue(g.Id, out var u) ? u : 0;
            data.Add(ToSummary(g, rolesUsing, usage, NameOf));
        }

        return Ok(ApiResponseFactory.Paginated(data, "Permission groups retrieved.", page, limit, total));
    }

    // ---- Detail ----

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<PermissionGroupDetailResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var group = await LoadAsync(id, cancellationToken);
        if (group is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Permission group not found."));
        }

        var rolesUsing = await _groups.GetRolesUsingGroupAsync(id, cancellationToken);
        var usage = await _groups.CountActiveMembersAsync(group.Id, group.TenantId, null, cancellationToken);
        var audit = await _auditRead.ListByEntityAsync(nameof(PermissionGroup), id.ToString(), 100, cancellationToken);
        // The trail records WHO as a user id; it is shown to a person, so it is resolved to a name.
        var actorName = await AuditActorNames.ResolverAsync(_users, audit, cancellationToken);
        var provenance = await RecordAudit.ForAsync(_users, group, cancellationToken);
        return Ok(ApiResponseFactory.Success(ToDetail(group, rolesUsing, audit, usage, actorName, provenance), "Permission group retrieved."));
    }

    // ---- Create ----

    [HttpPost]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateGroupRequest body, CancellationToken cancellationToken)
    {
        var (tenantId, tenantError) = await ResolveTargetTenantAsync(body.TenantId, cancellationToken);
        if (tenantError is not null)
        {
            return tenantError;
        }

        var keys = NormalizeKnown(body.PermissionKeys, out var unknown);
        if (unknown.Count > 0)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Unknown permission key(s).", string.Join(", ", unknown)));
        }
        var ceiling = await CheckCeilingAsync(tenantId, keys, cancellationToken);
        if (ceiling is not null)
        {
            return ceiling;
        }
        if (await _groups.NameExistsAsync(tenantId, body.Name.Trim(), Guid.Empty, cancellationToken))
        {
            return Conflict(ApiResponseFactory.Error(ApiErrorCodes.DuplicateGroupName, "A group with this name already exists.", body.Name.Trim()));
        }

        var group = new PermissionGroup
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = body.Name.Trim(),
            Description = body.Description?.Trim(),
            IsActive = true,
            CapacityLimit = body.CapacityLimit,
        };
        await _groups.AddAsync(group, cancellationToken);
        foreach (var key in keys)
        {
            await _groups.AddPermissionAsync(new PermissionGroupPermission { Id = Guid.NewGuid(), PermissionGroupId = group.Id, PermissionKey = key }, cancellationToken);
        }
        await _audit.AddAsync(nameof(PermissionGroup), group.Id.ToString(), "Created",
            details: $"name={group.Name}; keys={keys.Count}; capacity={(group.CapacityLimit?.ToString() ?? "unlimited")}", cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ApiResponseFactory.Success(new { groupId = group.Id }, "Permission group created."));
    }

    // ---- Update ----

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGroupRequest body, CancellationToken cancellationToken)
    {
        var group = await LoadAsync(id, cancellationToken);
        if (group is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Permission group not found."));
        }

        var keys = NormalizeKnown(body.PermissionKeys, out var unknown);
        if (unknown.Count > 0)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Unknown permission key(s).", string.Join(", ", unknown)));
        }
        var ceiling = await CheckCeilingAsync(group.TenantId, keys, cancellationToken);
        if (ceiling is not null)
        {
            return ceiling;
        }
        if (!string.Equals(group.Name, body.Name.Trim(), StringComparison.OrdinalIgnoreCase)
            && await _groups.NameExistsAsync(group.TenantId, body.Name.Trim(), group.Id, cancellationToken))
        {
            return Conflict(ApiResponseFactory.Error(ApiErrorCodes.DuplicateGroupName, "A group with this name already exists.", body.Name.Trim()));
        }

        // Capacity limit (WO-119): a new limit below the group's current usage is rejected — the admin must
        // remove members first (AC-PG-003.4). Reducing to null (unlimited) is always allowed.
        if (body.CapacityLimit is { } newLimit)
        {
            var currentUsage = await _groups.CountActiveMembersAsync(group.Id, group.TenantId, null, cancellationToken);
            if (newLimit < currentUsage)
            {
                await _audit.AddAsync(nameof(PermissionGroup), group.Id.ToString(), "CapacityChangeRejected",
                    details: $"Attempted limit {newLimit} is below current usage {currentUsage}.", cancellationToken: cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return BadRequest(ApiResponseFactory.Error(
                    ApiErrorCodes.CapacityBelowUsage,
                    $"The capacity limit ({newLimit}) is below the group's current usage ({currentUsage}). Remove at least {currentUsage - newLimit} member(s) before lowering the limit.",
                    (currentUsage - newLimit).ToString()));
            }
        }

        var before = group.Permissions.Select(p => p.PermissionKey).OrderBy(k => k).ToList();
        var capacityBefore = group.CapacityLimit;
        group.Name = body.Name.Trim();
        group.Description = body.Description?.Trim();
        group.CapacityLimit = body.CapacityLimit;
        _groups.RemovePermissions(group.Permissions.ToList());
        foreach (var key in keys)
        {
            await _groups.AddPermissionAsync(new PermissionGroupPermission { Id = Guid.NewGuid(), PermissionGroupId = group.Id, PermissionKey = key }, cancellationToken);
        }
        _groups.Update(group);
        await _audit.AddAsync(nameof(PermissionGroup), group.Id.ToString(), "Updated",
            details: $"keys before=[{string.Join(",", before)}] after=[{string.Join(",", keys)}]", cancellationToken: cancellationToken);
        if (capacityBefore != group.CapacityLimit)
        {
            await _audit.AddAsync(nameof(PermissionGroup), group.Id.ToString(), "CapacityChanged",
                details: $"{(capacityBefore?.ToString() ?? "unlimited")} -> {(group.CapacityLimit?.ToString() ?? "unlimited")}", cancellationToken: cancellationToken);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Propagate the new permission set to every role composed from this group.
        await _effective.RecomputeForGroupAsync(group.Id, cancellationToken);

        return Ok(ApiResponseFactory.Success(new { groupId = group.Id }, "Permission group updated."));
    }

    // ---- Activate / Deactivate ----

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetGroupStatusRequest body, CancellationToken cancellationToken)
    {
        var group = await LoadAsync(id, cancellationToken);
        if (group is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Permission group not found."));
        }

        group.IsActive = body.IsActive;
        _groups.Update(group);
        await _audit.AddAsync(nameof(PermissionGroup), group.Id.ToString(), "StatusChanged",
            details: body.IsActive ? "Activated" : "Deactivated", cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // A deactivated group contributes zero; reactivation restores its keys.
        await _effective.RecomputeForGroupAsync(group.Id, cancellationToken);

        return Ok(ApiResponseFactory.Success(new { groupId = group.Id, isActive = group.IsActive },
            body.IsActive ? "Permission group activated." : "Permission group deactivated."));
    }

    // ---- Delete ----

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var group = await LoadAsync(id, cancellationToken);
        if (group is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Permission group not found."));
        }

        // Capture affected roles, then remove all composition links and soft-delete the group (REQ-PG-009.3).
        var affectedRoles = (await _groups.GetRolesUsingGroupAsync(id, cancellationToken)).Select(r => r.RoleId).ToList();
        foreach (var link in await _groups.GetRoleLinksAsync(id, cancellationToken))
        {
            _groups.RemoveRoleLink(link);
        }
        _groups.RemovePermissions(group.Permissions.ToList());
        _groups.Remove(group);
        await _audit.AddAsync(nameof(PermissionGroup), group.Id.ToString(), "Deleted", details: group.Name, cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var roleId in affectedRoles)
        {
            await _effective.RecomputeForRoleAsync(roleId, cancellationToken);
            await _audit.AddAsync(nameof(Role), roleId.ToString(), "PermissionGroupRemoved", details: $"group {group.Name} deleted", cancellationToken: cancellationToken);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(new { groupId = id }, "Permission group deleted."));
    }

    // ---- Helpers ----

    private Task<PermissionGroup?> LoadAsync(Guid id, CancellationToken cancellationToken)
        => User.IsSuperAdmin()
            ? _groups.GetByIdUnscopedAsync(id, cancellationToken)
            : _groups.GetByIdAsync(id, cancellationToken);

    private async Task<(Guid TenantId, IActionResult? Error)> ResolveTargetTenantAsync(Guid? requested, CancellationToken cancellationToken)
    {
        var active = User.GetActiveTenantId();
        if (User.IsSuperAdmin() && requested is { } target && target != active)
        {
            var tenant = await _tenants.GetByIdAsync(target, cancellationToken);
            if (tenant is null)
            {
                return (Guid.Empty, NotFound(ApiResponseFactory.Error(ApiErrorCodes.TenantNotFound, "Tenant not found.", target.ToString())));
            }
            if (tenant.Status != TenantStatus.Active)
            {
                return (Guid.Empty, BadRequest(ApiResponseFactory.Error(ApiErrorCodes.TenantInactive, "The tenant is not active.", target.ToString())));
            }
            return (target, null);
        }

        return active is { } a
            ? (a, null)
            : (Guid.Empty, StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller.")));
    }

    /// <summary>Normalises to known catalogue keys (distinct) and reports any unknown ones.</summary>
    private static List<string> NormalizeKnown(IEnumerable<string> keys, out List<string> unknown)
    {
        var distinct = keys.Select(k => k?.Trim() ?? string.Empty).Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        unknown = distinct.Where(k => !Permissions.All.Contains(k)).ToList();
        return distinct.Where(Permissions.All.Contains).ToList();
    }

    /// <summary>Enforces the Tenant Admin ceiling (ADR-003).</summary>
    private async Task<IActionResult?> CheckCeilingAsync(Guid tenantId, IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        if (User.IsSuperAdmin())
        {
            return null;
        }

        var ceiling = await RoleAccess.CeilingAsync(_roles, tenantId, cancellationToken);

        var disallowed = keys.Where(k => !ceiling.Contains(k)).ToList();
        return disallowed.Count == 0
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Error(
                ApiErrorCodes.PermissionCeilingExceeded, "One or more permission keys are outside your tenant's permission ceiling.", string.Join(", ", disallowed)));
    }

    private static PermissionGroupSummaryResponse ToSummary(
        PermissionGroup g, int rolesUsing, int currentUsage, Func<Guid?, string?> nameOf)
        => new(g.Id, g.Name, g.Description, g.Permissions.Count, rolesUsing, g.IsActive, g.TenantId, g.Tenant?.Name,
            g.CapacityLimit, currentUsage, IsFull(g.CapacityLimit, currentUsage),
            nameOf(g.CreatedById), g.CreatedOnUtc, nameOf(g.UpdatedById), g.UpdatedOnUtc);

    /// <summary>A group is full when it has a limit and usage has reached (or exceeded) it.</summary>
    private static bool IsFull(int? capacityLimit, int currentUsage) => capacityLimit is { } limit && currentUsage >= limit;

    private bool CanDelete(PermissionGroup g) => true; // groups (unlike templates) are always deletable by managers

    private PermissionGroupDetailResponse ToDetail(
        PermissionGroup g,
        IReadOnlyList<(Guid RoleId, string RoleName)> rolesUsing,
        IReadOnlyList<AuditTrailEntry> audit,
        int currentUsage,
        Func<string?, string?> actorName,
        RecordAudit provenance)
        => new(
            g.Id, g.Name, g.Description, g.IsActive, g.TenantId, g.Tenant?.Name,
            g.Permissions.Select(p => p.PermissionKey).OrderBy(k => k).ToList(),
            rolesUsing.Select(r => new RoleUsingGroupResponse(r.RoleId, r.RoleName)).ToList(),
            audit.Select(a => new PermissionGroupAuditEntryResponse(a.Action, actorName(a.PerformedBy), a.CreatedDate, a.Details)).ToList(),
            CanDelete(g),
            provenance,
            g.CapacityLimit, currentUsage, IsFull(g.CapacityLimit, currentUsage));
}
