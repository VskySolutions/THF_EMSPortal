using Microsoft.AspNetCore.Authorization;

namespace EmsPortal.Api.Security;

/// <summary>Requires the caller to hold the given permission (see <see cref="Shared.Security.Permissions"/>).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string permission)
        => Policy = PermissionAuthorizationDefaults.PolicyPrefix + permission;
}

/// <summary>
/// Requires the caller to hold ANY ONE of the given permissions (an OR gate) — e.g. a screen open to
/// several workflow capabilities.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireAnyPermissionAttribute : AuthorizeAttribute
{
    public RequireAnyPermissionAttribute(params string[] permissions)
        => Policy = PermissionAuthorizationDefaults.PolicyPrefix + string.Join('|', permissions);
}
