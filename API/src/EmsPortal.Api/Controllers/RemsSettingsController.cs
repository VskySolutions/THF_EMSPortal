using EmsPortal.Api.Models.Rems;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.OptionSets;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>Per-tenant REMS engagement settings (WO-114).</summary>
[ApiController]
[Route("api/rems/settings")]
[Produces("application/json")]
[Tags("REMS Settings")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class RemsSettingsController : ControllerBase
{
    private readonly IRemsSettingsRepository _settings;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOptionCodeResolver _codes;

    public RemsSettingsController(
        IRemsSettingsRepository settings,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IOptionCodeResolver codes)
    {
        _settings = settings;
        _users = users;
        _unitOfWork = unitOfWork;
        _codes = codes;
    }

    /// <summary>The tenant's REMS settings: the managing shareholder and the department-director map (WO-114).</summary>
    [HttpGet]
    [RequirePermission(Permissions.RemsSettingsManage)]
    [ProducesResponseType<ApiResponse<RemsSettingsView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var settings = await _settings.GetAsync(cancellationToken);
        var view = await BuildViewAsync(settings, cancellationToken);
        return Ok(ApiResponseFactory.Success(view, "REMS settings retrieved."));
    }

    /// <summary>Fully replace the department-director map (WO-114).</summary>
    [HttpPut]
    [RequirePermission(Permissions.RemsSettingsManage)]
    [ProducesResponseType<ApiResponse<RemsSettingsView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update([FromBody] UpdateRemsSettingsRequest request, CancellationToken cancellationToken)
    {
        if (User.GetActiveTenantId() is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant context."));
        }

        // Every referenced user must resolve to a real user.
        foreach (var director in request.DepartmentDirectors)
        {
            if (await _users.GetByIdAsync(director.DirectorUserId, cancellationToken) is null)
            {
                return BadRequest(ApiResponseFactory.Error(
                    ApiErrorCodes.ValidationFailed, "Validation failed.", $"Unknown directorUserId for department '{director.Department}'."));
            }
        }

        var settings = await _settings.GetAsync(cancellationToken);
        if (settings is null)
        {
            settings = new RemsSettings { Id = Guid.NewGuid() };
            await _settings.AddAsync(settings, cancellationToken);
        }

        // Deliberately no Update() on the settings row itself.

        await ReconcileDepartmentDirectorsAsync(settings, request.DepartmentDirectors, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _settings.GetAsync(cancellationToken);
        var view = await BuildViewAsync(refreshed, cancellationToken);
        return Ok(ApiResponseFactory.Success(view, "REMS settings updated."));
    }

    // -------------------- Helpers --------------------

    /// <summary>
    /// Upserts the supplied department-director rows by (normalized) department and removes any not
    /// present.
    /// </summary>
    private async Task ReconcileDepartmentDirectorsAsync(
        RemsSettings settings, IReadOnlyList<RemsDepartmentDirectorInput> desired, CancellationToken cancellationToken)
    {
        var existing = settings.DepartmentDirectors.Where(d => !d.Deleted).ToList();
        var desiredByDept = desired.ToDictionary(d => Normalize(d.Department), d => d.DirectorUserId);

        // The mapping keys off the department ITEM now, so each wanted code is resolved once up front. A
        // code the tenant's list does not have is dropped rather than stored: there is nothing to map.
        var idsByCode = await _codes.IdsByCodeAsync(
            EmsPortal.Domain.Enums.EntityType.Rems, RemsOptionSetKeys.Department, cancellationToken);

        // Remove mappings that are no longer wanted.
        foreach (var row in existing)
        {
            if (!desiredByDept.ContainsKey(Normalize(row.Department!.Value)))
            {
                _settings.RemoveDepartmentDirector(row);
            }
        }

        // Upsert wanted mappings.
        foreach (var (department, directorId) in desiredByDept)
        {
            var row = existing.FirstOrDefault(d => Normalize(d.Department!.Value) == department);
            if (row is null)
            {
                if (!idsByCode.TryGetValue(department, out var departmentId))
                {
                    continue;
                }

                await _settings.AddDepartmentDirectorAsync(new RemsDepartmentDirector
                {
                    Id = Guid.NewGuid(),
                    RemsSettingsId = settings.Id,
                    DepartmentId = departmentId,
                    DirectorUserId = directorId,
                }, cancellationToken);
            }
            else if (row.DirectorUserId != directorId)
            {
                row.DirectorUserId = directorId;
                _settings.UpdateDepartmentDirector(row);
            }
        }
    }

    private async Task<RemsSettingsView> BuildViewAsync(RemsSettings? settings, CancellationToken cancellationToken)
    {
        if (settings is null)
        {
            return new RemsSettingsView(Array.Empty<RemsDepartmentDirectorView>());
        }

        var userIds = settings.DepartmentDirectors.Where(d => !d.Deleted).Select(d => d.DirectorUserId).ToList();

        var names = await _users.GetFullNamesAsync(userIds, cancellationToken);

        var directors = settings.DepartmentDirectors
            .Where(d => !d.Deleted)
            .OrderBy(d => d.Department!.Value)
            .Select(d => new RemsDepartmentDirectorView(
                d.Department!.Value,
                new RemsUserRef(d.DirectorUserId, names.TryGetValue(d.DirectorUserId, out var dn) ? dn : string.Empty)))
            .ToList();

        return new RemsSettingsView(directors);
    }

    private static string Normalize(string department) => department.Trim().ToLowerInvariant();
}
