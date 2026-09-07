using EmsPortal.Application.Abstractions.OptionSets;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Api.Models.Rems;

/// <summary>
/// Resolving a REMS option CODE to the item id a row stores, in the two shapes the feature writes
/// them.
/// </summary>
public static class RemsOptionCodeExtensions
{
    /// <summary>The item id for a REMS code, or null when the code is null/blank or the list has no such value.</summary>
    public static Task<Guid?> RemsIdAsync(
        this IOptionCodeResolver codes, string setKey, string? code, CancellationToken cancellationToken = default)
        => codes.IdOfAsync(EntityType.Rems, setKey, code, cancellationToken);

    /// <summary>
    /// The item id for a REMS code the application itself sets — a status transition, the type a
    /// request is filed under.
    /// </summary>
    public static async Task<Guid> RequireRemsIdAsync(
        this IOptionCodeResolver codes, string setKey, string code, CancellationToken cancellationToken = default)
        => await codes.IdOfAsync(EntityType.Rems, setKey, code, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The '{setKey}' option list has no value '{code}'. It is a value the application writes, so "
                + "it should be present and locked — check the list in Administration → Option Sets.");
}
