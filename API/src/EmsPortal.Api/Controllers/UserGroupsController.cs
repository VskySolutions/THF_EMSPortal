using EmsPortal.Api.Models.Users;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Auditing;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Tenant-scoped user groups: a way to segment/tag users (independent of RBAC roles) so they can be
/// listed and filtered by group name.
/// </summary>
[ApiController]
[Route("/api/admin/user-groups")]
[Produces("application/json")]
[Tags("User Groups")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class UserGroupsController : ControllerBase
{
    private readonly IUserGroupRepository _groups;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditTrailService _audit;

    public UserGroupsController(IUserGroupRepository groups, IUserRepository users, IUnitOfWork unitOfWork, IAuditTrailService audit)
    {
        _groups = groups;
        _users = users;
        _unitOfWork = unitOfWork;
        _audit = audit;
    }

    [HttpGet]
    [RequirePermission(Permissions.UsersRead)]
    public async Task<IActionResult> List(
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var groups = await _groups.ListAsync(search, cancellationToken);
        var counts = await _groups.GetMemberCountsAsync(cancellationToken);
        // Resolve the audit actor ids to display names for the list's Created/Updated By columns. Both ids
        // in one lookup — resolving only creators would leave an updater who never created a row unnamed.
        var actorNames = await _users.GetFullNamesAsync(
            groups.SelectMany(g => new[] { g.CreatedById, g.UpdatedById })
                .Where(id => id.HasValue).Select(id => id!.Value),
            cancellationToken);
        string? NameOf(Guid? id) => id is { } uid && actorNames.TryGetValue(uid, out var n) ? n : null;

        var data = groups.Select(g => new UserGroupResponse(
            g.Id, g.Name, g.Description, counts.TryGetValue(g.Id, out var c) ? c : 0,
            NameOf(g.CreatedById), g.CreatedOnUtc, NameOf(g.UpdatedById), g.UpdatedOnUtc));
        // Ordered after projecting: Members is a count assembled here, not a column on the group.
        return Ok(ApiResponseFactory.Success(ListSorts.Apply(data, sortBy, descending), "User groups retrieved."));
    }

    /// <summary>What the User Groups list may be ordered by.</summary>
    private static readonly SortMap<UserGroupResponse> ListSorts = new SortMap<UserGroupResponse>("updatedOnUtc")
        .Add("name", g => g.Name)
        .Add("description", g => g.Description)
        .Add("memberCount", g => g.MemberCount, g => g.Name)
        .Add("createdOnUtc", g => g.CreatedOnUtc)
        .Add("updatedOnUtc", g => g.UpdatedOnUtc);

    [HttpPost]
    [RequirePermission(Permissions.UsersGroupManagement)]
    [ProducesResponseType<ApiResponse<UserGroupResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateUserGroupRequest request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", "Group name is required."));
        }

        // If the name already exists in this tenant, return it (so the inline "create" in the picker
        // simply resolves to the existing group rather than failing on the unique index).
        if (await _groups.GetByNameAsync(name, cancellationToken) is { } existing)
        {
            return Ok(ApiResponseFactory.Success(
                new UserGroupResponse(
                    existing.Id, existing.Name, existing.Description, 0,
                    null, existing.CreatedOnUtc, null, existing.UpdatedOnUtc),
                "Group already exists."));
        }

        var group = new UserGroup { Id = Guid.NewGuid(), Name = name, Description = request.Description?.Trim() };
        await _groups.AddAsync(group, cancellationToken);
        await _audit.AddAsync(nameof(UserGroup), group.Id.ToString(), "Created", details: name, cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created,
            ApiResponseFactory.Success(
                new UserGroupResponse(
                    group.Id, group.Name, group.Description, 0,
                    null, group.CreatedOnUtc, null, group.UpdatedOnUtc),
                "User group created."));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.UsersGroupManagement)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var group = await _groups.GetByIdAsync(id, cancellationToken);
        if (group is null)
        {
            return NotFound(ApiResponseFactory.NotFound("User group not found."));
        }

        // Soft-delete the memberships first, then the group itself.
        foreach (var member in await _groups.GetMembersByGroupAsync(id, cancellationToken))
        {
            _groups.RemoveMember(member);
        }
        _groups.Remove(group);
        await _audit.AddAsync(nameof(UserGroup), id.ToString(), "Deleted", details: group.Name, cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(new { groupId = id }, "User group deleted."));
    }

    // ---- Members ----

    [HttpGet("{id:guid}/members")]
    [RequirePermission(Permissions.UsersRead)]
    public async Task<IActionResult> Members(Guid id, CancellationToken cancellationToken)
    {
        if (await _groups.GetByIdAsync(id, cancellationToken) is null)
        {
            return NotFound(ApiResponseFactory.NotFound("User group not found."));
        }

        var members = (await _groups.GetMembersWithUsersByGroupAsync(id, cancellationToken)).Where(m => m.User is not null).ToList();
        // Resolve both the member's name and the name of whoever added them to the group.
        var names = await _users.GetFullNamesAsync(
            members.Select(m => m.UserId).Concat(members.Where(m => m.CreatedById.HasValue).Select(m => m.CreatedById!.Value)),
            cancellationToken);
        var data = members.Select(m => new UserGroupMemberResponse(
            m.UserId,
            names.TryGetValue(m.UserId, out var name) ? name : m.User!.DisplayName,
            m.User!.Email,
            m.User!.IsActive,
            m.CreatedById is { } addedById && names.TryGetValue(addedById, out var addedBy) ? addedBy : null,
            m.CreatedOnUtc));
        return Ok(ApiResponseFactory.Success(data, "Group members retrieved."));
    }

    [HttpPost("{id:guid}/members")]
    [RequirePermission(Permissions.UsersGroupManagement)]
    public async Task<IActionResult> AddMembers(Guid id, [FromBody] AddGroupMembersRequest request, CancellationToken cancellationToken)
    {
        if (await _groups.GetByIdAsync(id, cancellationToken) is null)
        {
            return NotFound(ApiResponseFactory.NotFound("User group not found."));
        }

        var existing = (await _groups.GetMembersByGroupAsync(id, cancellationToken)).Select(m => m.UserId).ToHashSet();
        foreach (var userId in (request.UserIds ?? new List<Guid>()).Distinct().Where(u => !existing.Contains(u)))
        {
            await _groups.AddMemberAsync(new UserGroupMember { Id = Guid.NewGuid(), UserGroupId = id, UserId = userId }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { groupId = id }, "Members added."));
    }

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [RequirePermission(Permissions.UsersGroupManagement)]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var member = (await _groups.GetMembersByGroupAsync(id, cancellationToken)).FirstOrDefault(m => m.UserId == userId);
        if (member is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Membership not found."));
        }

        _groups.RemoveMember(member);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { groupId = id, userId }, "Member removed."));
    }
}
