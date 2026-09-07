using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace EmsPortal.Api.Security;

/// <summary>Materializes per-permission authorization policies on demand.</summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        => _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PermissionAuthorizationDefaults.PolicyPrefix, StringComparison.Ordinal))
        {
            // The suffix is one permission, or several pipe-separated for an any-of (OR) requirement.
            var permissions = policyName[PermissionAuthorizationDefaults.PolicyPrefix.Length..]
                .Split('|', StringSplitOptions.RemoveEmptyEntries);
            var policy = new AuthorizationPolicyBuilder(AuthenticationSchemes.Jwt, AuthenticationSchemes.ApiKey)
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permissions))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}
