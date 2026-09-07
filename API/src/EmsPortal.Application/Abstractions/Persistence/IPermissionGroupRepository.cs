using EmsPortal.Domain.Entities;
using EmsPortal.Application.Common;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// Data access for Permission Groups, their permission-key junction rows, role-composition links, and
/// group templates.
/// </summary>
public interface IPermissionGroupRepository
{
    // ---- Groups ----
    Task<(IReadOnlyList<PermissionGroup> Items, int Total)> ListAsync(
        Guid? tenantId, string? search, bool? isActive, bool? usedByRoles, string? category,
        SortRequest sort, int page, int limit, CancellationToken cancellationToken = default);

    /// <summary>Group with its permission keys loaded; tenant-scoped by the ambient filter.</summary>
    Task<PermissionGroup?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Group with its permission keys loaded, ignoring the tenant filter (Super Admin cross-tenant).</summary>
    Task<PermissionGroup?> GetByIdUnscopedAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(Guid tenantId, string name, Guid excludeId, CancellationToken cancellationToken = default);

    Task AddAsync(PermissionGroup group, CancellationToken cancellationToken = default);

    void Update(PermissionGroup group);

    void Remove(PermissionGroup group);

    // ---- Permission junction rows ----
    void RemovePermissions(IEnumerable<PermissionGroupPermission> permissions);

    Task AddPermissionAsync(PermissionGroupPermission permission, CancellationToken cancellationToken = default);

    // ---- Role composition links ----
    /// <summary>The groups (active and inactive) composed into a role, each with its permission keys loaded.</summary>
    Task<IReadOnlyList<PermissionGroup>> GetByRoleAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RolePermissionGroup>> GetRoleLinksAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<RolePermissionGroup?> GetRoleLinkAsync(Guid roleId, Guid groupId, CancellationToken cancellationToken = default);

    /// <summary>Roles (id + name) that currently include the group.</summary>
    Task<IReadOnlyList<(Guid RoleId, string RoleName)>> GetRolesUsingGroupAsync(Guid groupId, CancellationToken cancellationToken = default);

    Task<int> CountRolesUsingGroupAsync(Guid groupId, CancellationToken cancellationToken = default);

    Task AddRoleLinkAsync(RolePermissionGroup link, CancellationToken cancellationToken = default);

    void RemoveRoleLink(RolePermissionGroup link);

    /// <summary>The non-deleted permission groups composed by ANY of the given roles (may span tenants).</summary>
    Task<IReadOnlyList<PermissionGroup>> GetGroupsByRolesAsync(IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);

    // ---- Capacity / usage (WO-119) ----

    /// <summary>
    /// The group's current usage: the number of DISTINCT active users who hold at least one active role
    /// composing the group, within <paramref name="tenantId"/> (the group's tenant). Any
    /// <paramref name="additionalRoleIds"/> are treated as if they already composed the group, to
    /// project usage before a role is composed in (AC-PG-013.2).
    /// </summary>
    Task<int> CountActiveMembersAsync(
        Guid groupId, Guid tenantId, IEnumerable<Guid>? additionalRoleIds = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch current-usage per group (distinct active members), keyed by group id; absent groups have
    /// usage 0.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountActiveMembersForGroupsAsync(
        IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken = default);

    /// <summary>Whether the user already holds an active role composing the group within the group's tenant.</summary>
    Task<bool> IsUserActiveMemberAsync(Guid groupId, Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

    // ---- Templates ----
    Task<IReadOnlyList<PermissionGroupTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default);

    Task<PermissionGroupTemplate?> GetTemplateByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddTemplateAsync(PermissionGroupTemplate template, CancellationToken cancellationToken = default);

    void UpdateTemplate(PermissionGroupTemplate template);

    void RemoveTemplate(PermissionGroupTemplate template);
}
