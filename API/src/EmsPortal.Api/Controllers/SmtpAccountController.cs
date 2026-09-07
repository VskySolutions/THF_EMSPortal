using EmsPortal.Api.Models.SmtpAccounts;
using EmsPortal.Api.Security;
using EmsPortal.Api.Validators.SmtpAccounts;
using EmsPortal.Application.Abstractions.Email;
using EmsPortal.Application.Common;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>Manage a tenant's SMTP email accounts.</summary>
[ApiController]
[Authorize]
[Route("/api/admin/smtp-accounts")]
[Produces("application/json")]
[Tags("SMTP Email Accounts")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class SmtpAccountController : ControllerBase
{
    private readonly ISmtpAccountService _service;
    private readonly ISmtpAccountRepository _accounts;
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;

    public SmtpAccountController(
        ISmtpAccountService service,
        ISmtpAccountRepository accounts,
        ITenantRepository tenants,
        IUserRepository users)
    {
        _service = service;
        _accounts = accounts;
        _tenants = tenants;
        _users = users;
    }

    // ---- List ----

    [HttpGet]
    [RequirePermission(Permissions.UsersRead)]
    [ProducesResponseType<ApiResponse<IEnumerable<SmtpAccountSummaryResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? status,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var (resolvedTenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        bool? isActive = status?.Trim().ToLowerInvariant() switch
        {
            "active" => true,
            "inactive" => false,
            _ => null,
        };

        var accounts = await _accounts.ListByTenantAsync(resolvedTenant, isActive, cancellationToken);
        var summaries = await ToSummariesAsync(accounts, cancellationToken);

        // Only when a column was actually named.
        var ordered = ListSorts.Knows(sortBy) ? ListSorts.Apply(summaries, sortBy, descending) : summaries;
        return Ok(ApiResponseFactory.Success(ordered, "SMTP accounts retrieved."));
    }

    /// <summary>What the Email Accounts list may be ordered by, once a reader asks for an order at all.</summary>
    private static readonly SortMap<SmtpAccountSummaryResponse> ListSorts =
        new SortMap<SmtpAccountSummaryResponse>("updatedOnUtc")
            .Add("accountName", a => a.AccountName)
            .Add("host", a => a.Host, a => a.AccountName)
            .Add("port", a => a.Port, a => a.AccountName)
            .Add("fromEmail", a => a.FromEmail, a => a.AccountName)
            .Add("encryptionType", a => a.EncryptionType, a => a.AccountName)
            .Add("status", a => a.IsActive, a => a.AccountName)
            .Add("createdOnUtc", a => a.CreatedOnUtc)
            .Add("updatedOnUtc", a => a.UpdatedOnUtc);

    // ---- Detail ----

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.UsersRead)]
    [ProducesResponseType<ApiResponse<SmtpAccountSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, [FromQuery] Guid? tenantId, CancellationToken cancellationToken)
    {
        var (resolvedTenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var account = await _accounts.GetByIdAsync(id, resolvedTenant, cancellationToken);
        if (account is null)
        {
            return NotFound(ApiResponseFactory.NotFound("SMTP account not found."));
        }

        var summary = (await ToSummariesAsync(new[] { account }, cancellationToken)).Single();
        return Ok(ApiResponseFactory.Success(summary, "SMTP account retrieved."));
    }

    // ---- Create ----

    [HttpPost]
    [RequirePermission(Permissions.EmailManage)]
    [ProducesResponseType<ApiResponse<SmtpAccountSummaryResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateSmtpAccountRequest body, CancellationToken cancellationToken)
    {
        var (resolvedTenant, error) = await ResolveTargetTenantAsync(body.TenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var input = new CreateSmtpAccountInput(
            resolvedTenant,
            body.AccountName,
            body.Host,
            body.Port,
            SmtpEnum.Parse<SmtpEncryptionType>(body.EncryptionType),
            SmtpEnum.Parse<SmtpAuthType>(body.AuthType),
            body.Username,
            body.Password,
            body.FromName,
            body.FromEmail);

        try
        {
            var account = await _service.CreateAsync(input, cancellationToken);
            var summary = (await ToSummariesAsync(new[] { account }, cancellationToken)).Single();
            return CreatedAtAction(nameof(Get), new { id = account.Id },
                ApiResponseFactory.Success(summary, "SMTP account created."));
        }
        catch (SmtpAccountException ex)
        {
            return MapServiceError(ex);
        }
    }

    // ---- Update ----

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.EmailManage)]
    [ProducesResponseType<ApiResponse<SmtpAccountSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromQuery] Guid? tenantId, [FromBody] UpdateSmtpAccountRequest body, CancellationToken cancellationToken)
    {
        var (resolvedTenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var input = new UpdateSmtpAccountInput(
            body.AccountName,
            body.Host,
            body.Port,
            SmtpEnum.Parse<SmtpEncryptionType>(body.EncryptionType),
            SmtpEnum.Parse<SmtpAuthType>(body.AuthType),
            body.Username,
            body.Password,
            body.FromName,
            body.FromEmail);

        try
        {
            var account = await _service.UpdateAsync(id, resolvedTenant, input, cancellationToken);
            if (account is null)
            {
                return NotFound(ApiResponseFactory.NotFound("SMTP account not found."));
            }

            var summary = (await ToSummariesAsync(new[] { account }, cancellationToken)).Single();
            return Ok(ApiResponseFactory.Success(summary, "SMTP account updated."));
        }
        catch (SmtpAccountException ex)
        {
            return MapServiceError(ex);
        }
    }

    // ---- Delete ----

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.EmailManage)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid? tenantId, CancellationToken cancellationToken)
    {
        var (resolvedTenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        try
        {
            var deleted = await _service.DeleteAsync(id, resolvedTenant, cancellationToken);
            if (deleted is null)
            {
                return NotFound(ApiResponseFactory.NotFound("SMTP account not found."));
            }

            return Ok(ApiResponseFactory.Success(new { id }, "SMTP account deleted."));
        }
        catch (SmtpAccountException ex)
        {
            return MapServiceError(ex);
        }
    }

    // ---- Set active ----

    [HttpPut("{id:guid}/activate")]
    [RequirePermission(Permissions.EmailManage)]
    [ProducesResponseType<ApiResponse<SmtpActivationResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(Guid id, [FromQuery] Guid? tenantId, CancellationToken cancellationToken)
    {
        var (resolvedTenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var result = await _service.ActivateAsync(id, resolvedTenant, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiResponseFactory.NotFound("SMTP account not found."));
        }

        return Ok(ApiResponseFactory.Success(
            new SmtpActivationResponse(result.ActivatedId, result.DeactivatedId), "SMTP account activated."));
    }

    // ---- Test send ----

    [HttpPost("{id:guid}/test")]
    [RequirePermission(Permissions.EmailManage)]
    [ProducesResponseType<ApiResponse<SmtpTestResultResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Test(Guid id, [FromQuery] Guid? tenantId, [FromBody] TestSmtpRequest body, CancellationToken cancellationToken)
    {
        var (resolvedTenant, error) = await ResolveTargetTenantAsync(tenantId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var result = await _service.TestSendAsync(id, resolvedTenant, body.RecipientEmail, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiResponseFactory.NotFound("SMTP account not found."));
        }

        var response = new SmtpTestResultResponse(
            result.Success,
            result.SentAtUtc,
            result.ServerResponse,
            result.ErrorCategory?.ToString(),
            result.ErrorDetail);
        return Ok(ApiResponseFactory.Success(response,
            result.Success ? "Test email sent." : "Test email failed."));
    }

    // ---- Helpers ----

    /// <summary>Maps a service business-rule violation to the matching error envelope.</summary>
    private IActionResult MapServiceError(SmtpAccountException ex) => ex.Code switch
    {
        SmtpAccountErrorCodes.DuplicateName => BadRequest(ApiResponseFactory.Error(ApiErrorCodes.DuplicateIdentifier, "Validation failed.", ex.Message)),
        SmtpAccountErrorCodes.ActiveAccountDelete => BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", ex.Message)),
        _ => BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Validation failed.", ex.Message)),
    };

    /// <summary>Projects accounts to summaries, resolving the audit actor ids to display names.</summary>
    private async Task<IReadOnlyList<SmtpAccountSummaryResponse>> ToSummariesAsync(IReadOnlyList<SmtpAccount> accounts, CancellationToken cancellationToken)
    {
        var creatorNames = await _users.GetFullNamesAsync(
            accounts.SelectMany(a => new[] { a.CreatedById, a.UpdatedById })
                .Where(id => id.HasValue).Select(id => id!.Value),
            cancellationToken);

        return accounts.Select(a => new SmtpAccountSummaryResponse(
            a.Id,
            a.AccountName,
            a.Host,
            a.Port,
            a.FromName,
            a.FromEmail,
            a.EncryptionType.ToString(),
            a.AuthType.ToString(),
            a.Username,
            a.IsActive,
            a.CreatedById is { } cid && creatorNames.TryGetValue(cid, out var name) ? name : null,
            a.CreatedOnUtc,
            a.UpdatedById is { } uid && creatorNames.TryGetValue(uid, out var updater) ? updater : null,
            a.UpdatedOnUtc)).ToList();
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
