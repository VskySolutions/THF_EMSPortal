using System.Security.Claims;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Security;

namespace EmsPortal.Api.Security;

/// <summary>
/// Maps a Universal Feature target <see cref="EntityType"/> to the base read permission of its parent
/// entity, and checks it on the current principal.
/// </summary>
public static class UniversalFeatureEntityAccess
{
    /// <summary>The base read permission(s) gating UF access to a given entity type.</summary>
    public static IReadOnlyList<string> RequiredReadPermissions(EntityType entityType) => entityType switch
    {
        EntityType.Tenant => new[] { Permissions.TenantsRead },
        EntityType.User => new[] { Permissions.UsersRead },
        EntityType.UserGroup => new[] { Permissions.UsersRead },
        // Conversation/activity/attachments on a REMS record gate on the REMS read permission, so the
        // request thread (REQ-REMS-003) is the shared Conversations feature rather than a bespoke controller.
        EntityType.Rems => new[] { Permissions.RemsRequestsRead },
        _ => new[] { Permissions.UsersRead },
    };

    /// <summary>True when the caller may read the given entity type (and therefore its UF data).</summary>
    public static bool CanAccess(this ClaimsPrincipal principal, EntityType entityType)
        => principal.IsSuperAdmin() || RequiredReadPermissions(entityType).Any(principal.HasPermission);
}
