using EmsPortal.Api.Models.UniversalFeatures;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Infrastructure.Persistence.ModifiedLog;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>Modified Log — field-level change history.</summary>
[ApiController]
[Authorize]
[Produces("application/json")]
[Tags("Universal Features — Modified Log")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class ModifiedLogController : ControllerBase
{
    private readonly IModifiedLogRepository _log;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;

    public ModifiedLogController(IModifiedLogRepository log, IUserRepository users, IUnitOfWork unitOfWork)
    {
        _log = log;
        _users = users;
        _unitOfWork = unitOfWork;
    }

    [HttpGet("/api/uf/modified-log")]
    [ProducesResponseType<ApiResponse<IEnumerable<ModifiedLogEntryResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> History(
        [FromQuery] EntityType entityType,
        [FromQuery] Guid entityId,
        [FromQuery] string fieldName,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!User.CanAccess(entityType))
        {
            return Forbid();
        }

        var (items, total) = await _log.ListAsync(entityType, entityId, fieldName ?? string.Empty, page, limit, cancellationToken);
        var names = await _users.GetFullNamesAsync(items.Where(l => l.ChangedById.HasValue).Select(l => l.ChangedById!.Value), cancellationToken);
        var data = items.Select(l => new ModifiedLogEntryResponse(
            l.Id, l.FieldName, l.OldValue, l.NewValue, l.ChangedById,
            l.ChangedById is { } id && names.TryGetValue(id, out var name) ? name : "System",
            l.ChangedOnUtc));
        return Ok(ApiResponseFactory.Paginated(data, "Change history retrieved.", page, limit, total));
    }

    [HttpGet("/api/uf/modified-log/icon-counts")]
    [ProducesResponseType<ApiResponse<IReadOnlyDictionary<string, int>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> IconCounts([FromQuery] EntityType entityType, [FromQuery] Guid entityId, CancellationToken cancellationToken)
    {
        if (!User.CanAccess(entityType))
        {
            return Forbid();
        }

        var counts = await _log.GetIconCountsAsync(entityType, entityId, cancellationToken);
        return Ok(ApiResponseFactory.Success(counts, "Icon counts retrieved."));
    }

    [HttpGet("/api/admin/modified-log-config")]
    [RequirePermission(Permissions.SettingsManage)]
    [ProducesResponseType<ApiResponse<IEnumerable<ModifiedLogConfigResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListConfig([FromQuery] EntityType? entityType = null, CancellationToken cancellationToken = default)
    {
        var configs = (await _log.GetConfigsAsync(cancellationToken))
            .ToDictionary(c => (c.EntityType, c.FieldName));

        var descriptors = entityType is { } et ? TrackedFieldRegistry.ForEntityType(et) : TrackedFieldRegistry.All;
        var data = descriptors.Select(d =>
        {
            // System fields are always enabled; optional fields default to enabled unless a row disables them.
            var enabled = d.IsSystemTracked
                || !configs.TryGetValue((d.EntityType, d.PropertyName), out var config)
                || config.IsEnabled;
            return new ModifiedLogConfigResponse(d.Key, d.EntityType, d.PropertyName, d.DisplayName, enabled, d.IsSystemTracked);
        });

        return Ok(ApiResponseFactory.Success(data, "Tracked field config retrieved."));
    }

    [HttpPatch("/api/admin/modified-log-config/{fieldKey}")]
    [RequirePermission(Permissions.SettingsManage)]
    [ProducesResponseType<ApiResponse<ModifiedLogConfigResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ToggleConfig(string fieldKey, [FromBody] ToggleModifiedLogConfigRequest request, CancellationToken cancellationToken)
    {
        var descriptor = TrackedFieldRegistry.GetByKey(fieldKey);
        if (descriptor is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Tracked field not found."));
        }
        if (descriptor.IsSystemTracked)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "System tracked field.", "System Tracked fields cannot be disabled."));
        }

        var config = await _log.GetConfigAsync(descriptor.EntityType, descriptor.PropertyName, cancellationToken);
        if (config is null)
        {
            config = new ModifiedLogFieldConfig
            {
                Id = Guid.NewGuid(),
                EntityType = descriptor.EntityType,
                FieldName = descriptor.PropertyName,
                IsEnabled = request.IsEnabled,
            };
            await _log.AddConfigAsync(config, cancellationToken);
        }
        else
        {
            config.IsEnabled = request.IsEnabled;
            _log.UpdateConfig(config);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(
            new ModifiedLogConfigResponse(descriptor.Key, descriptor.EntityType, descriptor.PropertyName, descriptor.DisplayName, config.IsEnabled, descriptor.IsSystemTracked),
            "Tracked field config updated."));
    }
}
