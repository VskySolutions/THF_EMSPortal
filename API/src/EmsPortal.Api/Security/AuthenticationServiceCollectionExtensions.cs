using System.Security.Cryptography;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Shared.Configuration;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using EmsPortal.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using AuthenticationOptions = EmsPortal.Shared.Configuration.AuthenticationOptions;

namespace EmsPortal.Api.Security;

/// <summary>
/// Registers the two authentication schemes (platform JWT + API key), the <c>AnyOf</c> composite
/// scheme, and the three named RBAC policies.
/// </summary>
public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddEmsPortalAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authOptions = configuration
            .GetSection(ConfigurationSections.Authentication)
            .Get<AuthenticationOptions>() ?? new AuthenticationOptions();

        // Resolve the audit actor from the authenticated HTTP user. Registered before
        // AddInfrastructure so it takes precedence over the system-identity default.
        services.AddHttpContextAccessor();
        services.AddScoped<IActorAccessor, HttpContextActorAccessor>();

        services
            .AddAuthentication(AuthenticationSchemes.AnyOf)
            .AddPolicyScheme(AuthenticationSchemes.AnyOf, AuthenticationSchemes.AnyOf, options =>
            {
                // API key callers send X-Api-Key; everyone else is validated as a JWT.
                var headerName = string.IsNullOrWhiteSpace(authOptions.ApiKeyHeaderName)
                    ? "X-Api-Key"
                    : authOptions.ApiKeyHeaderName;
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.ContainsKey(headerName)
                        ? AuthenticationSchemes.ApiKey
                        : AuthenticationSchemes.Jwt;
            })
            .AddJwtBearer(AuthenticationSchemes.Jwt, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = BuildTokenValidationParameters(authOptions);
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateTokenVersionAsync,
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                AuthenticationSchemes.ApiKey, _ => { });

        // The platform issues and validates RS256 tokens with the same key (WO-39); resolve
        // it from the signing-key provider so issuance and validation always agree.
        services.AddOptions<JwtBearerOptions>(AuthenticationSchemes.Jwt)
            .Configure<ISigningKeyProvider>((jwtOptions, keyProvider) =>
            {
                jwtOptions.TokenValidationParameters.IssuerSigningKey = keyProvider.ValidationKey;
                jwtOptions.TokenValidationParameters.ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 };
            });

        services.AddAuthorization(options =>
        {
            // Default: any authenticated caller via either scheme.
            options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
                    AuthenticationSchemes.Jwt, AuthenticationSchemes.ApiKey)
                .RequireAuthenticatedUser()
                .Build();

            // RequireRole succeeds when ANY of the caller's `role` claims matches — multi-role users
            // emit one claim per role name and RoleClaimType is `role` (see BuildTokenValidationParameters).
            options.AddPolicy(AuthorizationPolicies.SuperAdminOnly, policy => policy
                .AddAuthenticationSchemes(AuthenticationSchemes.Jwt, AuthenticationSchemes.ApiKey)
                .RequireAuthenticatedUser()
                .RequireRole(Roles.SuperAdmin));

            options.AddPolicy(AuthorizationPolicies.TenantAdminOrAbove, policy => policy
                .AddAuthenticationSchemes(AuthenticationSchemes.Jwt, AuthenticationSchemes.ApiKey)
                .RequireAuthenticatedUser()
                .RequireRole(Roles.SuperAdmin, Roles.TenantAdmin));
        });

        // Permission-based authorization: a dynamic policy provider materializes "perm:<key>" policies (see
        // RequirePermissionAttribute) and a handler grants them from permission claims.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // Session issuance shared by password and Microsoft sign-in, and the one-time codes that carry a
        // Microsoft sign-in from the API's callback to the SPA (process-wide, so a singleton).
        services.AddScoped<SessionTokenIssuer>();
        services.AddSingleton<MicrosoftLoginCodeStore>();

        return services;
    }

    private static TokenValidationParameters BuildTokenValidationParameters(AuthenticationOptions authOptions)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(authOptions.Issuer),
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(authOptions.Audience),
            ValidAudience = authOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(authOptions.ClockSkewSeconds),
            NameClaimType = ClaimTypeNames.Subject,
            RoleClaimType = ClaimTypeNames.Role,
        };

        if (!string.IsNullOrWhiteSpace(authOptions.PublicKeyPem))
        {
            // Primary path: RS256 with the platform's RSA public key.
            var rsa = RSA.Create();
            rsa.ImportFromPem(authOptions.PublicKeyPem);
            parameters.IssuerSigningKey = new RsaSecurityKey(rsa);
            parameters.ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 };
        }
        else if (!string.IsNullOrWhiteSpace(authOptions.SigningKey))
        {
            // Development fallback: symmetric HS256 until an RSA key is provisioned.
            parameters.IssuerSigningKey = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(authOptions.SigningKey));
            parameters.ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 };
        }

        return parameters;
    }

    private static async Task ValidateTokenVersionAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var subject = principal?.FindFirst(ClaimTypeNames.Subject)?.Value;
        var tokenVersionClaim = principal?.FindFirst(ClaimTypeNames.TokenVersion)?.Value;

        if (!Guid.TryParse(subject, out var userId) || !int.TryParse(tokenVersionClaim, out var tokenVersion))
        {
            // No usable identity/version claims — leave as-is; signature already validated.
            return;
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<ITokenVersionValidator>();
        var isValid = await validator.IsValidAsync(userId, tokenVersion, context.HttpContext.RequestAborted);

        // Reject tokens invalidated by password change / deactivation. The Phase 1
        // validator accepts all versions; WO-38 adds the DB-backed comparison.
        if (!isValid)
        {
            context.Fail("Token has been invalidated.");
        }
    }
}
