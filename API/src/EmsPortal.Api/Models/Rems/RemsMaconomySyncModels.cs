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

    public static string StatusOf(Client client, bool stale) => stale
        ? RemsMaconomySyncStatuses.Failed
        : client.MaconomySyncStatus switch
        {
            RemsMaconomyCustomerSyncStatus.Pending => RemsMaconomySyncStatuses.Pending,
            RemsMaconomyCustomerSyncStatus.Syncing => RemsMaconomySyncStatuses.Syncing,
            RemsMaconomyCustomerSyncStatus.Synced => RemsMaconomySyncStatuses.Synced,
            RemsMaconomyCustomerSyncStatus.Linked => RemsMaconomySyncStatuses.Linked,
            RemsMaconomyCustomerSyncStatus.Failed => RemsMaconomySyncStatuses.Failed,
            _ => RemsMaconomySyncStatuses.NotSynced,
        };

    /// <summary>
    /// The sync state as the chip reads it. Attempts are no longer counted; the last attempt is the moment
    /// the client's state last changed while an attempt was pending, running or failed.
    /// </summary>
    public static RemsMaconomySyncView From(Client client, bool stale, bool enabled, bool canManage)
    {
        var status = StatusOf(client, stale);
        var lastAttempt = client.MaconomySyncStatus is RemsMaconomyCustomerSyncStatus.Pending
            or RemsMaconomyCustomerSyncStatus.Syncing or RemsMaconomyCustomerSyncStatus.Failed
            ? client.UpdatedOnUtc
            : client.MaconomySyncedOnUtc;
        return new RemsMaconomySyncView(
            client.Id, status, client.MaconomyCustomerNumber, 0, lastAttempt, client.MaconomySyncedOnUtc,
            stale ? StaleReason : client.MaconomySyncError,
            enabled,
            canManage && enabled && status is RemsMaconomySyncStatuses.NotSynced or RemsMaconomySyncStatuses.Failed);
    }

    /// <summary>
    /// The view for a request, or null when its client is not one that is posted. A client that has been
    /// recorded says what happened; an approved Brand New Client request whose client was never recorded
    /// reads as Not synced, and Sync now records it.
    /// </summary>
    public static RemsMaconomySyncView? For(RemsRequest request, bool stale, bool enabled, bool canManage)
    {
        var client = request.Client;
        if (client is null)
        {
            return null;
        }
        if (client.MaconomySyncStatus != RemsMaconomyCustomerSyncStatus.NotSynced || client.IsMaconomyClient)
        {
            return From(client, stale, enabled, canManage);
        }
        var eligible = string.Equals(request.Type?.Value, RemsRequestTypes.BrandNewClient, StringComparison.Ordinal)
            && string.Equals(request.Status?.Value, RemsRequestStatuses.Approved, StringComparison.Ordinal);
        return eligible
            ? new RemsMaconomySyncView(null, RemsMaconomySyncStatuses.NotSynced, null, 0, null, null, null, enabled, canManage && enabled)
            : null;
    }
}
