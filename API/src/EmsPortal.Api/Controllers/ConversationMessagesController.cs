using System.Text.RegularExpressions;
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
/// Conversations — the freeform, @mention-aware thread on any entity record, addressed through the
/// shared <c>(EntityType, EntityId)</c> key.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/uf/conversation-messages")]
[Produces("application/json")]
[Tags("Universal Features — Conversations")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class ConversationMessagesController : ControllerBase
{
    /// <summary>Matches an @mention token of the form <c>@[Display Name](guid)</c>.</summary>
    private static readonly Regex MentionPattern = new(
        @"@\[[^\]]*\]\((?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\)",
        RegexOptions.Compiled);

    private readonly IConversationMessageRepository _messages;
    private readonly IUserRepository _users;
    private readonly IActivityEventWriter _activity;
    private readonly INotificationDispatcher _notifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRemsApprovalRepository _approvals;

    public ConversationMessagesController(
        IConversationMessageRepository messages,
        IUserRepository users,
        IActivityEventWriter activity,
        INotificationDispatcher notifications,
        IUnitOfWork unitOfWork,
        IRemsApprovalRepository approvals)
    {
        _messages = messages;
        _users = users;
        _activity = activity;
        _notifications = notifications;
        _unitOfWork = unitOfWork;
        _approvals = approvals;
    }

    [HttpGet]
    [ProducesResponseType<ApiResponse<IEnumerable<ConversationMessageResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] EntityType entityType,
        [FromQuery] Guid entityId,
        [FromQuery] string? search = null,
        [FromQuery] Guid? authorId = null,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (!await CanReadConversationAsync(entityType, entityId, cancellationToken))
        {
            return Forbid();
        }

        var (items, total) = await _messages.ListAsync(entityType, entityId, search, authorId, page, limit, cancellationToken);
        var authorNames = await _users.GetFullNamesAsync(
            items.Where(m => m.CreatedById.HasValue).Select(m => m.CreatedById!.Value), cancellationToken);

        var data = items.Select(m => ToResponse(m, authorNames));
        return Ok(ApiResponseFactory.Paginated(data, "Conversation retrieved.", page, limit, total));
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse<ConversationMessageResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateConversationMessageRequest request, CancellationToken cancellationToken)
    {
        if (!await CanReadConversationAsync(request.EntityType, request.EntityId, cancellationToken))
        {
            return Forbid();
        }

        var message = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            Body = request.Body,
        };
        await _messages.AddAsync(message, cancellationToken);

        var mentionIds = ResolveMentions(request.Body, request.MentionedUserIds);
        foreach (var userId in mentionIds)
        {
            await _messages.AddMentionAsync(
                new ConversationMessageMention { Id = Guid.NewGuid(), ConversationMessageId = message.Id, MentionedUserId = userId },
                cancellationToken);
        }

        await _activity.WriteAsync(new CreateActivityEventDto(request.EntityType, request.EntityId, ActivityEventTypes.ConversationMessageAdded), cancellationToken);
        await NotifyMentionsAsync(message, mentionIds, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var authorNames = await ResolveAuthorNameAsync(message.CreatedById, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiResponseFactory.Success(ToResponse(message, authorNames), "Message posted."));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ApiResponse<ConversationMessageResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateConversationMessageRequest request, CancellationToken cancellationToken)
    {
        var message = await _messages.GetByIdAsync(id, cancellationToken);
        if (message is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Message not found."));
        }

        // Only the author may edit their own message.
        if (message.CreatedById != User.GetUserId())
        {
            return Forbid();
        }

        message.Body = request.Body;
        message.IsEdited = true;
        message.EditedOnUtc = DateTime.UtcNow;
        _messages.Update(message);

        // Re-notify only mentions newly added in this edit.
        var existing = message.Mentions.Select(m => m.MentionedUserId).ToHashSet();
        var resolved = ResolveMentions(request.Body, request.MentionedUserIds);
        var added = resolved.Where(uid => !existing.Contains(uid)).ToList();
        foreach (var userId in added)
        {
            await _messages.AddMentionAsync(
                new ConversationMessageMention { Id = Guid.NewGuid(), ConversationMessageId = message.Id, MentionedUserId = userId },
                cancellationToken);
        }

        await NotifyMentionsAsync(message, added, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var authorNames = await ResolveAuthorNameAsync(message.CreatedById, cancellationToken);
        return Ok(ApiResponseFactory.Success(ToResponse(message, authorNames), "Message updated."));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var message = await _messages.GetByIdAsync(id, cancellationToken);
        if (message is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Message not found."));
        }

        // Author may delete their own message; Tenant Admin / Super Admin may delete any.
        var isAdmin = User.IsSuperAdmin() || User.HasPermission(Permissions.SettingsManage);
        if (message.CreatedById != User.GetUserId() && !isAdmin)
        {
            return Forbid();
        }

        _messages.Remove(message);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { conversationMessageId = id }, "Message deleted."));
    }

    /// <summary>
    /// Whether the caller may read or post on this record's conversation: the parent entity's read
    /// permission, or — on a REMS request — having been routed an approval task on that request.
    /// </summary>
    private async Task<bool> CanReadConversationAsync(
        EntityType entityType, Guid entityId, CancellationToken cancellationToken)
    {
        if (User.CanAccess(entityType))
        {
            return true;
        }

        return entityType == EntityType.Rems
            && User.GetUserId() is { } me
            && await _approvals.IsApproverOnRequestAsync(entityId, me, cancellationToken);
    }

    private async Task NotifyMentionsAsync(ConversationMessage message, IReadOnlyCollection<Guid> mentionIds, CancellationToken cancellationToken)
    {
        if (mentionIds.Count == 0)
        {
            return;
        }

        var actorId = User.GetUserId();
        var actorNames = actorId is { } aid
            ? await _users.GetFullNamesAsync(new[] { aid }, cancellationToken)
            : new Dictionary<Guid, string>();
        var actorName = actorId is { } a && actorNames.TryGetValue(a, out var n) ? n : "Someone";
        var preview = message.Body.Length > 140 ? message.Body[..140] + "…" : message.Body;

        foreach (var userId in mentionIds.Where(uid => uid != actorId))
        {
            await _notifications.DispatchAsync(new CreateNotificationDto(
                userId, NotificationType.Mention, $"{actorName} mentioned you in a conversation", preview, message.EntityType, message.EntityId),
                cancellationToken);
        }
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveAuthorNameAsync(Guid? authorId, CancellationToken cancellationToken)
        => authorId is { } id
            ? await _users.GetFullNamesAsync(new[] { id }, cancellationToken)
            : new Dictionary<Guid, string>();

    private static List<Guid> ResolveMentions(string body, IEnumerable<Guid>? explicitIds)
    {
        var ids = new HashSet<Guid>(explicitIds ?? Enumerable.Empty<Guid>());
        foreach (Match match in MentionPattern.Matches(body))
        {
            if (Guid.TryParse(match.Groups["id"].Value, out var id))
            {
                ids.Add(id);
            }
        }

        return ids.ToList();
    }

    private static ConversationMessageResponse ToResponse(ConversationMessage message, IReadOnlyDictionary<Guid, string> authorNames)
        => new(
            message.Id,
            message.EntityType,
            message.EntityId,
            message.Body,
            message.CreatedById,
            message.CreatedById is { } id && authorNames.TryGetValue(id, out var name) ? name : null,
            message.IsEdited,
            message.EditedOnUtc,
            message.CreatedOnUtc,
            message.Mentions.Select(m => m.MentionedUserId).ToList());
}
