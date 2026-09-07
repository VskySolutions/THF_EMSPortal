using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.OptionSets;

/// <summary>Manages tenant-configurable option lists and their values.</summary>
public interface IOptionSetService
{
    Task<OptionSet> CreateSetAsync(CreateOptionSetInput input, CancellationToken cancellationToken = default);
    Task<OptionSet?> UpdateSetAsync(Guid id, UpdateOptionSetInput input, CancellationToken cancellationToken = default);
    Task<bool?> DeleteSetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<OptionSetItem?> CreateItemAsync(CreateOptionItemInput input, CancellationToken cancellationToken = default);
    Task<OptionSetItem?> UpdateItemAsync(Guid setId, Guid itemId, UpdateOptionItemInput input, CancellationToken cancellationToken = default);
    Task<bool?> DeleteItemAsync(Guid setId, Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Rewrites each item's sort order to its position in <paramref name="orderedItemIds"/>.</summary>
    Task<bool?> ReorderItemsAsync(Guid setId, IReadOnlyList<Guid> orderedItemIds, CancellationToken cancellationToken = default);
}

public sealed record CreateOptionSetInput(
    EntityType EntityType,
    string Key,
    string Name,
    Guid? ParentSetId,
    OptionItemSortMode ItemSortMode);

public sealed record UpdateOptionSetInput(
    string Name,
    OptionItemSortMode ItemSortMode,
    bool IsActive);

public sealed record CreateOptionItemInput(
    Guid OptionSetId,
    string Value,
    string Label,
    string? Description,
    Guid? ParentItemId,
    bool IsDefault,
    string? BackgroundColor,
    string? TextColor,
    string? Icon,
    string? MetadataJson);

public sealed record UpdateOptionItemInput(
    string Value,
    string Label,
    string? Description,
    Guid? ParentItemId,
    bool IsDefault,
    bool IsActive,
    string? BackgroundColor,
    string? TextColor,
    string? Icon,
    string? MetadataJson);

/// <summary>Stable error codes raised by <see cref="IOptionSetService"/> for the API to map to HTTP.</summary>
public static class OptionSetErrorCodes
{
    public const string DuplicateKey = "OPTION_SET_DUPLICATE_KEY";
    public const string DuplicateValue = "OPTION_SET_DUPLICATE_VALUE";
    public const string ReadOnlyStandardSet = "OPTION_SET_READ_ONLY";
    public const string NoActiveTenant = "OPTION_SET_NO_TENANT";
    public const string InvalidReorder = "OPTION_SET_INVALID_REORDER";

    /// <summary>The list is one the application branches on: no value may be added to or removed from it.</summary>
    public const string ClosedSet = "OPTION_SET_CLOSED";

    /// <summary>The value itself is one the application writes and reads back — see OptionSetItem.IsSystem.</summary>
    public const string SystemItem = "OPTION_SET_SYSTEM_ITEM";
}

/// <summary>Business-rule violation while managing option sets; carries a stable <see cref="Code"/>.</summary>
public sealed class OptionSetException : Exception
{
    public OptionSetException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}
