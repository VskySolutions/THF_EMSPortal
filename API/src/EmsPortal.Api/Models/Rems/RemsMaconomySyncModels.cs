using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Api.Models.Rems;

/// <summary>The sync status words the UI switches on.</summary>
public static class RemsMaconomySyncStatuses
{
    public const string NotSynced = "notSynced";
    public const string Pending = "pending";
    public const string Syncing = "syncing";
    public const string Synced = "synced";
    public const string Linked = "linked";
    public const string Failed = "failed";
}

/// <summary>
/// Where posting the request's client to Maconomy has got to. Present on a request whose client is
/// posted (a Brand New Client request, once approved); null on every other request.
/// </summary>
public sealed record RemsMaconomySyncView(
    /// <summary>Null when the request is eligible but nothing has recorded a row yet.</summary>
    Guid? Id,
    string Status,
    string? CustomerNumber,
    int Attempts,
    DateTime? LastAttemptOnUtc,
    DateTime? SyncedOnUtc,
    string? FailureReason,
    /// <summary>Whether the tenant's customer sync is switched on.</summary>
    bool SyncEnabled,
    /// <summary>Whether THIS caller may press Sync now on it.</summary>
    bool CanSyncNow);

/// <summary>The settings page's Customer sync card.</summary>
public sealed record MaconomyCustomerSyncOverviewResponse(
    bool Connected,
    bool Enabled,
    DateTime? ChangedOnUtc,
    string? ChangedByName,
    int Synced,
    int Linked,
    int Failed,
    int NotSynced,
    int Pending);

/// <summary>One row of the settings page's unsynced list.</summary>
public sealed record MaconomyUnsyncedRow(
    Guid SyncId,
    Guid RemsId,
    string RemsNumber,
    string ClientName,
    string? ClientEmail,
    string Status,
    string? FailureReason,
    int Attempts,
    DateTime? LastAttemptOnUtc,
    DateTime RecordedOnUtc);

public static class RemsMaconomySyncViews
{
    public const string StaleReason = "The portal lost track of this attempt. Sync now to try again.";

    public static string StatusOf(REMSMaconomyCustomerSync sync, bool stale) => stale
        ? RemsMaconomySyncStatuses.Failed
        : sync.Status switch
        {
            RemsMaconomyCustomerSyncStatus.Pending => RemsMaconomySyncStatuses.Pending,
            RemsMaconomyCustomerSyncStatus.Syncing => RemsMaconomySyncStatuses.Syncing,
            RemsMaconomyCustomerSyncStatus.Synced => RemsMaconomySyncStatuses.Synced,
            RemsMaconomyCustomerSyncStatus.Linked => RemsMaconomySyncStatuses.Linked,
            RemsMaconomyCustomerSyncStatus.Failed => RemsMaconomySyncStatuses.Failed,
            _ => RemsMaconomySyncStatuses.NotSynced,
        };

    public static RemsMaconomySyncView From(REMSMaconomyCustomerSync sync, bool stale, bool enabled, bool canManage)
    {
        var status = StatusOf(sync, stale);
        return new RemsMaconomySyncView(
            sync.Id, status, sync.CustomerNumber, sync.Attempts, sync.LastAttemptOnUtc, sync.SyncedOnUtc,
            stale ? StaleReason : sync.FailureReason,
            enabled,
            canManage && enabled && status is RemsMaconomySyncStatuses.NotSynced or RemsMaconomySyncStatuses.Failed);
    }

    /// <summary>
    /// The view for a request, or null when its client is not one that is posted. A row says what
    /// happened; an approved Brand New Client request with no row (approved before the sync existed)
    /// reads as Not synced, and Sync now makes its row.
    /// </summary>
    public static RemsMaconomySyncView? For(REMS rems, REMSMaconomyCustomerSync? sync, bool stale, bool enabled, bool canManage)
    {
        if (sync is not null)
        {
            return From(sync, stale, enabled, canManage);
        }
        var eligible = string.Equals(rems.Type?.Value, RemsRequestTypes.BrandNewClient, StringComparison.Ordinal)
            && string.Equals(rems.Status?.Value, RemsRequestStatuses.Approved, StringComparison.Ordinal)
            && rems.ClientPerson is { IsMaconomyClient: false };
        return eligible
            ? new RemsMaconomySyncView(null, RemsMaconomySyncStatuses.NotSynced, null, 0, null, null, null, enabled, canManage && enabled)
            : null;
    }
}
