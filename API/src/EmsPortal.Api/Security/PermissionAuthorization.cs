using System.Security.Claims;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;

namespace EmsPortal.Api.Security;

/// <summary>Shared constants for permission-based authorization policies.</summary>
public static class PermissionAuthorizationDefaults
{
    /// <summary>Prefix for the dynamically-materialized per-permission policies (e.g. "perm:tenants.write").</summary>
    public const string PolicyPrefix = "perm:";
}

/// <summary>
/// An authorization requirement satisfied when the caller holds ANY of the listed permission keys (see
/// <see cref="Permissions"/>).
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(params string[] permissions) => Permissions = permissions;

    public IReadOnlyList<string> Permissions { get; }
}

/// <summary>Grants a <see cref="PermissionRequirement"/> when the caller carries any of its permission claims.</summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (requirement.Permissions.Any(permission => HasPermission(context.User, permission)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static bool HasPermission(ClaimsPrincipal user, string permission)
    {
        if (user.HasClaim(ClaimTypeNames.Permission, permission))
        {
            return true;
        }

        // Fallback: the union of the seeded permission sets across ALL of the caller's role claims
        // (a multi-role user emits one `role` claim per role name).
        return user.FindAll(ClaimTypeNames.Role)
            .Any(role => Permissions.ForSystemRole(role.Value).Contains(permission));
    }
}
