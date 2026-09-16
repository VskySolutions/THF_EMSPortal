using EmsPortal.Domain.Entities;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

/// <summary>
/// The request-status filter every REMS list shares, in the terms the status column shows: a request
/// with the admins that nobody holds reads "Waiting for pickup" whatever its stored status, so that is a
/// value of its own here, and the two admin stages match only the rows an admin actually holds.
/// </summary>
internal static class RemsRequestFilters
{
    public static IQueryable<REMS> WhereStatus(IQueryable<REMS> query, string code)
    {
        var wanted = code.Trim();
        if (wanted.Equals(RemsRequestStatuses.WaitingForPickup, StringComparison.OrdinalIgnoreCase))
        {
            return query.Where(r => r.AdminAssignedToId == null
                && (r.Status!.Value == RemsRequestStatuses.AdminReview
                    || r.Status!.Value == RemsRequestStatuses.AwaitingAdminConfirmation));
        }

        return RemsRequestStatuses.IsWithAdmin(wanted)
            ? query.Where(r => r.Status!.Value == wanted && r.AdminAssignedToId != null)
            : query.Where(r => r.Status!.Value == wanted);
    }
}
