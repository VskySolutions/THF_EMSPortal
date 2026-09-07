using EmsPortal.Api.Models.UniversalFeatures;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>Mention Inbox + @mention autocomplete.</summary>
[ApiController]
[Authorize]
[Route("/api/uf")]
[Produces("application/json")]
[Tags("Universal Features — Mentions")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class MentionsController : ControllerBase
{
    private readonly IConversationMessageRepository _messages;
    private readonly IPersonRepository _persons;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;

    public MentionsController(IConversationMessageRepository messages, IPersonRepository persons, IUserRepository users, IUnitOfWork unitOfWork)
    {
        _messages = messages;
        _persons = persons;
        _users = users;
        _unitOfWork = unitOfWork;
    }

    [HttpGet("mentions")]
    [ProducesResponseType<ApiResponse<IEnumerable<MentionResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] EntityType? entityType = null,
        [FromQuery] bool? isRead = null,
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

        var (items, total) = await _messages.ListMentionsForUserAsync(
            userId, entityType, isRead, new SortRequest(sortBy, descending), page, limit, cancellationToken);
        var authorNames = await _users.GetFullNamesAsync(
            items.Where(x => x.Message.CreatedById.HasValue).Select(x => x.Message.CreatedById!.Value), cancellationToken);

        var data = items.Select(x => new MentionResponse(
            x.Mention.Id, x.Message.Id, x.Message.EntityType, x.Message.EntityId, x.Message.CreatedById,
            x.Message.CreatedById is { } id && authorNames.TryGetValue(id, out var name) ? name : null,
            x.Message.Body.Length > 160 ? x.Message.Body[..160] + "…" : x.Message.Body,
            x.Mention.IsRead, x.Message.CreatedOnUtc));
        return Ok(ApiResponseFactory.Paginated(data, "Mentions retrieved.", page, limit, total));
    }

    [HttpPut("mentions/{id:guid}/read")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var mention = await _messages.GetMentionForUserAsync(id, userId, cancellationToken);
        if (mention is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Mention not found."));
        }

        if (!mention.IsRead)
        {
            // The mention is tracked; flipping the flag and saving persists it.
            mention.IsRead = true;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Ok(ApiResponseFactory.Success(new { mentionId = id }, "Mention marked read."));
    }

    [HttpGet("mention-candidates")]
    [ProducesResponseType<ApiResponse<IEnumerable<MentionCandidateResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Candidates([FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        // Tenant people who hold a login account are valid @mention targets.
        var (people, _) = await _persons.ListAsync(
            search, null, isUser: true, isActive: true, SortRequest.Default, page: 1, limit: 20, cancellationToken: cancellationToken);
        var data = people
            .Where(p => p.UserId.HasValue)
            .Select(p => new MentionCandidateResponse(
                p.UserId!.Value,
                string.IsNullOrWhiteSpace(p.DisplayName) ? $"{p.FirstName} {p.LastName}".Trim() : p.DisplayName,
                p.PrimaryEmail));
        return Ok(ApiResponseFactory.Success(data, "Mention candidates retrieved."));
    }
}
