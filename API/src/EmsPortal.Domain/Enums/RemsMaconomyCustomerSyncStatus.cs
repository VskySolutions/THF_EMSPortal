namespace EmsPortal.Domain.Enums;

/// <summary>
/// Where posting a request's brand-new client to Maconomy has got to. Stored as a string
/// (<c>HasConversion&lt;string&gt;</c>), like <see cref="RemsAddressType"/>.
/// </summary>
public enum RemsMaconomyCustomerSyncStatus
{
    /// <summary>The request was approved while the customer sync was switched off. Nothing was posted; an admin may Sync now.</summary>
    NotSynced,

    /// <summary>Queued for the Workers process.</summary>
    Pending,

    /// <summary>
    /// An attempt is under way. Set BEFORE the create is sent, so a crash between the create and the
    /// record leaves a trace: the next attempt's email check finds the customer just made and links it.
    /// </summary>
    Syncing,

    /// <summary>Maconomy created the customer; the number it assigned is on the row and on the client.</summary>
    Synced,

    /// <summary>A customer with the client's email already existed, so none was created; the request was linked to it.</summary>
    Linked,

    /// <summary>The one attempt failed, with the reason on the row. It stays Failed until an admin presses Sync now.</summary>
    Failed,
}
