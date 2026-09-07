using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Api.Models;

/// <summary>Turns the actor an audit entry records into something a person can read.</summary>
public static class AuditActorNames
{
    /// <summary>The actor recorded when nobody was signed in — see HttpContextActorAccessor.</summary>
    private const string SystemIdentity = "system";

    /// <summary>One lookup for a page of entries, returning the function that names each actor.</summary>
    public static async Task<Func<string?, string?>> ResolverAsync(
        IUserRepository users, IEnumerable<AuditTrailEntry> entries, CancellationToken cancellationToken)
    {
        var ids = entries
            .Select(e => e.PerformedBy)
            .Where(actor => Guid.TryParse(actor, out _))
            .Select(actor => Guid.Parse(actor!))
            .Distinct()
            .ToList();

        var names = ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await users.GetFullNamesAsync(ids, cancellationToken);

        return actor =>
        {
            if (string.IsNullOrWhiteSpace(actor))
            {
                return null;
            }
            if (string.Equals(actor, SystemIdentity, StringComparison.OrdinalIgnoreCase))
            {
                return "System";
            }
            return Guid.TryParse(actor, out var id) && names.TryGetValue(id, out var name) ? name : actor;
        };
    }
}
