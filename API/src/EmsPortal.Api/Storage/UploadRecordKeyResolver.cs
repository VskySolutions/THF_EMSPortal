using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Api.Storage;

/// <summary>Names the folder a record owns in the upload tree.</summary>
public interface IUploadRecordKeyResolver
{
    /// <summary>
    /// The record's folder name, or null when the record does not exist — which is what stops an
    /// upload being filed against an id the caller made up.
    /// </summary>
    Task<string?> ResolveAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IUploadRecordKeyResolver" />
/// <remarks>
/// Existence is only verified for the types with a number to look up. The rest resolve to their id
/// without a round trip: those uploads are already gated by the caller's read permission on the
/// entity type, and a bad id costs an empty folder rather than a wrong one.
/// </remarks>
public sealed class UploadRecordKeyResolver : IUploadRecordKeyResolver
{
    private readonly IRemsRepository _rems;
    private readonly IPersonRepository _persons;

    public UploadRecordKeyResolver(IRemsRepository rems, IPersonRepository persons)
    {
        _rems = rems;
        _persons = persons;
    }

    public async Task<string?> ResolveAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        if (entityId == Guid.Empty)
        {
            return null;
        }

        return entityType switch
        {
            // A request raised before numbering, or a person seeded without a code, still needs a
            // folder — the id stands in rather than the upload failing.
            EntityType.Rems => Fallback((await _rems.GetByIdAsync(entityId, cancellationToken))?.REMSNumber, entityId),
            EntityType.Person or EntityType.Client => Fallback((await _persons.GetByIdAsync(entityId, cancellationToken))?.PersonCode, entityId),
            _ => entityId.ToString("N"),
        };
    }

    private static string? Fallback(string? key, Guid entityId)
        => key is null ? null : string.IsNullOrWhiteSpace(key) ? entityId.ToString("N") : key;
}
