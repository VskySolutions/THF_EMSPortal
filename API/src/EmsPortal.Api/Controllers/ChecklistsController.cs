using EmsPortal.Api.Models.UniversalFeatures;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Abstractions.UniversalFeatures;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Checklists — completable task lists attachable to any entity record via the shared
/// <c>(EntityType, EntityId)</c> key.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/uf/checklists")]
[Produces("application/json")]
[Tags("Universal Features — Checklists")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class ChecklistsController : ControllerBase
{
    private readonly IChecklistRepository _checklists;
    private readonly IActivityEventWriter _activity;
    private readonly IUnitOfWork _unitOfWork;

    public ChecklistsController(IChecklistRepository checklists, IActivityEventWriter activity, IUnitOfWork unitOfWork)
    {
        _checklists = checklists;
        _activity = activity;
        _unitOfWork = unitOfWork;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IEnumerable<ChecklistResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] EntityType entityType, [FromQuery] Guid entityId, CancellationToken cancellationToken)
    {
        if (!User.CanAccess(entityType))
        {
            return Forbid();
        }

        var checklists = await _checklists.ListAsync(entityType, entityId, cancellationToken);
        return Ok(ApiResponseFactory.Success(checklists.Select(ToResponse), "Checklists retrieved."));
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse<ChecklistResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateChecklistRequest request, CancellationToken cancellationToken)
    {
        if (!User.CanAccess(request.EntityType))
        {
            return Forbid();
        }

        var checklist = new Checklist
        {
            Id = Guid.NewGuid(),
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            Title = request.Title.Trim(),
        };

        var order = 0;
        foreach (var text in (request.Items ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            checklist.Items.Add(new ChecklistItem { Id = Guid.NewGuid(), Text = text.Trim(), SortOrder = order++ });
        }

        await _checklists.AddAsync(checklist, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ApiResponseFactory.Success(ToResponse(checklist), "Checklist created."));
    }

    [HttpPost("{id:guid}/items")]
    [ProducesResponseType<ApiResponse<ChecklistResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddItem(Guid id, [FromBody] AddChecklistItemRequest request, CancellationToken cancellationToken)
    {
        var checklist = await _checklists.GetByIdAsync(id, cancellationToken);
        if (checklist is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Checklist not found."));
        }
        if (!User.CanAccess(checklist.EntityType))
        {
            return Forbid();
        }

        var nextOrder = checklist.Items.Count == 0 ? 0 : checklist.Items.Max(i => i.SortOrder) + 1;
        await _checklists.AddItemAsync(new ChecklistItem
        {
            Id = Guid.NewGuid(),
            ChecklistId = id,
            Text = request.Text.Trim(),
            SortOrder = nextOrder,
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _checklists.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(ToResponse(refreshed!), "Item added."));
    }

    [HttpPatch("{id:guid}/items/{itemId:guid}")]
    [ProducesResponseType<ApiResponse<ChecklistResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ToggleItem(Guid id, Guid itemId, [FromBody] ToggleChecklistItemRequest request, CancellationToken cancellationToken)
    {
        var checklist = await _checklists.GetByIdAsync(id, cancellationToken);
        if (checklist is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Checklist not found."));
        }
        if (!User.CanAccess(checklist.EntityType))
        {
            return Forbid();
        }

        var item = checklist.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Checklist item not found."));
        }

        item.IsCompleted = request.IsCompleted;
        item.CompletedById = request.IsCompleted ? User.GetUserId() : null;
        item.CompletedOnUtc = request.IsCompleted ? DateTime.UtcNow : null;
        _checklists.UpdateItem(item);

        if (request.IsCompleted)
        {
            await _activity.WriteAsync(new CreateActivityEventDto(
                checklist.EntityType, checklist.EntityId, ActivityEventTypes.ChecklistItemCompleted, NewValue: item.Text), cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(ToResponse(checklist), "Item updated."));
    }

    [HttpPut("{id:guid}/items/{itemId:guid}")]
    [ProducesResponseType<ApiResponse<ChecklistResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EditItem(Guid id, Guid itemId, [FromBody] UpdateChecklistItemRequest request, CancellationToken cancellationToken)
    {
        var checklist = await _checklists.GetByIdAsync(id, cancellationToken);
        if (checklist is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Checklist not found."));
        }
        if (!User.CanAccess(checklist.EntityType))
        {
            return Forbid();
        }

        var item = checklist.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Checklist item not found."));
        }

        item.Text = request.Text.Trim();
        _checklists.UpdateItem(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(ToResponse(checklist), "Item updated."));
    }

    [HttpPut("{id:guid}/reorder")]
    [ProducesResponseType<ApiResponse<ChecklistResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reorder(Guid id, [FromBody] ReorderChecklistRequest request, CancellationToken cancellationToken)
    {
        var checklist = await _checklists.GetByIdAsync(id, cancellationToken);
        if (checklist is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Checklist not found."));
        }
        if (!User.CanAccess(checklist.EntityType))
        {
            return Forbid();
        }

        var order = 0;
        foreach (var itemId in request.ItemIds)
        {
            var item = checklist.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not null)
            {
                item.SortOrder = order++;
                _checklists.UpdateItem(item);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var refreshed = await _checklists.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponseFactory.Success(ToResponse(refreshed!), "Checklist reordered."));
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteItem(Guid id, Guid itemId, CancellationToken cancellationToken)
    {
        var checklist = await _checklists.GetByIdAsync(id, cancellationToken);
        if (checklist is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Checklist not found."));
        }
        if (!User.CanAccess(checklist.EntityType))
        {
            return Forbid();
        }

        var item = checklist.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Checklist item not found."));
        }

        _checklists.RemoveItem(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { itemId }, "Item deleted."));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var checklist = await _checklists.GetByIdAsync(id, cancellationToken);
        if (checklist is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Checklist not found."));
        }

        var isAdmin = User.IsSuperAdmin() || User.HasPermission(Permissions.SettingsManage);
        if (checklist.CreatedById != User.GetUserId() && !isAdmin)
        {
            return Forbid();
        }

        // Items cascade-delete with the checklist (soft-delete via the configured relationship).
        foreach (var item in checklist.Items)
        {
            _checklists.RemoveItem(item);
        }
        _checklists.Remove(checklist);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { checklistId = id }, "Checklist deleted."));
    }

    private static ChecklistResponse ToResponse(Checklist checklist)
    {
        var items = checklist.Items
            .OrderBy(i => i.SortOrder)
            .Select(i => new ChecklistItemResponse(i.Id, i.Text, i.IsCompleted, i.CompletedById, i.CompletedOnUtc, i.SortOrder))
            .ToList();
        return new ChecklistResponse(
            checklist.Id, checklist.EntityType, checklist.EntityId, checklist.Title,
            items.Count(i => i.IsCompleted), items.Count, items);
    }
}
