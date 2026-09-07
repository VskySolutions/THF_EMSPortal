using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Api.Models;

/// <summary>
/// The provenance block every detail page ends with: who made the record and when, who last touched it
/// and when, and — once it is deleted — who deleted it and when.
/// </summary>
public sealed record RecordAudit(
    string? CreatedBy,
    DateTime CreatedOnUtc,
    string? UpdatedBy,
    DateTime UpdatedOnUtc,
    bool Deleted,
    string? DeletedBy,
    DateTime? DeletedOnUtc)
{
    /// <summary>The block for one record, from a name lookup the caller has already made.</summary>
    public static RecordAudit From(AuditableEntity entity, Func<Guid?, string?> nameOf) => new(
        Actor(entity.CreatedById, nameOf),
        entity.CreatedOnUtc,
        Actor(entity.UpdatedById, nameOf),
        entity.UpdatedOnUtc,
        entity.Deleted,
        entity.Deleted ? Actor(entity.UpdatedById, nameOf) : null,
        entity.Deleted ? entity.DeletedOnUtc : null);

    /// <summary>The actor as a person reads it.</summary>
    private static string? Actor(Guid? id, Func<Guid?, string?> nameOf)
        => id is null ? "System" : nameOf(id);

    /// <summary>The same block, doing its own lookup — for a response with no other actor to resolve.</summary>
    public static async Task<RecordAudit> ForAsync(
        IUserRepository users, AuditableEntity entity, CancellationToken cancellationToken)
    {
        var ids = new[] { entity.CreatedById, entity.UpdatedById }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var names = ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await users.GetFullNamesAsync(ids, cancellationToken);

        return From(entity, Names(names));
    }

    /// <summary>Turns a name lookup into the <c>nameOf</c> the two above take.</summary>
    public static Func<Guid?, string?> Names(IReadOnlyDictionary<Guid, string> names)
        => id => id is { } userId && names.TryGetValue(userId, out var name) ? name : null;
}
