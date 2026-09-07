using System.Security.Claims;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Security;

namespace EmsPortal.Api.Security;

/// <summary>Who may see a role, who may change it, and how far a tenant's admins may go when they write one.</summary>
internal static class RoleAccess
{
    /// <summary>May read the role: it is a platform role, or the caller's own tenant owns it.</summary>
    public static bool CanSee(ClaimsPrincipal user, Role role)
    {
        if (user.IsSuperAdmin())
        {
            return true;
        }
        if (role.IsSystem && string.Equals(role.Name, Roles.SuperAdmin, StringComparison.Ordinal))
        {
            return false;
        }
        return role.TenantId is null || Owns(user, role);
    }

    /// <summary>May edit or delete the role.</summary>
    public static bool CanManage(ClaimsPrincipal user, Role role)
        => user.IsSuperAdmin() || Owns(user, role);

    private static bool Owns(ClaimsPrincipal user, Role role)
        => role.TenantId is { } owner && user.GetActiveTenantId() is { } active && owner == active;

    /// <summary>
    /// The permission keys a non-Super-Admin may put into a role or a permission group within a tenant
    /// (the tenant ceiling, ADR-003): everything a Tenant Admin holds.
    /// </summary>
    public static async Task<HashSet<string>> CeilingAsync(
        IRoleRepository roles, Guid tenantId, CancellationToken cancellationToken)
    {
        var ceiling = new HashSet<string>(Permissions.ForTenantAdmin(), StringComparer.Ordinal);
        foreach (var role in await roles.ListByTenantAsync(tenantId, cancellationToken))
        {
            ceiling.UnionWith(role.Permissions);
            ceiling.UnionWith(role.EffectivePermissions);
        }
        return ceiling;
    }
}
