using EmsPortal.Api.Models.Maconomy;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// The tenant's connection to Deltek Maconomy, and the customer lookup it powers. The connection is
/// managed by Super Admins and Tenant Admins; the lookup is open to every signed-in user of the tenant.
/// A Super Admin may target another tenant with <c>?tenantId=</c>, as on the SMTP accounts.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/integrations/maconomy")]
[Produces("application/json")]
[Tags("Maconomy")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class MaconomyController : ControllerBase
{
    private const int SearchMaxLength = 100;

    private readonly IMaconomyConnectionService _connections;
    private readonly IMaconomyCustomerService _customers;
    private readonly IMaconomySessionManager _session;
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;

    public MaconomyController(
        IMaconomyConnectionService connections,
        IMaconomyCustomerService customers,
        IMaconomySessionManager session,
        ITenantRepository tenants,
        IUserRepository users)
    {
        _connections = connections;
        _customers = customers;
        _session = session;
        _tenants = tenants;
        _users = users;
    }

    // ---- The connection ----

    /// <summary>The tenant's connection with its secrets masked, or null data when none is configured yet.</summary>
    [HttpGet("connection")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiResponse<MaconomyConnectionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConnection([FromQuery] Guid? tenantId, CancellationToken cancellationToken)
    {
        var (tenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var connection = await _connections.GetAsync(tenant, cancellationToken);
        if (connection is null)
        {
            return Ok(ApiResponseFactory.Success<MaconomyConnectionResponse?>(null, "No Maconomy connection is configured for this tenant."));
        }
        return Ok(ApiResponseFactory.Success<MaconomyConnectionResponse?>(
            await ToResponseAsync(connection, cancellationToken), "Maconomy connection retrieved."));
    }

    /// <summary>
    /// Creates or updates the tenant's connection. A blank password keeps the stored one; any change to
    /// the URL, instance, user or password drops the token held, so the next call logs in afresh.
    /// </summary>
    [HttpPut("connection")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiResponse<MaconomyConnectionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveConnection(
        [FromQuery] Guid? tenantId, [FromBody] SaveMaconomyConnectionRequest body, CancellationToken cancellationToken)
    {
        var (tenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        try
        {
            var connection = await _connections.SaveAsync(
                tenant,
                new SaveMaconomyConnectionInput(
                    body.BaseUrl, body.InstanceCode, body.UserName, body.Password, body.ContainerId, body.DefaultLimit, body.IsEnabled),
                cancellationToken);
            return Ok(ApiResponseFactory.Success(await ToResponseAsync(connection, cancellationToken), "Maconomy connection saved."));
        }
        catch (MaconomyConnectionException ex)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", ex.Message));
        }
    }

    /// <summary>Removes the tenant's connection, credentials and token included.</summary>
    [HttpDelete("connection")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteConnection([FromQuery] Guid? tenantId, CancellationToken cancellationToken)
    {
        var (tenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        return await _connections.DeleteAsync(tenant, cancellationToken)
            ? Ok(ApiResponseFactory.Success(new { tenantId = tenant }, "Maconomy connection deleted."))
            : NotFound(ApiResponseFactory.NotFound("No Maconomy connection is configured for this tenant."));
    }

    /// <summary>
    /// Logs in to Maconomy afresh with the stored credentials and stores the reconnect token — the
    /// "Test connection". The customer lookup logs in by itself; this is for an admin to prove the
    /// credentials, and it answers 502 with what Maconomy said when they are refused.
    /// </summary>
    [HttpPost("connection/login")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiResponse<MaconomyLoginResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Login([FromQuery] Guid? tenantId, CancellationToken cancellationToken)
    {
        var (tenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        try
        {
            var result = await _connections.LoginAsync(tenant, cancellationToken);
            return Ok(ApiResponseFactory.Success(
                new MaconomyLoginResponse(true, result.IssuedOnUtc, result.ExpiresOnUtc), "Connected to Maconomy."));
        }
        catch (MaconomyException ex)
        {
            return MapMaconomyError(ex);
        }
    }

    /// <summary>Forgets the token held, so the next call logs in afresh. Useful after Maconomy revokes sessions.</summary>
    [HttpDelete("connection/token")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForgetToken([FromQuery] Guid? tenantId, CancellationToken cancellationToken)
    {
        var (tenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        return await _connections.ForgetTokenAsync(tenant, cancellationToken)
            ? Ok(ApiResponseFactory.Success(new { tenantId = tenant }, "Maconomy token forgotten."))
            : NotFound(ApiResponseFactory.NotFound("No Maconomy connection is configured for this tenant."));
    }

    // ---- Customers ----

    /// <summary>
    /// Customers whose number or name contains <paramref name="search"/>, as dropdown options: <c>text</c>
    /// is "number - name (specification 6 name)", <c>value</c> the number, <c>specification6Name</c> that
    /// field on its own. Empty below two characters. <paramref name="limit"/> defaults to the tenant's
    /// setting and is capped platform-wide. Logs in by itself when it has to.
    /// </summary>
    [HttpGet("customers")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MaconomyCustomerOptionResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SearchCustomers(
        [FromQuery] string? search, [FromQuery] int? limit, [FromQuery] Guid? tenantId, CancellationToken cancellationToken)
    {
        if (search is { Length: > SearchMaxLength })
        {
            return BadRequest(ApiResponseFactory.Error(
                ApiErrorCodes.ValidationFailed, "Validation failed.", $"search must be {SearchMaxLength} characters or fewer."));
        }
        if (limit is < 1)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", "limit must be 1 or more."));
        }

        var (tenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        try
        {
            var options = await _customers.SearchAsync(tenant, search, limit, cancellationToken);
            IReadOnlyList<MaconomyCustomerOptionResponse> data = options
                .Select(o => new MaconomyCustomerOptionResponse(o.Text, o.Value, o.Specification6Name))
                .ToList();
            return Ok(ApiResponseFactory.Success(data, $"{data.Count} customer(s) found."));
        }
        catch (MaconomyException ex)
        {
            return MapMaconomyError(ex);
        }
    }

    // ---- Helpers ----

    /// <summary>A Maconomy failure as the caller sees it: not set up is a conflict, everything else a bad gateway.</summary>
    private IActionResult MapMaconomyError(MaconomyException ex) => ex.Failure switch
    {
        MaconomyFailure.NotConfigured => Conflict(ApiResponseFactory.Error(
            ApiErrorCodes.MaconomyNotConfigured, "Maconomy is not connected.", ex.Message)),
        MaconomyFailure.AuthFailed or MaconomyFailure.TokenRejected => StatusCode(StatusCodes.Status502BadGateway, ApiResponseFactory.Error(
            ApiErrorCodes.MaconomyAuthFailed, "Maconomy refused the credentials.", ex.Message)),
        _ => StatusCode(StatusCodes.Status502BadGateway, ApiResponseFactory.Error(
            ApiErrorCodes.MaconomyUnavailable, "Maconomy could not be reached.", ex.Message)),
    };

    private async Task<MaconomyConnectionResponse> ToResponseAsync(MaconomyConnection c, CancellationToken cancellationToken)
    {
        var names = await _users.GetFullNamesAsync(
            new[] { c.CreatedById, c.UpdatedById }.Where(id => id.HasValue).Select(id => id!.Value), cancellationToken);

        var token = c.ReconnectTokenIssuedOnUtc is { } issued && !string.IsNullOrEmpty(c.EncryptedReconnectToken)
            ? new MaconomyTokenStatus(true, issued, _session.ExpiresOn(issued))
            : new MaconomyTokenStatus(false, null, null);

        return new MaconomyConnectionResponse(
            c.Id,
            c.TenantId,
            c.BaseUrl,
            c.InstanceCode,
            c.UserName,
            !string.IsNullOrEmpty(c.EncryptedPassword),
            c.ContainerId,
            c.DefaultLimit,
            c.IsEnabled,
            token,
            c.LastLoginError,
            c.LastLoginErrorUtc,
            c.CreatedById is { } cid && names.TryGetValue(cid, out var creator) ? creator : null,
            c.CreatedOnUtc,
            c.UpdatedById is { } uid && names.TryGetValue(uid, out var updater) ? updater : null,
            c.UpdatedOnUtc);
    }

    /// <summary>
    /// Resolves the tenant to operate on: a Super Admin may target any active tenant via the override;
    /// everyone else is pinned to their active tenant.
    /// </summary>
    private async Task<(Guid TenantId, IActionResult? Error)> ResolveTargetTenantAsync(Guid? requested, CancellationToken cancellationToken)
    {
        var active = User.GetActiveTenantId();
        if (User.IsSuperAdmin() && requested is { } target && target != active)
        {
            var tenant = await _tenants.GetByIdAsync(target, cancellationToken);
            if (tenant is null)
            {
                return (Guid.Empty, NotFound(ApiResponseFactory.Error(ApiErrorCodes.TenantNotFound, "Tenant not found.", target.ToString())));
            }
            if (tenant.Status != TenantStatus.Active)
            {
                return (Guid.Empty, BadRequest(ApiResponseFactory.Error(ApiErrorCodes.TenantInactive, "The tenant is not active.", target.ToString())));
            }
            return (target, null);
        }

        return active is { } a
            ? (a, null)
            : (Guid.Empty, StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller.")));
    }
}
