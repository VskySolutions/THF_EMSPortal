using EmsPortal.Api.Models.UniversalFeatures;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Sticky Notes — floating notes that are either personal (owned by the creator) or tenant-broadcast
/// to every user until dismissed.
/// </summary>
[ApiController]
[Authorize]
[Produces("application/json")]
[Tags("Universal Features — Sticky Notes")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class StickyNotesController : ControllerBase
{
    private readonly IStickyNoteRepository _notes;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;

    public StickyNotesController(IStickyNoteRepository notes, IUserRepository users, IUnitOfWork unitOfWork)
    {
        _notes = notes;
        _users = users;
        _unitOfWork = unitOfWork;
    }

    [HttpGet("/api/uf/sticky-notes")]
    [ProducesResponseType<ApiResponse<IEnumerable<StickyNoteResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? scope = null, CancellationToken cancellationToken = default)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var notes = await _notes.ListActiveForUserAsync(userId, scope, cancellationToken);
        var states = await _notes.GetStatesAsync(userId, notes.Select(n => n.Id).ToList(), cancellationToken);
        var data = notes.Select(n => ToResponse(n, userId, states.GetValueOrDefault(n.Id)));
        return Ok(ApiResponseFactory.Success(data, "Sticky notes retrieved."));
    }

    [HttpPost("/api/uf/sticky-notes")]
    [ProducesResponseType<ApiResponse<StickyNoteResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateStickyNoteRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        // Tenant (broadcast) notes require settings.manage; personal notes are open to any user.
        if (!request.IsPersonal && !CanManageTenantNotes())
        {
            return Forbid();
        }

        var note = new StickyNote
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = userId,
            Title = request.Title?.Trim(),
            Body = request.Body,
            Colour = request.Colour,
            Scope = string.IsNullOrWhiteSpace(request.Scope) ? "global" : request.Scope.Trim(),
            IsPersonal = request.IsPersonal,
            ExpiresAtUtc = request.IsPersonal ? null : request.ExpiresAtUtc,
        };
        await _notes.AddAsync(note, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ApiResponseFactory.Success(ToResponse(note, userId, null), "Sticky note created."));
    }

    [HttpPut("/api/uf/sticky-notes/{id:guid}")]
    [ProducesResponseType<ApiResponse<StickyNoteResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStickyNoteRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var note = await _notes.GetByIdAsync(id, cancellationToken);
        if (note is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Sticky note not found."));
        }
        if (!CanModify(note, userId))
        {
            return Forbid();
        }

        note.Title = request.Title?.Trim();
        note.Body = request.Body;
        note.Colour = request.Colour;
        note.Scope = string.IsNullOrWhiteSpace(request.Scope) ? "global" : request.Scope.Trim();
        if (!note.IsPersonal)
        {
            note.ExpiresAtUtc = request.ExpiresAtUtc;
            // Editing a tenant note re-surfaces it for everyone who had dismissed it.
            foreach (var dismissal in await _notes.GetDismissalsByNoteAsync(id, cancellationToken))
            {
                _notes.RemoveDismissal(dismissal);
            }
        }
        _notes.Update(note);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(ToResponse(note, userId, null), "Sticky note updated."));
    }

    [HttpDelete("/api/uf/sticky-notes/{id:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var note = await _notes.GetByIdAsync(id, cancellationToken);
        if (note is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Sticky note not found."));
        }
        if (!CanModify(note, userId))
        {
            return Forbid();
        }

        _notes.Remove(note);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { stickyNoteId = id }, "Sticky note deleted."));
    }

    [HttpPost("/api/uf/sticky-notes/{id:guid}/dismiss")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Dismiss(Guid id, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var note = await _notes.GetByIdAsync(id, cancellationToken);
        if (note is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Sticky note not found."));
        }
        if (note.IsPersonal)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, "Cannot dismiss.", "Personal sticky notes cannot be dismissed; delete them instead."));
        }

        if (await _notes.GetDismissalAsync(id, userId, cancellationToken) is null)
        {
            await _notes.AddDismissalAsync(new StickyNoteDismissal { Id = Guid.NewGuid(), StickyNoteId = id, UserId = userId }, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Ok(ApiResponseFactory.Success(new { stickyNoteId = id }, "Sticky note dismissed."));
    }

    [HttpPut("/api/uf/sticky-note-states/{noteId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertState(Guid noteId, [FromBody] StickyNoteStateRequest request, CancellationToken cancellationToken)
    {
        if (User.GetUserId() is not { } userId)
        {
            return Unauthorized(ApiResponseFactory.Unauthorized("No user context."));
        }

        var note = await _notes.GetByIdAsync(noteId, cancellationToken);
        if (note is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Sticky note not found."));
        }

        var state = await _notes.GetStateAsync(noteId, userId, cancellationToken);
        if (state is null)
        {
            await _notes.AddStateAsync(new UserStickyNoteState
            {
                Id = Guid.NewGuid(),
                StickyNoteId = noteId,
                UserId = userId,
                X = request.X,
                Y = request.Y,
                Width = request.Width,
                Height = request.Height,
                IsMinimised = request.IsMinimised,
                ZIndex = request.ZIndex,
            }, cancellationToken);
        }
        else
        {
            state.X = request.X;
            state.Y = request.Y;
            state.Width = request.Width;
            state.Height = request.Height;
            state.IsMinimised = request.IsMinimised;
            state.ZIndex = request.ZIndex;
            _notes.UpdateState(state);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { saved = true }, "State saved."));
    }

    /// <summary>What the tenant Sticky Notes list may be ordered by.</summary>
    private static readonly SortMap<AdminStickyNoteResponse> AdminListSorts =
        new SortMap<AdminStickyNoteResponse>("updatedOnUtc")
            .Add("title", n => n.Title, n => n.CreatedOnUtc)
            .Add("scope", n => n.Scope, n => n.CreatedOnUtc)
            .Add("expiresAtUtc", n => n.ExpiresAtUtc, n => n.CreatedOnUtc)
            .Add("dismissalCount", n => n.DismissalCount, n => n.CreatedOnUtc)
            .Add("createdOnUtc", n => n.CreatedOnUtc)
            .Add("updatedOnUtc", n => n.UpdatedOnUtc);

    [HttpGet("/api/admin/sticky-notes")]
    [RequirePermission(Permissions.SettingsManage)]
    [ProducesResponseType<ApiResponse<IEnumerable<AdminStickyNoteResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AdminList(
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var rows = await _notes.ListTenantNotesWithCountsAsync(cancellationToken);

        // One name lookup for the page, so the audit columns read as people rather than guids.
        var names = await _users.GetFullNamesAsync(
            rows.SelectMany(r => new[] { r.Note.CreatedById, r.Note.UpdatedById })
                .Where(id => id.HasValue).Select(id => id!.Value),
            cancellationToken);
        string? NameOf(Guid? id) => id is { } uid && names.TryGetValue(uid, out var n) ? n : null;

        var data = rows.Select(r => new AdminStickyNoteResponse(
            r.Note.Id, r.Note.Title, r.Note.Body, r.Note.Colour, r.Note.Scope, r.Note.ExpiresAtUtc, r.DismissalCount,
            r.Note.CreatedOnUtc, NameOf(r.Note.CreatedById), NameOf(r.Note.UpdatedById), r.Note.UpdatedOnUtc));
        return Ok(ApiResponseFactory.Success(
            AdminListSorts.Apply(data, sortBy, descending), "Tenant sticky notes retrieved."));
    }

    private bool CanManageTenantNotes() => User.IsSuperAdmin() || User.HasPermission(Permissions.SettingsManage);

    private bool CanModify(StickyNote note, Guid userId)
        => note.IsPersonal ? note.CreatedByUserId == userId : CanManageTenantNotes();

    private static StickyNoteResponse ToResponse(StickyNote note, Guid userId, UserStickyNoteState? state)
        => new(
            note.Id,
            note.Title,
            note.Body,
            note.Colour,
            note.Scope,
            note.IsPersonal,
            note.CreatedByUserId == userId,
            note.ExpiresAtUtc,
            note.CreatedOnUtc,
            state is null ? null : new StickyNoteStateResponse(state.X, state.Y, state.Width, state.Height, state.IsMinimised, state.ZIndex));
}
