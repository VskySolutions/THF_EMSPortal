using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EmsPortal.Api.Models.Auth;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Configuration;
using EmsPortal.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// "Login with Microsoft" (Microsoft 365 / Entra ID), run server-side so the client secret never leaves the
/// API: <see cref="Start"/> sends the browser to Microsoft, <see cref="Callback"/> redeems the code Microsoft
/// sends back and verifies who signed in, and the browser is then returned to the web app with a one-time code
/// that <see cref="Exchange"/> turns into the same token pair a password login gets. Only existing, active
/// accounts can sign in this way: the Microsoft account's email address must match a user; nobody is created.
/// </summary>
[ApiController]
[Produces("application/json")]
[Tags("Auth")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class MicrosoftSsoController : ControllerBase
{
    /// <summary>The callback route, which is also the redirect URI registered in Entra.</summary>
    public const string CallbackPath = "/api/auth/microsoft/callback";

    // The handshake cookie ties the callback to the browser that started the sign-in (state), the ID token to
    // this attempt (nonce) and the code to this server (PKCE verifier). Scoped to these endpoints, and gone
    // once Microsoft has answered.
    private const string HandshakeCookieName = "EmsPortal.MicrosoftSso";
    private const string HandshakeCookiePath = "/api/auth/microsoft";
    private const string HandshakeProtectorPurpose = "EmsPortal.MicrosoftSso.Handshake";
    private static readonly TimeSpan HandshakeLifetime = TimeSpan.FromMinutes(10);

    // The web app's own random state. It is echoed back to the SPA, which checks it on its callback page, so a
    // sign-in URL crafted elsewhere cannot log its bearer in.
    private static readonly Regex SpaStateFormat = new("^[A-Za-z0-9_-]{16,128}$", RegexOptions.Compiled);

    private readonly IMicrosoftIdentityClient _microsoft;
    private readonly IUserRepository _users;
    private readonly SessionTokenIssuer _sessions;
    private readonly MicrosoftLoginCodeStore _loginCodes;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITimeLimitedDataProtector _handshakeProtector;
    private readonly MicrosoftSsoOptions _options;
    private readonly string _spaBaseUrl;
    private readonly ILogger<MicrosoftSsoController> _logger;

    public MicrosoftSsoController(
        IMicrosoftIdentityClient microsoft,
        IUserRepository users,
        SessionTokenIssuer sessions,
        MicrosoftLoginCodeStore loginCodes,
        IUnitOfWork unitOfWork,
        IDataProtectionProvider dataProtection,
        IOptions<MicrosoftSsoOptions> options,
        IOptions<AppOptions> appOptions,
        ILogger<MicrosoftSsoController> logger)
    {
        _microsoft = microsoft;
        _users = users;
        _sessions = sessions;
        _loginCodes = loginCodes;
        _unitOfWork = unitOfWork;
        _handshakeProtector = dataProtection.CreateProtector(HandshakeProtectorPurpose).ToTimeLimitedDataProtector();
        _options = options.Value;
        _spaBaseUrl = appOptions.Value.BaseUrl.TrimEnd('/');
        _logger = logger;
    }

    /// <summary>
    /// Sends the browser to Microsoft. <paramref name="state"/> is the web app's random value, handed back to it
    /// on the way home.
    /// </summary>
    [HttpGet("/api/auth/microsoft/login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult Start([FromQuery] string? state)
    {
        if (state is null || !SpaStateFormat.IsMatch(state))
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", "state must be 16 to 128 URL-safe characters."));
        }

        if (_spaBaseUrl.Length == 0)
        {
            _logger.LogError("Microsoft sign-in cannot return the browser to the web app: App:BaseUrl is not configured.");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponseFactory.Error(
                ApiErrorCodes.InternalError, "Microsoft sign-in is not configured.", "App:BaseUrl must be set."));
        }

        if (_options.ConfigurationProblem is { } problem)
        {
            _logger.LogWarning("Microsoft sign-in is switched off: {Problem}", problem);
            return RedirectToLogin("not_configured");
        }

        var handshake = new Handshake(RandomToken(), RandomToken(), RandomToken(), state);
        Response.Cookies.Append(HandshakeCookieName, _handshakeProtector.Protect(handshake.Serialize(), HandshakeLifetime), new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            // Lax, not Strict: the callback is a top-level navigation arriving from login.microsoftonline.com,
            // and Strict would withhold the cookie from it.
            SameSite = SameSiteMode.Lax,
            Path = HandshakeCookiePath,
            MaxAge = HandshakeLifetime,
            IsEssential = true,
        });

        return Redirect(_microsoft.BuildAuthorizationUrl(
            RedirectUri, handshake.State, handshake.Nonce, CodeChallengeFor(handshake.CodeVerifier)));
    }

    /// <summary>
    /// Where Microsoft sends the browser back. Every failure lands on the web app's login page with a reason
    /// code it can put into words.
    /// </summary>
    [HttpGet(CallbackPath)]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken cancellationToken)
    {
        var handshake = ReadHandshake();
        Response.Cookies.Delete(HandshakeCookieName, new CookieOptions { Path = HandshakeCookiePath });
        if (handshake is null)
        {
            return RedirectToLogin("expired");
        }

        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Microsoft sign-in returned {Error}: {Description}", error, errorDescription);
            return RedirectToLogin(error == "access_denied" ? "cancelled" : "provider");
        }

        if (string.IsNullOrEmpty(code) || !string.Equals(state, handshake.State, StringComparison.Ordinal))
        {
            return RedirectToLogin("invalid_state");
        }

        MicrosoftIdentity identity;
        try
        {
            identity = await _microsoft.ExchangeCodeAsync(
                code, RedirectUri, handshake.CodeVerifier, handshake.Nonce, cancellationToken);
        }
        catch (MicrosoftSsoException ex)
        {
            _logger.LogWarning(ex, "Microsoft sign-in could not be completed");
            return RedirectToLogin("provider");
        }

        var user = await FindUserAsync(identity, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning(
                "Microsoft sign-in by {Emails} (object id {ObjectId}) matches no account",
                string.Join(", ", identity.Emails), identity.ObjectId);
            return RedirectToLogin("no_account");
        }

        if (!user.IsActive)
        {
            return RedirectToLogin("disabled");
        }

        _logger.LogInformation("Microsoft sign-in verified for {Email}", user.Email);
        var loginCode = _loginCodes.Issue(user.Id);
        return Redirect(
            $"{_spaBaseUrl}/auth/sso/callback?code={Uri.EscapeDataString(loginCode)}&state={Uri.EscapeDataString(handshake.SpaState)}");
    }

    /// <summary>The web app trades the one-time code for a session. Single use, two-minute window.</summary>
    [HttpPost("/api/auth/microsoft/exchange")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<LoginTokenResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Exchange([FromBody] MicrosoftSsoExchangeRequest request, CancellationToken cancellationToken)
    {
        if (!_loginCodes.TryRedeem(request.Code, out var userId))
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("This Microsoft sign-in has expired. Please try again."));
        }

        var user = await _users.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("Account is disabled."));
        }

        // A temporary password is not forced on someone who did not sign in with one.
        var session = await _sessions.IssueAsync(user, mustChangePassword: false, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(session, "Login successful."));
    }

    /// <summary>The first account whose email matches one of the addresses Microsoft vouched for.</summary>
    private async Task<User?> FindUserAsync(MicrosoftIdentity identity, CancellationToken cancellationToken)
    {
        foreach (var email in identity.Emails)
        {
            if (await _users.GetByEmailAsync(email, cancellationToken) is { } user)
            {
                return user;
            }
        }

        return null;
    }

    private string RedirectUri => string.IsNullOrWhiteSpace(_options.RedirectUri)
        ? $"{Request.Scheme}://{Request.Host}{Request.PathBase}{CallbackPath}"
        : _options.RedirectUri.Trim();

    private IActionResult RedirectToLogin(string reason) => Redirect($"{_spaBaseUrl}/auth/login?ssoError={reason}");

    private Handshake? ReadHandshake()
    {
        if (!Request.Cookies.TryGetValue(HandshakeCookieName, out var protectedValue) || string.IsNullOrEmpty(protectedValue))
        {
            return null;
        }

        try
        {
            return Handshake.Deserialize(_handshakeProtector.Unprotect(protectedValue));
        }
        catch (CryptographicException)
        {
            // Expired, tampered with, or from before a key change: all mean "start again".
            return null;
        }
    }

    /// <summary>32 random bytes, URL-safe: 43 characters, which is also a valid PKCE verifier length.</summary>
    private static string RandomToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>PKCE S256: Microsoft binds the code to this; only the verifier kept in the cookie can redeem it.</summary>
    private static string CodeChallengeFor(string codeVerifier)
        => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    /// <summary>What the handshake cookie carries between the two Microsoft legs. Every part is URL-safe.</summary>
    private sealed record Handshake(string State, string Nonce, string CodeVerifier, string SpaState)
    {
        public string Serialize() => string.Join('|', State, Nonce, CodeVerifier, SpaState);

        public static Handshake? Deserialize(string value)
        {
            var parts = value.Split('|');
            return parts.Length == 4 ? new Handshake(parts[0], parts[1], parts[2], parts[3]) : null;
        }
    }
}
