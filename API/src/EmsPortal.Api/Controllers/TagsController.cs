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
/// Tags — tenant-managed labels (CRUD under <c>/api/admin/tags</c>, gated by <c>settings.manage</c>)
/// and their application to entity records (<c>/api/uf/entity-tags</c>.
/// </summary>
[ApiController]
[Authorize]
[Produces("application/json")]
[Tags("Universal Features — Tags")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class TagsController : ControllerBase
{
    private readonly ITagRepository _tags;
    private readonly IUserRepository _users;
    private readonly IActivityEventWriter _activity;
    private readonly IUnitOfWork _unitOfWork;

    public TagsController(ITagRepository tags, IUserRepository users, IActivityEventWriter activity, IUnitOfWork unitOfWork)
    {
        _tags = tags;
        _users = users;
        _activity = activity;
        _unitOfWork = unitOfWork;
    }

    // ---- Tag catalogue (admin) ----

    /// <summary>What the Tags list may be ordered by.</summary>
    private static readonly SortMap<TagResponse> ListSorts = new SortMap<TagResponse>("updatedOnUtc")
        .Add("name", t => t.Name)
        .Add("colour", t => t.Colour, t => t.Name)
        .Add("category", t => t.Category, t => t.Name)
        .Add("usageCount", t => t.UsageCount, t => t.Name)
        .Add("createdOnUtc", t => t.CreatedOnUtc)
        .Add("updatedOnUtc", t => t.UpdatedOnUtc);

    [HttpGet("/api/admin/tags")]
    [RequirePermission(Permissions.SettingsManage)]
    [ProducesResponseType<ApiResponse<IEnumerable<TagResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var tags = await _tags.ListAsync(search, cancellationToken);
        var counts = await _tags.GetUsageCountsAsync(cancellationToken);

        // One name lookup for the page, so the audit columns read as people rather than guids.
        var names = await _users.GetFullNamesAsync(
            tags.SelectMany(t => new[] { t.CreatedById, t.UpdatedById })
                .Where(id => id.HasValue).Select(id => id!.Value),
            cancellationToken);
        string? NameOf(Guid? id) => id is { } uid && names.TryGetValue(uid, out var n) ? n : null;

        var data = tags.Select(t => new TagResponse(
            t.Id, t.Name, t.Colour, t.Category, counts.TryGetValue(t.Id, out var c) ? c : 0,
            NameOf(t.CreatedById), t.CreatedOnUtc, NameOf(t.UpdatedById), t.UpdatedOnUtc));
        // Ordered after projecting: Usage is a count assembled here, not a column on the tag.
        return Ok(ApiResponseFactory.Success(ListSorts.Apply(data, sortBy, descending), "Tags retrieved."));
    }

    [HttpPost("/api/admin/tags")]
    [RequirePermission(Permissions.SettingsManage)]
    [ProducesResponseType<ApiResponse<TagResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateTagRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await _tags.GetByNameAsync(name, cancellationToken) is not null)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.DuplicateIdentifier, "Duplicate tag.", $"A tag named '{name}' already exists."));
        }

        var tag = new Tag { Id = Guid.NewGuid(), Name = name, Colour = request.Colour?.Trim(), Category = request.Category?.Trim() };
        await _tags.AddAsync(tag, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created,
            ApiResponseFactory.Success(new TagResponse(tag.Id, tag.Name, tag.Colour, tag.Category, 0), "Tag created."));
    }

    [HttpPut("/api/admin/tags/{id:guid}")]
    [RequirePermission(Permissions.SettingsManage)]
    [ProducesResponseType<ApiResponse<TagResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTagRequest request, CancellationToken cancellationToken)
    {
        var tag = await _tags.GetByIdAsync(id, cancellationToken);
        if (tag is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Tag not found."));
        }

        var name = request.Name.Trim();
        if (!string.Equals(name, tag.Name, StringComparison.Ordinal)
            && await _tags.GetByNameAsync(name, cancellationToken) is not null)
        {
            return BadRequest(ApiResponseFactory.Error(ApiErrorCodes.DuplicateIdentifier, "Duplicate tag.", $"A tag named '{name}' already exists."));
        }

        // Name/colour are resolved from the Tag row at read time, so the change propagates to every
        // entity application automatically — no per-application update needed.
        tag.Name = name;
        tag.Colour = request.Colour?.Trim();
        tag.Category = request.Category?.Trim();
        _tags.Update(tag);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await _tags.GetUsageCountsAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(
            new TagResponse(tag.Id, tag.Name, tag.Colour, tag.Category, counts.TryGetValue(tag.Id, out var c) ? c : 0), "Tag updated."));
    }

    [HttpDelete("/api/admin/tags/{id:guid}")]
    [RequirePermission(Permissions.SettingsManage)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var tag = await _tags.GetByIdAsync(id, cancellationToken);
        if (tag is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Tag not found."));
        }

        // Soft-delete every application first, then the tag itself.
        foreach (var application in await _tags.GetApplicationsByTagAsync(id, cancellationToken))
        {
            _tags.RemoveEntityTag(application);
        }
        _tags.Remove(tag);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponseFactory.Success(new { tagId = id }, "Tag deleted."));
    }

    // ---- Tag applications on entity records ----

    [HttpGet("/api/uf/tags")]
    [ProducesResponseType<ApiResponse<IEnumerable<TagResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Picker([FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        // Read-only tenant tag list for the "apply tag" picker — available to any authenticated tenant user.
        var tags = await _tags.ListAsync(search, cancellationToken);
        var data = tags.Select(t => new TagResponse(t.Id, t.Name, t.Colour, t.Category, 0));
        return Ok(ApiResponseFactory.Success(data, "Tags retrieved."));
    }

    [HttpGet("/api/uf/entity-tags")]
    [ProducesResponseType<ApiResponse<IEnumerable<EntityTagResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListEntityTags(
        [FromQuery] EntityType entityType, [FromQuery] Guid entityId, CancellationToken cancellationToken)
    {
        if (!User.CanAccess(entityType))
        {
            return Forbid();
        }

        var applications = await _tags.GetEntityTagsAsync(entityType, entityId, cancellationToken);
        var data = applications.Select(ToResponse);
        return Ok(ApiResponseFactory.Success(data, "Tags retrieved."));
    }

    [HttpPost("/api/uf/entity-tags")]
    [ProducesResponseType<ApiResponse<EntityTagResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Apply([FromBody] ApplyTagRequest request, CancellationToken cancellationToken)
    {
        if (!User.CanAccess(request.EntityType))
        {
            return Forbid();
        }

        var tag = await _tags.GetByIdAsync(request.TagId, cancellationToken);
        if (tag is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Tag not found."));
        }

        // Idempotent: applying an already-applied tag returns the existing application.
        var existing = await _tags.GetEntityTagAsync(request.EntityType, request.EntityId, request.TagId, cancellationToken);
        if (existing is not null)
        {
            return Ok(ApiResponseFactory.Success(ToResponse(existing, tag), "Tag already applied."));
        }

        var application = new EntityTag
        {
            Id = Guid.NewGuid(),
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            TagId = request.TagId,
        };
        await _tags.AddEntityTagAsync(application, cancellationToken);
        await _activity.WriteAsync(new CreateActivityEventDto(request.EntityType, request.EntityId, ActivityEventTypes.TagApplied, NewValue: tag.Name), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ApiResponseFactory.Success(ToResponse(application, tag), "Tag applied."));
    }

    [HttpDelete("/api/uf/entity-tags/{id:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        var application = await _tags.GetEntityTagAsync(id, cancellationToken);
        if (application is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Tag application not found."));
        }
        if (!User.CanAccess(application.EntityType))
        {
            return Forbid();
        }

        var tag = await _tags.GetByIdAsync(application.TagId, cancellationToken);
        _tags.RemoveEntityTag(application);
        await _activity.WriteAsync(new CreateActivityEventDto(application.EntityType, application.EntityId, ActivityEventTypes.TagRemoved, OldValue: tag?.Name), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(new { entityTagId = id }, "Tag removed."));
    }

    private static EntityTagResponse ToResponse(EntityTag application)
        => new(application.Id, application.EntityType, application.EntityId, application.TagId,
            application.Tag?.Name ?? string.Empty, application.Tag?.Colour, application.Tag?.Category);

    private static EntityTagResponse ToResponse(EntityTag application, Tag tag)
        => new(application.Id, application.EntityType, application.EntityId, application.TagId, tag.Name, tag.Colour, tag.Category);
}
