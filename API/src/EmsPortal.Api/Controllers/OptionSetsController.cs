using EmsPortal.Api.Models;
using EmsPortal.Api.Models.OptionSets;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.OptionSets;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Manage tenant-configurable option lists (e.g. Payment Terms) and their values. Reads require
/// <c>optionSets.read</c>.
/// </summary>
[ApiController]
[Authorize]
[Route("/api/option-sets")]
[Produces("application/json")]
[Tags("Option Sets")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
public sealed class OptionSetsController : ControllerBase
{
    private readonly IOptionSetService _service;
    private readonly IOptionSetRepository _sets;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenantContext;

    public OptionSetsController(
        IOptionSetService service, IOptionSetRepository sets, IUserRepository users, ITenantContext tenantContext)
    {
        _service = service;
        _sets = sets;
        _users = users;
        _tenantContext = tenantContext;
    }

    private Guid? ScopeTenantId => _tenantContext.IsResolved ? _tenantContext.TenantId : null;

    /// <summary>What the Option Lists list may be ordered by.</summary>
    private static readonly SortMap<OptionSetSummaryResponse> ListSorts =
        new SortMap<OptionSetSummaryResponse>("updatedOnUtc")
            .Add("name", s => s.Name)
            .Add("entityType", s => s.EntityType, s => s.Name)
            .Add("itemCount", s => s.ItemCount, s => s.Name)
            .Add("itemSortMode", s => s.ItemSortMode, s => s.Name)
            .Add("origin", s => s.IsSystem, s => s.Name)
            .Add("isActive", s => s.IsActive, s => s.Name)
            .Add("createdOnUtc", s => s.CreatedOnUtc)
            .Add("updatedOnUtc", s => s.UpdatedOnUtc);

    [HttpGet]
    [RequirePermission(Permissions.OptionSetsRead)]
    [ProducesResponseType<ApiResponse<IEnumerable<OptionSetSummaryResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? entityType,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var filter = entityType is { } et ? (EntityType)et : (EntityType?)null;
        var sets = await _sets.ListSetsForScopeAsync(ScopeTenantId, filter, cancellationToken);

        // A tenant with its own copy of a standard list sees only that copy.
        var owned = sets.Where(s => s.TenantId is not null).Select(s => (s.EntityType, s.Key)).ToHashSet();
        var visible = sets.Where(s => s.TenantId is not null || !owned.Contains((s.EntityType, s.Key))).ToList();

        var counts = await _sets.CountItemsAsync(visible.Select(s => s.Id).ToList(), cancellationToken);

        // One name lookup for the page, so the audit columns read as people rather than guids.
        var names = await _users.GetFullNamesAsync(
            visible.SelectMany(s => new[] { s.CreatedById, s.UpdatedById })
                .Where(id => id.HasValue).Select(id => id!.Value),
            cancellationToken);
        string? NameOf(Guid? id) => id is { } uid && names.TryGetValue(uid, out var n) ? n : null;

        // Ordered after projecting: Values is a count assembled here, and Type reads off a flag.
        var summaries = ListSorts
            .Apply(visible.Select(s => ToSummary(s, counts.TryGetValue(s.Id, out var c) ? c : 0, NameOf)), sortBy, descending)
            .ToList();
        return Ok(ApiResponseFactory.Success<IEnumerable<OptionSetSummaryResponse>>(summaries, "Option lists retrieved."));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.OptionSetsRead)]
    [ProducesResponseType<ApiResponse<OptionSetDetailResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var set = await _sets.GetSetWithItemsAsync(id, ScopeTenantId, cancellationToken);
        return set is null
            ? NotFound(ApiResponseFactory.NotFound("Option list not found."))
            : Ok(ApiResponseFactory.Success(await ToDetailAsync(set, cancellationToken), "Option list retrieved."));
    }

    /// <summary>
    /// Effective active values for a key — the tenant's own list when present, else the standard
    /// one.
    /// </summary>
    [HttpGet("resolve")]
    [Authorize]
    [ProducesResponseType<ApiResponse<IEnumerable<OptionSetItemResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Resolve(
        [FromQuery] int entityType,
        [FromQuery] string key,
        [FromQuery] Guid? parentItemId,
        CancellationToken cancellationToken)
    {
        var set = await _sets.GetEffectiveSetAsync(ScopeTenantId, (EntityType)entityType, key?.Trim() ?? string.Empty, cancellationToken);
        if (set is null)
        {
            return Ok(ApiResponseFactory.Success<IEnumerable<OptionSetItemResponse>>(Array.Empty<OptionSetItemResponse>(), "No options found."));
        }

        var items = OrderItems(set)
            .Where(i => i.IsActive)
            .Where(i => parentItemId is null || i.ParentItemId == parentItemId)
            .Select(ToItem)
            .ToList();

        return Ok(ApiResponseFactory.Success<IEnumerable<OptionSetItemResponse>>(items, "Options resolved."));
    }

    [HttpPost]
    [RequirePermission(Permissions.OptionSetsManage)]
    [ProducesResponseType<ApiResponse<OptionSetDetailResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateOptionSetRequest body, CancellationToken cancellationToken)
    {
        var input = new CreateOptionSetInput(
            (EntityType)body.EntityType,
            body.Key,
            body.Name,
            body.ParentSetId,
            ParseSortMode(body.ItemSortMode));

        try
        {
            var set = await _service.CreateSetAsync(input, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = set.Id }, ApiResponseFactory.Success(await ToDetailAsync(set, cancellationToken), "Option list created."));
        }
        catch (OptionSetException ex)
        {
            return MapError(ex);
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.OptionSetsManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOptionSetRequest body, CancellationToken cancellationToken)
    {
        var input = new UpdateOptionSetInput(body.Name, ParseSortMode(body.ItemSortMode), body.IsActive);
        try
        {
            var set = await _service.UpdateSetAsync(id, input, cancellationToken);
            return set is null
                ? NotFound(ApiResponseFactory.NotFound("Option list not found."))
                : Ok(ApiResponseFactory.Success(await ToDetailAsync(set, cancellationToken), "Option list updated."));
        }
        catch (OptionSetException ex)
        {
            return MapError(ex);
        }
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.OptionSetsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _service.DeleteSetAsync(id, cancellationToken);
            return deleted is null
                ? NotFound(ApiResponseFactory.NotFound("Option list not found."))
                : Ok(ApiResponseFactory.Success(new { id }, "Option list deleted."));
        }
        catch (OptionSetException ex)
        {
            return MapError(ex);
        }
    }

    [HttpPost("{id:guid}/items")]
    [RequirePermission(Permissions.OptionSetsManage)]
    [ProducesResponseType<ApiResponse<OptionSetItemResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateItem(Guid id, [FromBody] CreateOptionItemRequest body, CancellationToken cancellationToken)
    {
        var input = new CreateOptionItemInput(
            id, body.Value, body.Label, body.Description, body.ParentItemId, body.IsDefault,
            body.BackgroundColor, body.TextColor, body.Icon, body.MetadataJson);
        try
        {
            var item = await _service.CreateItemAsync(input, cancellationToken);
            return item is null
                ? NotFound(ApiResponseFactory.NotFound("Option list not found."))
                : CreatedAtAction(nameof(Get), new { id }, ApiResponseFactory.Success(ToItem(item), "Option value created."));
        }
        catch (OptionSetException ex)
        {
            return MapError(ex);
        }
    }

    [HttpPut("{id:guid}/items/{itemId:guid}")]
    [RequirePermission(Permissions.OptionSetsManage)]
    public async Task<IActionResult> UpdateItem(Guid id, Guid itemId, [FromBody] UpdateOptionItemRequest body, CancellationToken cancellationToken)
    {
        var input = new UpdateOptionItemInput(
            body.Value, body.Label, body.Description, body.ParentItemId, body.IsDefault, body.IsActive,
            body.BackgroundColor, body.TextColor, body.Icon, body.MetadataJson);
        try
        {
            var item = await _service.UpdateItemAsync(id, itemId, input, cancellationToken);
            return item is null
                ? NotFound(ApiResponseFactory.NotFound("Option value not found."))
                : Ok(ApiResponseFactory.Success(ToItem(item), "Option value updated."));
        }
        catch (OptionSetException ex)
        {
            return MapError(ex);
        }
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    [RequirePermission(Permissions.OptionSetsManage)]
    public async Task<IActionResult> DeleteItem(Guid id, Guid itemId, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _service.DeleteItemAsync(id, itemId, cancellationToken);
            return deleted is null
                ? NotFound(ApiResponseFactory.NotFound("Option value not found."))
                : Ok(ApiResponseFactory.Success(new { id = itemId }, "Option value deleted."));
        }
        catch (OptionSetException ex)
        {
            return MapError(ex);
        }
    }

    [HttpPut("{id:guid}/items/reorder")]
    [RequirePermission(Permissions.OptionSetsManage)]
    public async Task<IActionResult> Reorder(Guid id, [FromBody] ReorderItemsRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var done = await _service.ReorderItemsAsync(id, body.ItemIds, cancellationToken);
            return done is null
                ? NotFound(ApiResponseFactory.NotFound("Option list not found."))
                : Ok(ApiResponseFactory.Success(new { id }, "Order saved."));
        }
        catch (OptionSetException ex)
        {
            return MapError(ex);
        }
    }

    // ---- mapping helpers ----

    private static OptionItemSortMode ParseSortMode(string? value)
        => Enum.TryParse<OptionItemSortMode>(value, ignoreCase: true, out var mode) ? mode : OptionItemSortMode.Custom;

    private static IEnumerable<OptionSetItem> OrderItems(OptionSet set) => set.ItemSortMode switch
    {
        OptionItemSortMode.AlphabeticalAsc => set.Items.OrderBy(i => i.Label),
        OptionItemSortMode.AlphabeticalDesc => set.Items.OrderByDescending(i => i.Label),
        _ => set.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Label),
    };

    /// <summary>
    /// Every list a caller can see is manageable — a standard list is a starting point, not a fixed
    /// one, so its values can be added, renamed, re-ordered and removed.
    /// </summary>
    private static bool IsEditable(OptionSet set) => true;

    private static OptionSetSummaryResponse ToSummary(OptionSet s, int itemCount, Func<Guid?, string?> nameOf) => new(
        s.Id, s.TenantId, (int)s.EntityType, s.Key, s.Name, s.ParentSetId,
        s.ItemSortMode.ToString(), s.IsSystem, s.IsClosed, s.IsActive, IsEditable(s), itemCount,
        nameOf(s.CreatedById), s.CreatedOnUtc, nameOf(s.UpdatedById), s.UpdatedOnUtc);

    /// <summary>The list plus its provenance block, which the detail page ends with.</summary>
    private async Task<OptionSetDetailResponse> ToDetailAsync(OptionSet s, CancellationToken cancellationToken) => new(
        s.Id, s.TenantId, (int)s.EntityType, s.Key, s.Name, s.ParentSetId,
        s.ItemSortMode.ToString(), s.IsSystem, s.IsClosed, s.IsActive, IsEditable(s),
        OrderItems(s).Select(ToItem).ToList(),
        await RecordAudit.ForAsync(_users, s, cancellationToken));

    private static OptionSetItemResponse ToItem(OptionSetItem i) => new(
        i.Id, i.OptionSetId, i.ParentItemId, i.Value, i.Label, i.Description, i.SortOrder,
        i.IsDefault, i.IsActive, i.TenantId is null, i.BackgroundColor, i.TextColor, i.Icon, i.IsSystem,
        i.MetadataJson);

    private IActionResult MapError(OptionSetException ex) => ex.Code switch
    {
        OptionSetErrorCodes.DuplicateKey or OptionSetErrorCodes.DuplicateValue
            => Conflict(ApiResponseFactory.Error(ApiErrorCodes.DuplicateIdentifier, ex.Message, ex.Code)),
        OptionSetErrorCodes.ReadOnlyStandardSet or OptionSetErrorCodes.NoActiveTenant
            => StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden(ex.Message)),
        // A closed list and a system value are not the caller doing anything wrong — they are asking for
        // something the application does not allow of itself, which is a 409 rather than a bad request.
        OptionSetErrorCodes.ClosedSet or OptionSetErrorCodes.SystemItem
            => Conflict(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, ex.Message, ex.Code)),
        _ => BadRequest(ApiResponseFactory.Error(ApiErrorCodes.ValidationFailed, ex.Message, ex.Code)),
    };
}
