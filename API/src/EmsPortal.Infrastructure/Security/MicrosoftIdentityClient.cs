using System.Security.Claims;
using System.Text.Json;
using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace EmsPortal.Infrastructure.Security;

/// <summary>
/// Talks to Microsoft Entra ID for the server-side authorization-code flow. The tenant's OpenID Connect
/// metadata (issuer + signing keys) is fetched once and cached by <see cref="ConfigurationManager{T}"/>, which
/// refreshes it on its own schedule; a token signed with a key it has not seen forces one early refresh, which
/// is how Microsoft's key rollovers are absorbed.
/// </summary>
internal sealed class MicrosoftIdentityClient : IMicrosoftIdentityClient
{
    /// <summary>Named HttpClient for the token endpoint (registered in DependencyInjection).</summary>
    public const string HttpClientName = "MicrosoftIdentity";

    // openid: an ID token; profile: the display name; email: the address claim, when the directory publishes one.
    private const string Scopes = "openid profile email";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MicrosoftSsoOptions _options;
    private readonly ILogger<MicrosoftIdentityClient> _logger;
    private readonly Lazy<ConfigurationManager<OpenIdConnectConfiguration>> _metadata;
    private readonly JsonWebTokenHandler _tokenHandler = new();

    public MicrosoftIdentityClient(
        IHttpClientFactory httpClientFactory,
        IOptions<MicrosoftSsoOptions> options,
        ILogger<MicrosoftIdentityClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        // Lazy: nothing should reach out to Microsoft merely because the API started, or when the keys are unset.
        _metadata = new Lazy<ConfigurationManager<OpenIdConnectConfiguration>>(() =>
            new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{_options.Authority}/v2.0/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = true }));
    }

    public string BuildAuthorizationUrl(string redirectUri, string state, string nonce, string codeChallenge)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new("client_id", _options.ClientId),
            new("response_type", "code"),
            new("redirect_uri", redirectUri),
            new("response_mode", "query"),
            new("scope", Scopes),
            new("state", state),
            new("nonce", nonce),
            new("code_challenge", codeChallenge),
            new("code_challenge_method", "S256"),
        };
        if (!string.IsNullOrWhiteSpace(_options.Prompt))
        {
            query.Add(new("prompt", _options.Prompt.Trim()));
        }

        var encoded = string.Join("&", query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return $"{_options.Authority}/oauth2/v2.0/authorize?{encoded}";
    }

    public async Task<MicrosoftIdentity> ExchangeCodeAsync(
        string code, string redirectUri, string codeVerifier, string expectedNonce, CancellationToken cancellationToken = default)
    {
        var idToken = await RedeemCodeAsync(code, redirectUri, codeVerifier, cancellationToken);
        var claims = await ValidateIdTokenAsync(idToken, cancellationToken);

        // The nonce ties this token to the attempt that asked for it (the API keeps it in the handshake cookie).
        if (!string.Equals(claims.FindFirst("nonce")?.Value, expectedNonce, StringComparison.Ordinal))
        {
            throw new MicrosoftSsoException("The Microsoft sign-in response does not belong to this sign-in attempt.");
        }

        var emails = new[] { "email", "preferred_username", "upn" }
            .Select(type => claims.FindFirst(type)?.Value?.Trim())
            .Where(value => !string.IsNullOrEmpty(value) && value.Contains('@'))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MicrosoftIdentity(
            claims.FindFirst("oid")?.Value ?? string.Empty,
            claims.FindFirst("tid")?.Value ?? string.Empty,
            claims.FindFirst("name")?.Value,
            emails);
    }

    private async Task<string> RedeemCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
            ["scope"] = Scopes,
        });

        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await http.PostAsync($"{_options.Authority}/oauth2/v2.0/token", form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = ParseJson(body);

        if (!response.IsSuccessStatusCode)
        {
            // {"error":"invalid_grant","error_description":"AADSTS…"}: the description goes to the log; the
            // caller only learns that it failed.
            var description = json is not null && json.RootElement.TryGetProperty("error_description", out var element)
                ? element.GetString()
                : body;
            _logger.LogWarning(
                "Microsoft token endpoint returned {StatusCode}: {Description}", (int)response.StatusCode, description);
            throw new MicrosoftSsoException("Microsoft did not accept the sign-in code.");
        }

        if (json is null
            || !json.RootElement.TryGetProperty("id_token", out var idToken)
            || idToken.GetString() is not { Length: > 0 } token)
        {
            throw new MicrosoftSsoException("Microsoft's token response carried no ID token.");
        }

        return token;
    }

    private static JsonDocument? ParseJson(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Signature, issuer, audience and lifetime, against the tenant's published keys.</summary>
    private async Task<ClaimsIdentity> ValidateIdTokenAsync(string idToken, CancellationToken cancellationToken)
    {
        var manager = _metadata.Value;
        var metadata = await manager.GetConfigurationAsync(cancellationToken);
        var parameters = new TokenValidationParameters
        {
            // Metadata fetched for one tenant reports that tenant's own issuer, so this pins the directory even
            // when TenantId was given as a domain name.
            ValidIssuer = metadata.Issuer,
            ValidAudience = _options.ClientId,
            IssuerSigningKeys = metadata.SigningKeys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        var result = await _tokenHandler.ValidateTokenAsync(idToken, parameters);
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            // Key rollover: signed with a key newer than the cached set.
            manager.RequestRefresh();
            metadata = await manager.GetConfigurationAsync(cancellationToken);
            parameters.ValidIssuer = metadata.Issuer;
            parameters.IssuerSigningKeys = metadata.SigningKeys;
            result = await _tokenHandler.ValidateTokenAsync(idToken, parameters);
        }

        if (!result.IsValid)
        {
            _logger.LogWarning(result.Exception, "Microsoft ID token failed validation");
            throw new MicrosoftSsoException("The Microsoft sign-in could not be verified.", result.Exception);
        }

        return result.ClaimsIdentity;
    }
}
