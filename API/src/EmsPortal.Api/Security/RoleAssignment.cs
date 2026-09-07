using EmsPortal.Application.Abstractions.Auditing;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Api.Security;

/// <summary>
/// The rules every (user, tenant, role) assignment answers to, wherever it is made: the user page,
/// which reconciles one person's whole role set, and the role page.
/// </summary>
internal static class RoleAssignment
{
    /// <summary>
    /// Maps an RBAC role to a legacy fixed-tier enum for the transition period: system roles map by
    /// name; custom roles fall back to an explicit enum if given.
    /// </summary>
    public static UserRole MapLegacyRole(Role roleEntity, string? explicitRole)
    {
        if (roleEntity.IsSystem && Enum.TryParse<UserRole>(roleEntity.Name, ignoreCase: false, out var system))
        {
            return system;
        }

        if (!string.IsNullOrWhiteSpace(explicitRole) && Enum.TryParse<UserRole>(explicitRole, ignoreCase: false, out var explicitEnum))
        {
            return explicitEnum;
        }

        return UserRole.Custom;
    }

    /// <summary>A capacity limit that stands in the way of a grant: the group at its limit, and that limit.</summary>
    public sealed record CapacityBlock(string GroupName, int Limit)
    {
        public string Message => $"Cannot assign this role: permission group '{GroupName}' is at its capacity limit ({Limit}).";
    }

    /// <summary>
    /// Permission Group capacity (WO-119) for a grant about to happen: for each capped group in the
    /// tenant composed by a newly-granted role.
    /// </summary>
    public static async Task<CapacityBlock?> FindCapacityBlockAsync(
        IPermissionGroupRepository permissionGroups,
        IAuditTrailService audit,
        Guid userId,
        bool userIsActive,
        Guid tenantId,
        IReadOnlyList<Guid> addedRoleIds,
        CancellationToken cancellationToken)
    {
        // Inactive users never count toward usage, and no new roles means no growth.
        if (!userIsActive || addedRoleIds.Count == 0)
        {
            return null;
        }

        var groups = await permissionGroups.GetGroupsByRolesAsync(addedRoleIds, cancellationToken);
        foreach (var group in groups.Where(g => g.TenantId == tenantId && g.CapacityLimit.HasValue))
        {
            var limit = group.CapacityLimit!.Value;

            // Already a member (via a role they keep) → not a new distinct user → no growth.
            if (await permissionGroups.IsUserActiveMemberAsync(group.Id, tenantId, userId, cancellationToken))
            {
                continue;
            }

            var projected = await permissionGroups.CountActiveMembersAsync(group.Id, tenantId, null, cancellationToken) + 1;
            if (projected > limit)
            {
                await audit.AddAsync(nameof(PermissionGroup), group.Id.ToString(), "CapacityLimitReached",
                    details: $"Assigning a role composing '{group.Name}' to user {userId} would raise usage to {projected}, above the limit of {limit}.",
                    cancellationToken: cancellationToken);
                return new CapacityBlock(group.Name, limit);
            }
        }

        return null;
    }
}
