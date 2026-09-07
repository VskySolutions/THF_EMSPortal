using EmsPortal.Api.Models.UniversalFeatures;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>Reminders — personal, date-based reminders a user sets against any entity record.</summary>
[ApiController]
[Authorize]
[Route("/api/uf/reminders")]
[Produces("application/json")]
[Tags("Universal Features — Reminders")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class RemindersController : ControllerBase
{
    private readonly IReminderRepository _reminders;
    private readonly IUnitOfWork _unitOfWork;

    public RemindersController(IReminderRepository reminders, IUnitOfWork unitOfWork)
    {
        _reminders = reminders;
        _unitOfWork = unitOfWork;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IEnumerable<ReminderResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var (items, total) = await _reminders.ListByUserAsync(userId, page, limit, cancellationToken);
        return Ok(ApiResponseFactory.Paginated(items.Select(ToResponse), "Reminders retrieved.", page, limit, total));
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse<ReminderResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateReminderRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }
        if (!User.CanAccess(request.EntityType))
        {
            return Forbid();
        }

        var reminder = new Reminder
        {
            Id = Guid.NewGuid(),
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            UserId = userId,
            DueAtUtc = request.DueAtUtc,
            Note = request.Note,
        };
        await _reminders.AddAsync(reminder, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ApiResponseFactory.Success(ToResponse(reminder), "Reminder created."));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ApiResponse<ReminderResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateReminderRequest request, CancellationToken cancellationToken)
    {
        var reminder = await _reminders.GetByIdAsync(id, cancellationToken);
        if (reminder is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Reminder not found."));
        }
        if (reminder.UserId != User.GetUserId())
        {
            return Forbid();
        }

        reminder.DueAtUtc = request.DueAtUtc;
        reminder.Note = request.Note;
        // Re-arm: editing a reminder lets it dispatch again at the new time.
        reminder.IsDispatched = false;
        reminder.IsOverdue = false;
        _reminders.Update(reminder);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(ToResponse(reminder), "Reminder updated."));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var reminder = await _reminders.GetByIdAsync(id, cancellationToken);
        if (reminder is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Reminder not found."));
        }

        var isAdmin = User.IsSuperAdmin() || User.HasPermission(Permissions.SettingsManage);
        if (reminder.UserId != User.GetUserId() && !isAdmin)
        {
            return Forbid();
        }

        _reminders.Remove(reminder);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { reminderId = id }, "Reminder cancelled."));
    }

    private static ReminderResponse ToResponse(Reminder r)
        => new(r.Id, r.EntityType, r.EntityId, r.DueAtUtc, r.Note, r.IsDispatched, r.IsOverdue, r.CreatedOnUtc);
}
