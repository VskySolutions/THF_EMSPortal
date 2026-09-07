using System.Text.Json;
using EmsPortal.Api.Dashboard;
using EmsPortal.Api.Models.Dashboard;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>Read-only dashboard aggregations (WO-72).</summary>
[ApiController]
[Authorize]
[Route("/api/dashboard")]
[Produces("application/json")]
[Tags("Dashboard")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class DashboardController : ControllerBase
{
    private readonly IDashboardQueryService _query;
    private readonly IDashboardCacheService _cache;
    private readonly IDashboardLayoutRepository _layouts;
    private readonly IUnitOfWork _unitOfWork;

    public DashboardController(
        IDashboardQueryService query,
        IDashboardCacheService cache,
        IDashboardLayoutRepository layouts,
        IUnitOfWork unitOfWork)
    {
        _query = query;
        _cache = cache;
        _layouts = layouts;
        _unitOfWork = unitOfWork;
    }

    // ---- Users ----

    [HttpGet("users")]
    [RequirePermission(Permissions.UsersRead)]
    [ProducesResponseType<ApiResponse<UserDashboardDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Users([FromQuery] string dateRange = "7d", [FromQuery] Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var data = await _query.GetUsersAsync(ResolveScope(tenantId), dateRange, cancellationToken);
        return Ok(ApiResponseFactory.Success(data, "User dashboard retrieved."));
    }

    // ---- Platform (Super Admin only) ----

    [HttpGet("platform")]
    [ProducesResponseType<ApiResponse<PlatformDashboardDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Platform([FromQuery] string dateRange = "7d", CancellationToken cancellationToken = default)
    {
        if (!User.IsSuperAdmin())
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("Platform dashboard is restricted to Super Admins."));
        }

        var forceRefresh = TruthyHeader(Request.Headers["X-Dashboard-Force-Refresh"]);
        var data = await _cache.GetOrAddAsync(
            $"dashboard:platform:{dateRange}",
            forceRefresh,
            () => _query.GetPlatformAsync(dateRange, forceRefresh, cancellationToken));

        return Ok(ApiResponseFactory.Success(data, "Platform dashboard retrieved."));
    }

    // ---- Layout ----

    [HttpGet("layout")]
    [ProducesResponseType<ApiResponse<DashboardLayoutResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLayout(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId is not { } uid)
        {
            return StatusCode(StatusCodes.Status401Unauthorized, ApiResponseFactory.Unauthorized("No user in token."));
        }

        var saved = await _layouts.GetByUserAsync(uid, cancellationToken);
        if (saved is not null)
        {
            return Ok(ApiResponseFactory.Success(
                new DashboardLayoutResponse(saved.WidgetOrder, saved.HiddenWidgets, saved.CollapsedWidgets),
                "Layout retrieved."));
        }

        var role = ResolveDashboardRole();
        return Ok(ApiResponseFactory.Success(
            new DashboardLayoutResponse(
                DashboardDefaultLayouts.For(role),
                DashboardDefaultLayouts.DefaultHiddenFor(role),
                Array.Empty<string>()),
            "Default layout retrieved."));
    }

    [HttpPut("layout")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveLayout([FromBody] DashboardLayoutRequest body, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId is not { } uid)
        {
            return StatusCode(StatusCodes.Status401Unauthorized, ApiResponseFactory.Unauthorized("No user in token."));
        }

        var layout = new DashboardLayout
        {
            Id = Guid.NewGuid(),
            UserId = uid,
            WidgetOrderJson = JsonSerializer.Serialize(body.WidgetOrder ?? new List<string>()),
            HiddenWidgetsJson = JsonSerializer.Serialize(body.HiddenWidgets ?? new List<string>()),
            CollapsedWidgetsJson = JsonSerializer.Serialize(body.CollapsedWidgets ?? new List<string>()),
        };
        await _layouts.UpsertAsync(layout, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(new { saved = true }, "Layout saved."));
    }

    // ---- Helpers ----

    /// <summary>
    /// Super Admins may target any tenant explicitly; with none requested they get the tenant they are
    /// currently viewing (the claim follows the Super-Admin tenant scope).
    /// </summary>
    private Guid? ResolveScope(Guid? requestedTenantId)
        => (User.IsSuperAdmin() ? requestedTenantId : null) ?? User.GetActiveTenantId();

    /// <summary>
    /// Resolves the layout tier: Super Admin, else Tenant Admin (users.read + tenants.read), else
    /// Common.
    /// </summary>
    private DashboardRole ResolveDashboardRole()
    {
        if (User.IsSuperAdmin())
        {
            return DashboardRole.SuperAdmin;
        }
        if (User.HasPermission(Permissions.UsersRead) && User.HasPermission(Permissions.TenantsRead))
        {
            return DashboardRole.TenantAdmin;
        }
        return DashboardRole.Common;
    }

    private static bool TruthyHeader(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value, "0", StringComparison.Ordinal);
}
