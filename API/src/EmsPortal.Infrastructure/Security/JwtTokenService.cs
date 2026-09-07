using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Configuration;
using EmsPortal.Shared.Security;
using Microsoft.Extensions.Options;

namespace EmsPortal.Infrastructure.Security;

/// <summary>
/// Issues RS256 access tokens carrying sub/email/activeTenantId/tokenVersion, one <c>role</c> claim
/// per distinct role NAME the user holds in the active tenant (multi-role), the union of those roles'
/// effective permissions, and a serialized tenantAssignments array grouped by tenant
/// (<c>[{ tenantId, roleNames:[...] }]</c>). A Super Admin assignment grants SuperAdmin in any tenant
/// (AC-ADM-008.6).
/// </summary>
internal sealed class JwtTokenService : IJwtTokenService
{
    private readonly ISigningKeyProvider _signingKeyProvider;
    private readonly AuthenticationOptions _options;

    public JwtTokenService(ISigningKeyProvider signingKeyProvider, IOptions<AuthenticationOptions> options)
    {
        _signingKeyProvider = signingKeyProvider;
        _options = options.Value;
    }

    public AccessToken CreateAccessToken(User user, Guid activeTenantId)
    {
        // Tenant assignments grouped by tenant → the distinct role names held in each (multi-role).
        var assignments = user.TenantRoles
            .GroupBy(r => r.TenantId)
            .Select(g => new
            {
                tenantId = g.Key,
                roleNames = g.Select(RoleNameOf).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            })
            .ToArray();

        var claims = new List<Claim>
        {
            new(ClaimTypeNames.Subject, user.Id.ToString()),
            new("email", user.Email),
            new(ClaimTypeNames.ActiveTenantId, activeTenantId.ToString()),
            new(ClaimTypeNames.TokenVersion, user.TokenVersion.ToString()),
            new(ClaimTypeNames.TenantAssignments, JsonSerializer.Serialize(assignments)),
        };

        // One `role` claim per distinct role NAME the user holds in the active tenant (RequireRole
        // matches any). A Super Admin assignment anywhere collapses to a single SuperAdmin claim.
        foreach (var roleName in ResolveRoleNames(user, activeTenantId))
        {
            claims.Add(new Claim(ClaimTypeNames.Role, roleName));
        }

        // Effective permissions for the active tenant — the union of the assigned roles' permission
        // sets — drive permission-based authorization and the client's permission-gated UI.
        foreach (var permission in ResolvePermissions(user, activeTenantId))
        {
            claims.Add(new Claim(ClaimTypeNames.Permission, permission));
        }

        var expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes <= 0
            ? AuthenticationOptions.DefaultAccessTokenMinutes
            : _options.AccessTokenMinutes);
        var token = new JwtSecurityToken(
            issuer: string.IsNullOrWhiteSpace(_options.Issuer) ? null : _options.Issuer,
            audience: string.IsNullOrWhiteSpace(_options.Audience) ? null : _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: _signingKeyProvider.SigningCredentials);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        var expiresIn = (int)(expires - DateTime.UtcNow).TotalSeconds;
        return new AccessToken(encoded, expiresIn);
    }

    /// <summary>
    /// The distinct role NAMES the user holds in the active tenant — one JWT <c>role</c> claim per
    /// name. A Super Admin assignment (in any tenant) grants SuperAdmin everywhere (AC-ADM-008.6);
    /// otherwise the RBAC role name of each active-tenant assignment (falling back to the legacy enum
    /// name only when no RBAC role is linked).
    /// </summary>
    private static IReadOnlyList<string> ResolveRoleNames(User user, Guid activeTenantId)
    {
        if (user.TenantRoles.Any(IsSuperAdminAssignment))
        {
            return new[] { Roles.SuperAdmin };
        }

        return user.TenantRoles
            .Where(r => r.TenantId == activeTenantId)
            .Select(RoleNameOf)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The user's effective permissions in the active tenant: the full catalogue for a Super Admin
    /// (assigned anywhere), otherwise the UNION across every active-tenant assignment of that
    /// assignment's effective permission set — each role's direct keys ∪ its group-derived cache
    /// (<see cref="Role.EffectivePermissions"/>, Permission Groups feature) — falling back to the
    /// seeded set for the role name when a role carries no keys from either source.
    /// </summary>
    private static IReadOnlyList<string> ResolvePermissions(User user, Guid activeTenantId)
    {
        if (user.TenantRoles.Any(IsSuperAdminAssignment))
        {
            return Permissions.ForSuperAdmin();
        }

        return user.TenantRoles
            .Where(r => r.TenantId == activeTenantId)
            .SelectMany(EffectivePermissionsFor)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The effective permission keys contributed by a single assignment.</summary>
    private static IEnumerable<string> EffectivePermissionsFor(UserTenantRole assignment)
    {
        if (assignment.RoleEntity is { } roleEntity)
        {
            // Direct role permissions ∪ permissions contributed by composed Permission Groups.
            var effective = roleEntity.Permissions
                .Concat(roleEntity.EffectivePermissions)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (effective.Count > 0)
            {
                return effective;
            }
        }

        return Permissions.ForSystemRole(RoleNameOf(assignment));
    }

    /// <summary>The RBAC role name for an assignment, falling back to the legacy enum string.</summary>
    private static string RoleNameOf(UserTenantRole assignment)
        => assignment.RoleEntity?.Name ?? assignment.Role.ToString();

    /// <summary>True when an assignment resolves to the SuperAdmin role (by name, else legacy enum).</summary>
    private static bool IsSuperAdminAssignment(UserTenantRole assignment)
        => string.Equals(RoleNameOf(assignment), Roles.SuperAdmin, StringComparison.Ordinal);
}
