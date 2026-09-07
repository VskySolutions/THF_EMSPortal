using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EmsPortal.Api.Models.Auth;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Email;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Configuration;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Authentication and session management (WO-39): login, refresh, logout, logout-all, tenant switch,
/// profile, change-password (Admin User &amp; Role Management).
/// </summary>
[ApiController]
[Produces("application/json")]
[Tags("Auth")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class AuthController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwt;
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AuthenticationOptions _options;
    private readonly IEmailDispatcher _emailDispatcher;
    private readonly IPasswordResetTokenRepository _passwordResetTokens;
    private readonly string _baseUrl;

    public AuthController(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwt,
        ITenantRepository tenants,
        IUnitOfWork unitOfWork,
        IOptions<AuthenticationOptions> options,
        IEmailDispatcher emailDispatcher,
        IPasswordResetTokenRepository passwordResetTokens,
        IOptions<AppOptions> appOptions)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _tenants = tenants;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _emailDispatcher = emailDispatcher;
        _passwordResetTokens = passwordResetTokens;
        _baseUrl = appOptions.Value.BaseUrl;
    }

    [HttpPost("/api/auth/login")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<LoginTokenResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _users.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash, user.Salt))
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("Invalid email or password."));
        }

        if (!user.IsActive)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("Account is disabled."));
        }

        var activeTenantId = user.TenantRoles.FirstOrDefault()?.TenantId ?? Guid.Empty;
        var access = _jwt.CreateAccessToken(user, activeTenantId);
        var refresh = await IssueRefreshTokenAsync(user.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(
            new LoginTokenResponse(
                access.Token, access.ExpiresInSeconds, refresh, RefreshExpiresInSeconds, user.MustChangePassword),
            "Login successful."));
    }

    [HttpPost("/api/auth/refresh")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<RefreshTokenResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        var stored = await _refreshTokens.GetByHashAsync(HashToken(request.RefreshToken), cancellationToken);
        if (stored is null || stored.IsRevoked || stored.ExpiresAt <= DateTime.UtcNow)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("Refresh token is invalid or expired."));
        }

        var user = await _users.GetByIdAsync(stored.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("Account is disabled."));
        }

        // Rotate: revoke the presented token, issue a new one.
        stored.IsRevoked = true;
        _refreshTokens.Update(stored);
        var newRefresh = await IssueRefreshTokenAsync(user.Id, cancellationToken);

        var activeTenantId = user.TenantRoles.FirstOrDefault()?.TenantId ?? Guid.Empty;
        var access = _jwt.CreateAccessToken(user, activeTenantId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(
            new RefreshTokenResponse(access.Token, access.ExpiresInSeconds, newRefresh, RefreshExpiresInSeconds),
            "Token refreshed."));
    }

    [HttpPost("/api/auth/logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        user.TokenVersion++; // invalidates all access tokens
        _users.Update(user);

        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var stored = await _refreshTokens.GetByHashAsync(HashToken(request.RefreshToken), cancellationToken);
            if (stored is not null && stored.UserId == user.Id)
            {
                stored.IsRevoked = true;
                _refreshTokens.Update(stored);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { message = "Logged out." }, "Logged out."));
    }

    [HttpPost("/api/auth/logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        user.TokenVersion++;
        _users.Update(user);
        await _refreshTokens.RevokeAllForUserAsync(user.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(new { message = "Logged out everywhere." }, "Logged out everywhere."));
    }

    [HttpPost("/api/auth/switch-tenant")]
    [Authorize]
    [ProducesResponseType<ApiResponse<SwitchTenantResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SwitchTenant([FromBody] SwitchTenantRequest request, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var isSuperAdmin = user.TenantRoles.Any(IsSuperAdminAssignment);
        var hasAssignment = user.TenantRoles.Any(r => r.TenantId == request.TenantId);
        if (!isSuperAdmin && !hasAssignment)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponseFactory.Forbidden("You are not assigned to the requested tenant."));
        }

        var tenant = await _tenants.GetByIdAsync(request.TenantId, cancellationToken);
        if (tenant is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("Tenant not found."));
        }

        var access = _jwt.CreateAccessToken(user, request.TenantId);
        // The distinct role names for the switched-to tenant (a Super Admin assignment wins everywhere).
        var roleNames = isSuperAdmin
            ? new[] { Roles.SuperAdmin }
            : user.TenantRoles
                .Where(r => r.TenantId == request.TenantId)
                .Select(RoleNameOf)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
        // Refresh token is intentionally not rotated on switch (AC-ADM-011.5).
        return Ok(ApiResponseFactory.Success(
            new SwitchTenantResponse(access.Token, access.ExpiresInSeconds, tenant.Identifier, roleNames), "Tenant switched."));
    }

    [HttpGet("/api/auth/profile")]
    [Authorize]
    [ProducesResponseType<ApiResponse<UserProfileResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        // Group the (multi-role) assignments by tenant → one membership row carrying all role names.
        var memberships = new List<TenantMembershipDto>();
        foreach (var group in user.TenantRoles.GroupBy(r => r.TenantId))
        {
            var tenant = await _tenants.GetByIdAsync(group.Key, cancellationToken);
            var roleNames = group
                .Select(RoleNameOf)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
            memberships.Add(new TenantMembershipDto(
                group.Key,
                tenant?.Identifier ?? string.Empty,
                tenant?.Name ?? string.Empty,
                roleNames,
                tenant?.TimeZoneId ?? "UTC"));
        }

        return Ok(ApiResponseFactory.Success(
            new UserProfileResponse(user.Id, user.Email, user.DisplayName, memberships), "Profile retrieved."));
    }

    [HttpPut("/api/users/me/change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash, user.Salt))
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", "Current password is incorrect."));
        }

        var (hash, salt) = _passwordHasher.Hash(request.NewPassword);
        user.PasswordHash = hash;
        user.Salt = salt;
        user.MustChangePassword = false;
        user.TokenVersion++; // invalidate all sessions
        _users.Update(user);
        await _refreshTokens.RevokeAllForUserAsync(user.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Best-effort confirmation email via the user's tenant active SMTP account.
        var tenantForEmail = User.GetActiveTenantId() ?? user.TenantRoles.FirstOrDefault()?.TenantId;
        if (tenantForEmail is { } tid)
        {
            _emailDispatcher.Enqueue(tid, EmailTemplateKey.PasswordChanged, user.Email,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["FullName"] = user.DisplayName,
                    // The app's display format, MM/DD/YYYY with a 12-hour clock — this string is read by a
                    // person in an email, not parsed by anything.
                    ["ChangedAtUtc"] = DateTime.UtcNow.ToString("MM/dd/yyyy hh:mm tt", CultureInfo.InvariantCulture),
                });
        }

        return Ok(ApiResponseFactory.Success(new { message = "Password changed." }, "Password changed."));
    }

    /// <summary>The RBAC role name for an assignment, falling back to the legacy enum string.</summary>
    private static string RoleNameOf(UserTenantRole assignment)
        => assignment.RoleEntity?.Name ?? assignment.Role.ToString();

    /// <summary>True when an assignment resolves to the SuperAdmin role (by name, else legacy enum).</summary>
    private static bool IsSuperAdminAssignment(UserTenantRole assignment)
        => string.Equals(RoleNameOf(assignment), Roles.SuperAdmin, StringComparison.Ordinal);

    private Task<User?> CurrentUserAsync(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        return userId is null ? Task.FromResult<User?>(null) : _users.GetByIdAsync(userId.Value, cancellationToken);
    }

    /// <summary>Configured refresh-token lifetime in days, falling back to the documented default.</summary>
    private int RefreshTokenDays => _options.RefreshTokenDays <= 0
        ? AuthenticationOptions.DefaultRefreshTokenDays
        : _options.RefreshTokenDays;

    /// <summary>The same window in seconds, as returned to the client.</summary>
    private int RefreshExpiresInSeconds => (int)TimeSpan.FromDays(RefreshTokenDays).TotalSeconds;

    private async Task<string> IssueRefreshTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var plaintext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await _refreshTokens.AddAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashToken(plaintext),
            ExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays),
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow,
        }, cancellationToken);
        return plaintext;
    }

    private static string HashToken(string token)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // -------------------- Self-service password reset --------------------

    /// <summary>How long an emailed reset link stays valid.</summary>
    private const int ResetTokenLifetimeMinutes = 60;

    /// <summary>
    /// Starts the "forgot password" flow: emails a one-time reset link to the address if it belongs to
    /// an active account.
    /// </summary>
    [HttpPost("/api/auth/forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        const string Message = "If that email address has an account, a reset link is on its way.";
        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            return Ok(ApiResponseFactory.Success(new { message = Message }, Message));
        }

        var user = await _users.GetByEmailAsync(email, cancellationToken);
        // Inactive accounts and unknown addresses take the same silent path as a success.
        if (user is null || !user.IsActive)
        {
            return Ok(ApiResponseFactory.Success(new { message = Message }, Message));
        }

        var now = DateTime.UtcNow;

        // One live link per account: requesting again supersedes the previous email.
        await _passwordResetTokens.InvalidateAllForUserAsync(user.Id, now, cancellationToken);

        // 32 random bytes, URL-safe. Only its hash is stored, so the plaintext exists solely in the email.
        var plaintext = Base64UrlToken();
        await _passwordResetTokens.AddAsync(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(plaintext),
            ExpiresAtUtc = now.AddMinutes(ResetTokenLifetimeMinutes),
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // The flow is anonymous, so there is no active tenant — send through the user's first assignment,
        // which is the same fallback the change-password confirmation uses.
        if (user.TenantRoles.FirstOrDefault()?.TenantId is { } tenantId)
        {
            _emailDispatcher.Enqueue(tenantId, EmailTemplateKey.PasswordResetLink, user.Email,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["FullName"] = user.Person?.FullName ?? user.DisplayName,
                    ["ResetUrl"] = $"{_baseUrl.TrimEnd('/')}/auth/reset-password?token={Uri.EscapeDataString(plaintext)}",
                    ["ExpiryMinutes"] = ResetTokenLifetimeMinutes.ToString(),
                });
        }

        return Ok(ApiResponseFactory.Success(new { message = Message }, Message));
    }

    /// <summary>Completes the flow: redeems the token and sets the new password.</summary>
    [HttpPost("/api/auth/reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordWithTokenRequest request, CancellationToken cancellationToken)
    {
        // One message for every failure mode — expired, spent, forged — so nothing about a token's state
        // can be probed from the outside.
        IActionResult Invalid() => BadRequest(ApiResponseFactory.Error(
            ApiErrorCodes.ValidationFailed, "Validation failed.",
            "This reset link is invalid or has expired. Please request a new one."));

        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return Invalid();
        }

        var token = await _passwordResetTokens.GetByHashAsync(HashToken(request.Token.Trim()), cancellationToken);
        var now = DateTime.UtcNow;
        if (token is null || token.UsedOnUtc is not null || token.ExpiresAtUtc <= now)
        {
            return Invalid();
        }

        var user = await _users.GetByIdAsync(token.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Invalid();
        }

        var (hash, salt) = _passwordHasher.Hash(request.NewPassword);
        user.PasswordHash = hash;
        user.Salt = salt;
        // They chose this password deliberately, so do not force another change at sign-in.
        user.MustChangePassword = false;
        user.TokenVersion++; // invalidate all sessions
        _users.Update(user);

        token.UsedOnUtc = now;
        _passwordResetTokens.Update(token);

        await _refreshTokens.RevokeAllForUserAsync(user.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(
            new { message = "Your password has been reset." }, "Your password has been reset."));
    }

    /// <summary>A 256-bit URL-safe random token (no padding), suitable for a query string.</summary>
    private static string Base64UrlToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
