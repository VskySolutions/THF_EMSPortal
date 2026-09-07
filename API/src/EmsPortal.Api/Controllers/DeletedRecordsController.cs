using EmsPortal.Api.Models.UniversalFeatures;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Deleted Records Management — lets admins view, restore, and permanently delete soft-deleted
/// records, and configure the retention period.
/// </summary>
[ApiController]
[Authorize]
[Produces("application/json")]
[Tags("Universal Features — Deleted Records")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class DeletedRecordsController : ControllerBase
{
    private const int DefaultRetentionDays = 90;

    private readonly IDeletedRecordsRepository _deleted;
    private readonly IRetentionConfigRepository _retention;
    private readonly IUserRepository _users;
    private readonly IActivityEventWriter _activity;
    private readonly IUnitOfWork _unitOfWork;

    public DeletedRecordsController(
        IDeletedRecordsRepository deleted,
        IRetentionConfigRepository retention,
        IUserRepository users,
        IActivityEventWriter activity,
        IUnitOfWork unitOfWork)
    {
        _deleted = deleted;
        _retention = retention;
        _users = users;
        _activity = activity;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Resolves the Super-Admin tenant override (others are pinned to their ambient tenant).</summary>
    private Guid? ResolveTenant(Guid? tenantId) => User.IsSuperAdmin() ? tenantId : null;

    [HttpGet("/api/uf/deleted")]
    [RequirePermission(Permissions.RecordsAdminDelete)]
    [ProducesResponseType<ApiResponse<IEnumerable<DeletedRecordResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] EntityType entityType,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        if (!_deleted.IsSupported(entityType))
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Unsupported entity type.", $"Deleted Records Management is not available for {entityType}."));
        }

        var tenant = ResolveTenant(tenantId);
        var (items, total) = await _deleted.ListDeletedAsync(entityType, tenant, new SortRequest(sortBy, descending), page, limit, cancellationToken);
        var retentionDays = (await _retention.GetAsync(tenant, cancellationToken))?.RetentionDays ?? DefaultRetentionDays;
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        var names = await _users.GetFullNamesAsync(items.Where(i => i.DeletedById.HasValue).Select(i => i.DeletedById!.Value), cancellationToken);
        var data = items.Select(i => new DeletedRecordResponse(
            entityType, i.EntityId, i.Identity, i.TenantId, i.DeletedById,
            i.DeletedById is { } id && names.TryGetValue(id, out var name) ? name : null,
            i.DeletedOnUtc, i.DeletedOnUtc is { } d && d <= cutoff));
        return Ok(ApiResponseFactory.Paginated(data, "Deleted records retrieved.", page, limit, total));
    }

    [HttpPost("/api/uf/restore")]
    [RequirePermission(Permissions.RecordsAdminDelete)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Restore([FromBody] RestoreRecordRequest request, CancellationToken cancellationToken)
    {
        if (!await _deleted.RestoreAsync(request.EntityType, request.EntityId, null, cancellationToken))
        {
            return NotFound(ApiResponseFactory.NotFound("Deleted record not found."));
        }

        await WriteRestoreEventAsync(request.EntityType, request.EntityId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { request.EntityType, request.EntityId }, "Record restored."));
    }

    [HttpPost("/api/uf/restore/bulk")]
    [RequirePermission(Permissions.RecordsAdminDelete)]
    [ProducesResponseType<ApiResponse<IEnumerable<BulkRecordResult>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RestoreBulk([FromBody] BulkRestoreRequest request, CancellationToken cancellationToken)
    {
        var results = new List<BulkRecordResult>();
        foreach (var entityId in request.EntityIds.Distinct())
        {
            try
            {
                if (await _deleted.RestoreAsync(request.EntityType, entityId, null, cancellationToken))
                {
                    await WriteRestoreEventAsync(request.EntityType, entityId, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    results.Add(new BulkRecordResult(entityId, true, "Restored."));
                }
                else
                {
                    results.Add(new BulkRecordResult(entityId, false, "Not found."));
                }
            }
            catch (Exception ex)
            {
                results.Add(new BulkRecordResult(entityId, false, ex.Message));
            }
        }

        return Ok(ApiResponseFactory.Success(results, "Bulk restore completed."));
    }

    [HttpDelete("/api/uf/hard-delete")]
    [RequirePermission(Permissions.RecordsAdminDelete)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> HardDelete([FromBody] HardDeleteRequest request, CancellationToken cancellationToken)
    {
        var identity = await _deleted.GetDeletedIdentityAsync(request.EntityType, request.EntityId, null, cancellationToken);
        if (identity is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Deleted record not found."));
        }
        if (!string.Equals(identity, request.ConfirmationToken?.Trim(), StringComparison.Ordinal))
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Confirmation failed.", "The confirmation token does not match the record identifier."));
        }

        await _unitOfWork.ExecuteInTransactionAsync(ct => _deleted.HardDeleteAsync(request.EntityType, request.EntityId, null, ct), cancellationToken);
        return Ok(ApiResponseFactory.Success(new { request.EntityType, request.EntityId }, "Record permanently deleted."));
    }

    [HttpDelete("/api/uf/hard-delete/bulk")]
    [RequirePermission(Permissions.RecordsAdminDelete)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> HardDeleteBulk([FromBody] BulkHardDeleteRequest request, CancellationToken cancellationToken)
    {
        var ids = request.EntityIds.Distinct().ToList();
        if (ids.Count != request.ConfirmationCount)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Confirmation failed.", $"Confirmation count {request.ConfirmationCount} does not match the {ids.Count} record(s) selected."));
        }

        // All-or-nothing: the whole batch commits in a single transaction.
        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var entityId in ids)
            {
                await _deleted.HardDeleteAsync(request.EntityType, entityId, null, ct);
            }
        }, cancellationToken);

        return Ok(ApiResponseFactory.Success(new { count = ids.Count }, "Records permanently deleted."));
    }

    [HttpGet("/api/admin/retention-config")]
    [RequirePermission(Permissions.RecordsAdminDelete)]
    [ProducesResponseType<ApiResponse<RetentionConfigResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRetention([FromQuery] Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var tenant = ResolveTenant(tenantId);
        var config = await _retention.GetAsync(tenant, cancellationToken);
        return Ok(ApiResponseFactory.Success(new RetentionConfigResponse(config?.RetentionDays ?? DefaultRetentionDays), "Retention config retrieved."));
    }

    [HttpPut("/api/admin/retention-config")]
    [RequirePermission(Permissions.RecordsAdminDelete)]
    [ProducesResponseType<ApiResponse<RetentionConfigResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateRetention([FromBody] UpdateRetentionConfigRequest request, [FromQuery] Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        // Cross-tenant retention changes are Super-Admin only.
        if (tenantId is not null && !User.IsSuperAdmin())
        {
            return Forbid();
        }

        var tenant = ResolveTenant(tenantId);
        var config = await _retention.GetAsync(tenant, cancellationToken);
        if (config is null)
        {
            config = new DeletedRecordRetentionConfig { Id = Guid.NewGuid(), RetentionDays = request.RetentionDays };
            if (tenant is { } t)
            {
                config.TenantId = t;
            }
            await _retention.AddAsync(config, cancellationToken);
        }
        else
        {
            config.RetentionDays = request.RetentionDays;
            _retention.Update(config);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new RetentionConfigResponse(config.RetentionDays), "Retention config updated."));
    }

    [HttpGet("/api/admin/retention-overdue")]
    [RequirePermission(Permissions.RecordsAdminDelete)]
    [ProducesResponseType<ApiResponse<IEnumerable<RetentionOverdueResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RetentionOverdue([FromQuery] Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var tenant = ResolveTenant(tenantId);
        var retentionDays = (await _retention.GetAsync(tenant, cancellationToken))?.RetentionDays ?? DefaultRetentionDays;
        var counts = await _deleted.CountOverdueAsync(retentionDays, tenant, cancellationToken);
        var data = counts.Select(kv => new RetentionOverdueResponse(kv.Key, kv.Value));
        return Ok(ApiResponseFactory.Success(data, "Retention overdue counts retrieved."));
    }

    private async Task WriteRestoreEventAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken)
    {
        var actorId = User.GetUserId();
        var actorName = actorId is { } id
            ? (await _users.GetFullNamesAsync(new[] { id }, cancellationToken)).GetValueOrDefault(id, "an administrator")
            : "an administrator";
        await _activity.WriteAsync(new CreateActivityEventDto(entityType, entityId, ActivityEventTypes.Restored, NewValue: $"Restored by {actorName}"), cancellationToken);
    }
}
