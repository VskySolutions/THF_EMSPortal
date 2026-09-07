using EmsPortal.Api.Models.UniversalFeatures;
using EmsPortal.Api.Security;
using EmsPortal.Api.Storage;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Personal productivity features that attach to any entity record for the current user: Pins (max 5
/// per entity type, 50 overall), Colour Codes (upsert/clear, batch fetch for list pages).
/// </summary>
[ApiController]
[Authorize]
[Route("/api/uf")]
[Produces("application/json")]
[Tags("Universal Features — Personal")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class PersonalFeaturesController : ControllerBase
{
    private const int MaxPinsPerUser = 50;
    private const int MaxPinsPerType = 5;

    private readonly IPinRepository _pins;
    private readonly IColourCodeRepository _colours;
    private readonly IConversationMessageRepository _messages;
    private readonly IUnitOfWork _unitOfWork;

    public PersonalFeaturesController(
        IPinRepository pins,
        IColourCodeRepository colours,
        IConversationMessageRepository messages,
        IUnitOfWork unitOfWork)
    {
        _pins = pins;
        _colours = colours;
        _messages = messages;
        _unitOfWork = unitOfWork;
    }

    // ---- Pins ----

    [HttpGet("pins")]
    [ProducesResponseType<ApiResponse<IEnumerable<PinResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPins([FromQuery] int page = 1, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var (items, total) = await _pins.ListByUserAsync(userId, page, limit, cancellationToken);
        var data = items.Select(p => new PinResponse(p.Id, p.EntityType, p.EntityId, p.PinnedOnUtc));
        return Ok(ApiResponseFactory.Paginated(data, "Pinned records retrieved.", page, limit, total));
    }

    [HttpPost("pins")]
    [ProducesResponseType<ApiResponse<PinResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreatePin([FromBody] CreatePinRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }
        if (!User.CanAccess(request.EntityType))
        {
            return Forbid();
        }

        // Idempotent: re-pinning returns the existing pin.
        if (await _pins.GetAsync(userId, request.EntityType, request.EntityId, cancellationToken) is { } existing)
        {
            return Ok(ApiResponseFactory.Success(new PinResponse(existing.Id, existing.EntityType, existing.EntityId, existing.PinnedOnUtc), "Already pinned."));
        }

        // Max 5 pins per entity type, and 50 overall.
        if (await _pins.CountByUserAndTypeAsync(userId, request.EntityType, cancellationToken) >= MaxPinsPerType)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Pin limit reached.", $"You can pin at most {MaxPinsPerType} records of this type."));
        }
        if (await _pins.CountByUserAsync(userId, cancellationToken) >= MaxPinsPerUser)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Pin limit reached.", $"You can pin at most {MaxPinsPerUser} records."));
        }

        var pin = new Pin
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            PinnedOnUtc = DateTime.UtcNow,
        };
        await _pins.AddAsync(pin, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created,
            ApiResponseFactory.Success(new PinResponse(pin.Id, pin.EntityType, pin.EntityId, pin.PinnedOnUtc), "Record pinned."));
    }

    [HttpDelete("pins/{id:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeletePin(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var pin = await _pins.GetByIdForUserAsync(id, userId, cancellationToken);
        if (pin is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Pin not found."));
        }

        _pins.Remove(pin);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { pinId = id }, "Record unpinned."));
    }

    // ---- Colour codes ----

    [HttpGet("colour-codes")]
    [ProducesResponseType<ApiResponse<IReadOnlyDictionary<Guid, string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetColours(
        [FromQuery] EntityType entityType, [FromQuery] Guid[] entityIds, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var map = await _colours.GetBatchAsync(userId, entityType, entityIds ?? Array.Empty<Guid>(), cancellationToken);
        return Ok(ApiResponseFactory.Success(map, "Colour codes retrieved."));
    }

    [HttpPut("colour-codes")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertColour([FromBody] UpsertColourCodeRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }
        if (!User.CanAccess(request.EntityType))
        {
            return Forbid();
        }

        var existing = await _colours.GetAsync(userId, request.EntityType, request.EntityId, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Colour))
        {
            // Null/empty colour clears the assignment.
            if (existing is not null)
            {
                _colours.Remove(existing);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Ok(ApiResponseFactory.Success(new { cleared = true }, "Colour cleared."));
        }

        if (existing is null)
        {
            await _colours.AddAsync(new ColourCode
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                Colour = request.Colour.Trim(),
            }, cancellationToken);
        }
        else
        {
            existing.Colour = request.Colour.Trim();
            _colours.Update(existing);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { colour = request.Colour.Trim() }, "Colour saved."));
    }

    // ---- PDF export ----

    [HttpPost("pdf-export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportPdf([FromBody] PdfExportRequest request, CancellationToken cancellationToken)
    {
        if (!User.CanAccess(request.EntityType))
        {
            return Forbid();
        }

        var (title, lines) = await BuildExportContentAsync(request, cancellationToken);
        if (request.IncludeConversation)
        {
            await AppendConversationAsync(request.EntityType, request.EntityId, lines, cancellationToken);
        }

        var pdf = SimplePdfWriter.Build(title, lines);
        return File(pdf, "application/pdf", $"{request.EntityType}-{request.EntityId}.pdf");
    }

    private static Task<(string Title, List<string> Lines)> BuildExportContentAsync(PdfExportRequest request, CancellationToken cancellationToken)
    {
        var lines = new List<string>
        {
            $"Entity Type: {request.EntityType}",
            $"Entity Id: {request.EntityId}",
        };
        return Task.FromResult(($"{request.EntityType} Export", lines));
    }

    private async Task AppendConversationAsync(EntityType entityType, Guid entityId, List<string> lines, CancellationToken cancellationToken)
    {
        var (messages, _) = await _messages.ListAsync(entityType, entityId, null, null, 1, 100, cancellationToken);
        if (messages.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add("Conversation:");
        foreach (var message in messages)
        {
            lines.Add($"- [{message.CreatedOnUtc:u}] {message.Body}");
        }
    }
}
