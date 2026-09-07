using EmsPortal.Domain.Enums;

namespace EmsPortal.Api.Models.UniversalFeatures;

/// <summary>Request to create a tenant tag.</summary>
public sealed class CreateTagRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Colour { get; set; }
    public string? Category { get; set; }
}

/// <summary>Request to update a tenant tag.</summary>
public sealed class UpdateTagRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Colour { get; set; }
    public string? Category { get; set; }
}

/// <summary>A tag as returned to the client, with usage count.</summary>
public sealed record TagResponse(
    Guid Id,
    string Name,
    string? Colour,
    string? Category,
    int UsageCount,
    string? CreatedBy = null,
    DateTime? CreatedOnUtc = null,
    string? UpdatedBy = null,
    DateTime? UpdatedOnUtc = null);

/// <summary>Request to apply a tag to an entity record.</summary>
public sealed class ApplyTagRequest
{
    public EntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public Guid TagId { get; set; }
}

/// <summary>A tag applied to an entity record.</summary>
public sealed record EntityTagResponse(
    Guid Id, EntityType EntityType, Guid EntityId, Guid TagId, string TagName, string? Colour, string? Category);
