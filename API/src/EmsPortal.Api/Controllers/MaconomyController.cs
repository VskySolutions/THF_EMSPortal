using EmsPortal.Api.Models.Maconomy;
using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EmsPortal.Api.RateLimiting;

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
    private readonly IMaconomyCustomerSyncService _customerSync;
    private readonly IMaconomyJobSyncService _jobSync;
    private readonly IMaconomyClientDirectory _directory;
    private readonly IMaconomyNumberMaps _numbers;

    public MaconomyController(
        IMaconomyConnectionService connections,
        IMaconomyCustomerService customers,
        IMaconomySessionManager session,
        ITenantRepository tenants,
        IUserRepository users,
        IMaconomyCustomerSyncService customerSync,
        IMaconomyJobSyncService jobSync,
        IMaconomyClientDirectory directory,
        IMaconomyNumberMaps numbers)
    {
        _connections = connections;
        _customers = customers;
        _session = session;
        _tenants = tenants;
        _users = users;
        _customerSync = customerSync;
        _jobSync = jobSync;
        _directory = directory;
        _numbers = numbers;
    }

    // ---- The customer sync ----
    //
    // ALWAYS the caller's own tenant. Unlike the connection above, these take no ?tenantId= override: the
    // rows are REMS requests, which the caller only ever sees for the tenant they are signed in to, and a
    // Super Admin with the page scoped to another tenant was shown that tenant's requests here (user,
    // 22 Oct 2026).

    /// <summary>The Customer sync card: whether it is on, who last moved it and when, and the counts.</summary>
    [HttpGet("customer-sync")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiResponse<MaconomyCustomerSyncOverviewResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCustomerSync(CancellationToken cancellationToken)
    {
        var (tenant, error) = OwnTenant();
        if (error is not null)
        {
            return error;
        }
        return Ok(ApiResponseFactory.Success(await CustomerSyncOverviewAsync(tenant, cancellationToken), "Customer sync retrieved."));
    }

    /// <summary>Start posting approved new clients. Posts nothing by itself: what is already waiting is synced by hand.</summary>
    [HttpPost("customer-sync/start")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiResponse<MaconomyCustomerSyncOverviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> StartCustomerSync(CancellationToken cancellationToken)
        => SetCustomerSyncAsync(enabled: true, cancellationToken);

    /// <summary>Stop posting. A job already running finishes; requests approved from now on are recorded as Not synced.</summary>
    [HttpPost("customer-sync/stop")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiResponse<MaconomyCustomerSyncOverviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> StopCustomerSync(CancellationToken cancellationToken)
        => SetCustomerSyncAsync(enabled: false, cancellationToken);

    /// <summary>The requests an admin may want to Sync now: failed, not synced, and attempts the portal lost track of.</summary>
    [HttpGet("customer-sync/unsynced")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MaconomyUnsyncedRow>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListUnsynced(CancellationToken cancellationToken)
    {
        var (tenant, error) = OwnTenant();
        if (error is not null)
        {
            return error;
        }

        // The sync state is three columns on the client now; the request is only the one that made it
        // postable, where that is still known. Attempts are no longer counted, and the last attempt is the
        // moment the client's state last moved while one was pending, running or failed.
        IReadOnlyList<MaconomyUnsyncedRow> rows = (await _customerSync.ListUnsyncedAsync(tenant, cancellationToken))
            .Select(row =>
            {
                var client = row.Client;
                var stale = _customerSync.IsStale(client);
                var lastAttempt = client.MaconomySyncStatus is RemsMaconomyCustomerSyncStatus.Pending
                    or RemsMaconomyCustomerSyncStatus.Syncing or RemsMaconomyCustomerSyncStatus.Failed
                    ? client.UpdatedOnUtc
                    : client.MaconomySyncedOnUtc;
                return new MaconomyUnsyncedRow(
                    client.Id, row.Request?.Id ?? Guid.Empty, row.Request?.RemsNumber ?? string.Empty,
                    client.ClientName, client.ClientEmail,
                    RemsMaconomySyncViews.StatusOf(client, stale), stale ? RemsMaconomySyncViews.StaleReason : client.MaconomySyncError,
                    0, lastAttempt, client.UpdatedOnUtc);
            })
            .ToList();
        return Ok(ApiResponseFactory.Success(rows, $"{rows.Count} request(s) waiting."));
    }

    /// <summary>Sync now from the settings page: one attempt, inline, in the caller's own tenant.</summary>
    [HttpPost("customer-sync/{remsId:guid}/run")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiResponse<RemsMaconomySyncView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RunCustomerSync(Guid remsId, CancellationToken cancellationToken)
    {
        var (tenant, error) = OwnTenant();
        if (error is not null)
        {
            return error;
        }
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        try
        {
            var client = await _customerSync.SyncNowAsync(remsId, tenant, me, cancellationToken);
            var view = RemsMaconomySyncViews.From(client, _customerSync.IsStale(client), enabled: true, canManage: true);
            return Ok(ApiResponseFactory.Success(view, RemsMaconomySyncController.MessageFor(view)));
        }
        catch (MaconomyCustomerSyncException ex)
        {
            return RemsMaconomySyncController.RefusalFor(this, ex);
        }
    }

    /// <summary>The tenant the caller is signed in to, or the 403 that says they have none.</summary>
    private (Guid TenantId, IActionResult? Error) OwnTenant()
        => User.GetActiveTenantId() is { } tenant
            ? (tenant, null)
            : (Guid.Empty, StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller.")));

    private async Task<IActionResult> SetCustomerSyncAsync(bool enabled, CancellationToken cancellationToken)
    {
        var (tenant, error) = OwnTenant();
        if (error is not null)
        {
            return error;
        }
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        try
        {
            await _customerSync.SetEnabledAsync(tenant, enabled, me, cancellationToken);
        }
        catch (MaconomyCustomerSyncException ex)
        {
            return RemsMaconomySyncController.RefusalFor(this, ex);
        }
        return Ok(ApiResponseFactory.Success(
            await CustomerSyncOverviewAsync(tenant, cancellationToken),
            enabled
                ? "Customer sync started. Approvals from now on post their new client; anything already waiting is synced by hand."
                : "Customer sync stopped."));
    }

    private async Task<MaconomyCustomerSyncOverviewResponse> CustomerSyncOverviewAsync(Guid tenant, CancellationToken cancellationToken)
    {
        var overview = await _customerSync.GetOverviewAsync(tenant, cancellationToken);
        string? changedBy = null;
        if (overview.ChangedById is { } changedById)
        {
            var names = await _users.GetFullNamesAsync(new[] { changedById }, cancellationToken);
            names.TryGetValue(changedById, out changedBy);
        }
        return new MaconomyCustomerSyncOverviewResponse(
            overview.Connected, overview.Enabled, overview.ChangedOnUtc, changedBy,
            overview.Synced, overview.Linked, overview.Failed, overview.NotSynced, overview.Pending);
    }

    // ---- The job sync ----
    //
    // Also ALWAYS the caller's own tenant, for the same reason as the customer sync above: the rows are
    // REMS requests. Sync job now is pressed from the lists and from here; nothing posts a job by itself.

    /// <summary>The Job tab: whether it is on, who last moved it, the settings, the counts and the template map.</summary>
    [HttpGet("job-sync")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiResponse<MaconomyJobSyncOverviewResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetJobSync(CancellationToken cancellationToken)
    {
        var (tenant, error) = OwnTenant();
        if (error is not null)
        {
            return error;
        }
        return Ok(ApiResponseFactory.Success(await JobSyncOverviewAsync(tenant, cancellationToken), "Job sync retrieved."));
    }

    /// <summary>Offer Sync job now on approved requests. Posts nothing by itself.</summary>
    [HttpPost("job-sync/start")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiResponse<MaconomyJobSyncOverviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> StartJobSync(CancellationToken cancellationToken)
        => SetJobSyncAsync(enabled: true, cancellationToken);

    /// <summary>Hide Sync job now everywhere. An attempt already under way finishes.</summary>
    [HttpPost("job-sync/stop")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiResponse<MaconomyJobSyncOverviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> StopJobSync(CancellationToken cancellationToken)
        => SetJobSyncAsync(enabled: false, cancellationToken);

    /// <summary>The Job tab's table: every approved request and where its job stands, newest change first.</summary>
    [HttpGet("job-sync/requests")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MaconomyJobSyncRow>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListJobSyncRequests(CancellationToken cancellationToken)
    {
        var (tenant, error) = OwnTenant();
        if (error is not null)
        {
            return error;
        }

        var context = await _jobSync.GetContextAsync(tenant, cancellationToken);
        var requests = await _jobSync.ListApprovedAsync(cancellationToken);
        var names = await _users.GetFullNamesAsync(
            requests.Where(r => r.MaconomyJobSyncedById.HasValue).Select(r => r.MaconomyJobSyncedById!.Value), cancellationToken);
        var rows = new List<MaconomyJobSyncRow>(requests.Count);
        foreach (var r in requests)
        {
            // A Maconomy-picked client's name is Maconomy's to give.
            await _directory.HydrateAsync(r.Client, cancellationToken);
            var syncedBy = r.MaconomyJobSyncedById is { } by && names.TryGetValue(by, out var n) ? n : null;
            rows.Add(new MaconomyJobSyncRow(
                r.Id, r.RemsNumber, r.Client?.ClientName ?? string.Empty, r.Client?.MaconomyCustomerNumber,
                r.ServiceLine?.Label, r.JobTemplate?.Label, r.UpdatedOnUtc,
                RemsMaconomyJobSyncViews.For(r, context, _jobSync.IsStale(r), canManage: true, syncedBy)));
        }
        return Ok(ApiResponseFactory.Success<IReadOnlyList<MaconomyJobSyncRow>>(rows, $"{rows.Count} approved request(s)."));
    }

    /// <summary>Sync job now from the settings page: one attempt, inline, in the caller's own tenant.</summary>
    [HttpPost("job-sync/{remsId:guid}/run")]
    [RequirePermission(Permissions.IntegrationsMaconomyManage)]
    [ProducesResponseType<ApiResponse<RemsMaconomyJobSyncView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RunJobSync(Guid remsId, CancellationToken cancellationToken)
    {
        var (tenant, error) = OwnTenant();
        if (error is not null)
        {
            return error;
        }
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        try
        {
            var request = await _jobSync.SyncNowAsync(remsId, tenant, me, cancellationToken);
            var context = await _jobSync.GetContextAsync(tenant, cancellationToken);
            var names = await _users.GetFullNamesAsync(new[] { me }, cancellationToken);
            var view = RemsMaconomyJobSyncViews.For(
                request, context, _jobSync.IsStale(request), canManage: true, names.TryGetValue(me, out var name) ? name : null);
            return Ok(ApiResponseFactory.Success(view, RemsMaconomyJobSyncViews.MessageFor(view)));
        }
        catch (MaconomyJobSyncException ex)
        {
            return RemsMaconomyJobSyncController.RefusalFor(this, ex);
        }
    }

    private async Task<IActionResult> SetJobSyncAsync(bool enabled, CancellationToken cancellationToken)
    {
        var (tenant, error) = OwnTenant();
        if (error is not null)
        {
            return error;
        }
        if (User.GetUserId() is not { } me)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        try
        {
            await _jobSync.SetEnabledAsync(tenant, enabled, me, cancellationToken);
        }
        catch (MaconomyJobSyncException ex)
        {
            return RemsMaconomyJobSyncController.RefusalFor(this, ex);
        }
        return Ok(ApiResponseFactory.Success(
            await JobSyncOverviewAsync(tenant, cancellationToken),
            enabled ? "Job sync started. Sync job now is offered on approved requests." : "Job sync stopped. Sync job now is hidden."));
    }

    private async Task<MaconomyJobSyncOverviewResponse> JobSyncOverviewAsync(Guid tenant, CancellationToken cancellationToken)
    {
        var overview = await _jobSync.GetOverviewAsync(tenant, cancellationToken);
        string? changedBy = null;
        if (overview.ChangedById is { } changedById)
        {
            var names = await _users.GetFullNamesAsync(new[] { changedById }, cancellationToken);
            names.TryGetValue(changedById, out changedBy);
        }

        // Every value of the tenant's REMS Job Template list with the Maconomy number it carries, so the
        // tab can say how many still lack one. The numbers are edited under Option Sets.
        var numbers = await _numbers.GetAsync(tenant, cancellationToken);
        var templates = numbers.JobTemplates
            .Select(t => new MaconomyJobTemplateRow(t.ItemId, t.Code, t.Label, t.Number))
            .ToList();

        return new MaconomyJobSyncOverviewResponse(
            overview.Connected, overview.Enabled, overview.ChangedOnUtc, changedBy,
            overview.ProjectManagerNumber, overview.DefaultTemplateNumber,
            overview.Synced, overview.Failed, overview.Syncing, overview.Waiting, templates);
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
    /// Customers whose number, name, email or telephone contains <paramref name="search"/>, as dropdown options: <c>text</c>
    /// is "number - name (specification 6 name)", <c>value</c> the number, <c>specification6Name</c> that
    /// name on its own, <c>entityType</c> the REMS entity type code it stands for, <c>emailAddress</c> and
    /// <c>phoneNumber</c> the customer's own. Empty below two characters. <paramref name="limit"/> defaults
    /// to the tenant's setting and is capped platform-wide. Logs in by itself when it has to.
    /// </summary>
    [HttpGet("customers")]
    [EnableRateLimiting(RateLimitPolicies.MaconomySearch)]
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
            var found = await _customers.SearchAsync(tenant, search, limit, cancellationToken);
            IReadOnlyList<MaconomyCustomerOptionResponse> data = found.Customers
                .Select(o => new MaconomyCustomerOptionResponse(
                    o.Text, o.Value, o.Specification6Name, o.EntityType, o.EmailAddress, o.PhoneNumber))
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
