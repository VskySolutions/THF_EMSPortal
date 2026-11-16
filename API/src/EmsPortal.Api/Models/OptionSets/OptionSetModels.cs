namespace EmsPortal.Api.Models.OptionSets;

// ---- Requests ----

public sealed class CreateOptionSetRequest
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? ParentSetId { get; set; }
    /// <summary>"AlphabeticalAsc" | "AlphabeticalDesc" | "Custom".</summary>
    public string ItemSortMode { get; set; } = "AlphabeticalAsc";
}

public sealed class UpdateOptionSetRequest
{
    public string Name { get; set; } = string.Empty;
    /// <summary>The list this one depends on; null for none. Changing it clears the values' "valid under".</summary>
    public Guid? ParentSetId { get; set; }
    public string ItemSortMode { get; set; } = "AlphabeticalAsc";
    public bool IsActive { get; set; } = true;
}

public sealed class CreateOptionItemRequest
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>What this value means; shown as its tooltip wherever the value is offered or displayed.</summary>
    public string? Description { get; set; }
    /// <summary>The parent-list values this one is valid under; empty means every parent.</summary>
    public IReadOnlyList<Guid>? ParentItemIds { get; set; }
    public bool IsDefault { get; set; }
    public string? BackgroundColor { get; set; }
    public string? TextColor { get; set; }
    /// <summary>Material icon name shown beside the value, e.g. "o_support_agent".</summary>
    public string? Icon { get; set; }
    public string? MetadataJson { get; set; }

    /// <summary>The number Maconomy files this value under (entity type, industry, template job, department); null for none.</summary>
    public string? MaconomyNumber { get; set; }
}

public sealed class UpdateOptionItemRequest
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>The parent-list values this one is valid under; empty means every parent.</summary>
    public IReadOnlyList<Guid>? ParentItemIds { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public string? BackgroundColor { get; set; }
    public string? TextColor { get; set; }
    public string? Icon { get; set; }
    public string? MetadataJson { get; set; }

    /// <summary>The number Maconomy files this value under (entity type, industry, template job, department); null for none.</summary>
    public string? MaconomyNumber { get; set; }
}

public sealed class ReorderItemsRequest
{
    public IReadOnlyList<Guid> ItemIds { get; set; } = Array.Empty<Guid>();
}

// ---- Responses ----

public sealed record OptionSetSummaryResponse(
    Guid Id,
    Guid? TenantId,
    string Key,
    string Name,
    Guid? ParentSetId,
    string ItemSortMode,
    bool IsSystem,
    /// <summary>The application branches on this list, so no value may be added to or removed from it.</summary>
    bool IsClosed,
    bool IsActive,
    bool IsEditable,
    int ItemCount,
    // *By names are resolved by the controller so the list's audit columns read as people, not guids.
    string? CreatedBy,
    DateTime CreatedOnUtc,
    string? UpdatedBy,
    DateTime UpdatedOnUtc);

public sealed record OptionSetItemResponse(
    Guid Id,
    Guid OptionSetId,
    IReadOnlyList<Guid> ParentItemIds,
    string Value,
    string Label,
    string? Description,
    int SortOrder,
    bool IsDefault,
    bool IsActive,
    bool IsStandard,
    string? BackgroundColor,
    string? TextColor,
    string? Icon,
    /// <summary>True for a value the application writes: it cannot be deleted, hidden or re-coded.</summary>
    bool IsSystem,
    string? MetadataJson,
    string? MaconomyNumber);

public sealed record OptionSetDetailResponse(
    Guid Id,
    Guid? TenantId,
    string Key,
    string Name,
    Guid? ParentSetId,
    string ItemSortMode,
    bool IsSystem,
    bool IsClosed,
    bool IsActive,
    bool IsEditable,
    IReadOnlyList<OptionSetItemResponse> Items,
    RecordAudit Audit);
