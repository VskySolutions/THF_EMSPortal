using EmsPortal.Api.Models.UniversalFeatures;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Notification centre — the authenticated user's in-app notifications plus their per-type channel
/// preferences.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/notifications")]
[Produces("application/json")]
[Tags("Universal Features — Notifications")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationRepository _notifications;
    private readonly IUnitOfWork _unitOfWork;

    public NotificationsController(INotificationRepository notifications, IUnitOfWork unitOfWork)
    {
        _notifications = notifications;
        _unitOfWork = unitOfWork;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IEnumerable<NotificationResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] bool? isRead = null,
        [FromQuery] NotificationType? type = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var (items, total) = await _notifications.ListAsync(
            userId, isRead, type, search, createdFrom, createdTo,
            new SortRequest(sortBy, descending), page, limit, cancellationToken);
        return Ok(ApiResponseFactory.Paginated(items.Select(ToResponse), "Notifications retrieved.", page, limit, total));
    }

    [HttpGet("unread-count")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnreadCount(CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var count = await _notifications.CountUnreadAsync(userId, cancellationToken);
        return Ok(ApiResponseFactory.Success(new { unread = count }, "Unread count retrieved."));
    }

    [HttpPut("{id:guid}/read")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var notification = await _notifications.GetByIdForUserAsync(id, userId, cancellationToken);
        if (notification is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Notification not found."));
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            _notifications.Update(notification);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Ok(ApiResponseFactory.Success(new { notificationId = id }, "Notification marked read."));
    }

    [HttpPut("read-all")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var unread = await _notifications.GetUnreadAsync(userId, cancellationToken);
        foreach (var notification in unread)
        {
            notification.IsRead = true;
            _notifications.Update(notification);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(new { marked = unread.Count }, "All notifications marked read."));
    }

    [HttpGet("preferences")]
    [ProducesResponseType<ApiResponse<IEnumerable<NotificationPreferenceResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var stored = (await _notifications.GetPreferencesAsync(userId, cancellationToken))
            .ToDictionary(p => p.NotificationType);

        // Return the full matrix: every type, in-app only (default-on when unset, AC-UNI-013.4).
        var data = Enum.GetValues<NotificationType>().Select(type =>
            stored.TryGetValue(type, out var p)
                ? new NotificationPreferenceResponse(type, p.InApp)
                : new NotificationPreferenceResponse(type, true));
        return Ok(ApiResponseFactory.Success(data, "Preferences retrieved."));
    }

    [HttpPut("preferences")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        foreach (var item in request.Preferences)
        {
            var existing = await _notifications.GetPreferenceAsync(userId, item.NotificationType, cancellationToken);
            if (existing is null)
            {
                await _notifications.AddPreferenceAsync(new NotificationPreference
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    NotificationType = item.NotificationType,
                    InApp = item.InApp,
                    // In-app only (WO-124, AC-UNI-013.2): the email channel is no longer user-configurable.
                    Email = false,
                }, cancellationToken);
            }
            else
            {
                existing.InApp = item.InApp;
                existing.Email = false;
                _notifications.UpdatePreference(existing);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { updated = request.Preferences.Count }, "Preferences updated."));
    }

    private static NotificationResponse ToResponse(Notification n)
        => new(n.Id, n.Type, n.Title, n.Body, n.EntityType, n.EntityId, n.IsRead, n.IsGrouped, n.CreatedOnUtc);
}
