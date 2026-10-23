using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Persistence;



/// <summary>Rows per status. <c>Stale</c> is the Syncing rows older than the cutoff, counted apart from the live ones.</summary>
public sealed record RemsMaconomyCustomerSyncCounts(
    int Synced, int Linked, int Failed, int NotSynced, int Pending, int Syncing, int Stale)
{
    public static readonly RemsMaconomyCustomerSyncCounts Empty = new(0, 0, 0, 0, 0, 0, 0);
}
