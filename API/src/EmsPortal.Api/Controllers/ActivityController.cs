using EmsPortal.Api.Models.UniversalFeatures;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>Activity timeline — the read-only, append-only event feed for any entity record.</summary>
[ApiController]
[Authorize]
[Route("/api/uf/activity")]
[Produces("application/json")]
[Tags("Universal Features — Activity")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class ActivityController : ControllerBase
{
    private readonly IActivityEventRepository _events;
    private readonly IUserRepository _users;

    public ActivityController(IActivityEventRepository events, IUserRepository users)
    {
        _events = events;
        _users = users;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IEnumerable<ActivityEventResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] EntityType entityType,
        [FromQuery] Guid entityId,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (!User.CanAccess(entityType))
        {
            return Forbid();
        }

        var (items, total) = await _events.ListAsync(entityType, entityId, page, limit, cancellationToken);
        var actorNames = await _users.GetFullNamesAsync(
            items.Where(e => e.ActorId.HasValue).Select(e => e.ActorId!.Value), cancellationToken);

        var data = items.Select(e => new ActivityEventResponse(
            e.Id, e.EntityType, e.EntityId, e.EventType, e.ActorId,
            e.ActorId is { } id && actorNames.TryGetValue(id, out var name) ? name : "System",
            e.OldValue, e.NewValue, e.CreatedOnUtc));
        return Ok(ApiResponseFactory.Paginated(data, "Activity retrieved.", page, limit, total));
    }
}
