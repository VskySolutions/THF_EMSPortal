using EmsPortal.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Security;

/// <summary>Resolves the "acting as" claim a delegate sends with a request, and decides whether they may.</summary>
public static class RemsActingAs
{
    public const string HeaderName = "X-Rems-On-Behalf-Of";

    /// <summary>What a delegate may do in the seat they claimed, or null when they are acting as themselves.</summary>
    public sealed record Seat(Guid PrincipalUserId, bool CanPrepare, bool CanSend);

    /// <summary>
    /// The principal this call is being made for, or null when the caller is acting as themselves —
    /// which covers no header, an unparseable one, the caller's own id.
    /// </summary>
    public static async Task<Seat?> ResolveAsync(
        ControllerBase controller,
        IRemsDelegationRepository delegations,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        if (!controller.Request.Headers.TryGetValue(HeaderName, out var raw)
            || !Guid.TryParse(raw.ToString(), out var principalId)
            || principalId == callerId)
        {
            return null;
        }

        var grant = await delegations.GetAsync(principalId, callerId, cancellationToken);
        if (grant is null || !grant.IsActiveOn(DateOnly.FromDateTime(DateTime.UtcNow)))
        {
            return null;
        }

        return new Seat(principalId, grant.CanPrepare, grant.CanSend);
    }
}
