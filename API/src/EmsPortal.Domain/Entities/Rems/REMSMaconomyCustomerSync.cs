using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// One row per REMS request: whether its brand-new client has been posted to Maconomy as a customer,
/// and how that went. Recorded when the request is fully approved; nothing else creates one, except
/// Sync now on a request approved before this existed.
/// <para>
/// Approval and the sync are two standalone processes. Approval never waits on this row and never
/// fails because of it; a row that is Failed, or NotSynced because the sync was off, stays so until an
/// admin re-syncs it by hand.
/// </para>
/// </summary>
public class REMSMaconomyCustomerSync : AuditableEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid REMSId { get; set; }

    /// <summary>The client the request named when the row was recorded — the Person posted to Maconomy.</summary>
    public Guid ClientPersonId { get; set; }

    public RemsMaconomyCustomerSyncStatus Status { get; set; }

    /// <summary>
    /// The customer number Maconomy assigned (Synced), or the one the email matched (Linked). The same
    /// number is written onto the Person unless another Person in the tenant already holds it.
    /// </summary>
    public string? CustomerNumber { get; set; }

    public int Attempts { get; set; }

    public DateTime? LastAttemptOnUtc { get; set; }

    public DateTime? SyncedOnUtc { get; set; }

    /// <summary>Why the last attempt failed — Maconomy's own message where it gave one.</summary>
    public string? FailureReason { get; set; }

    /// <summary>What was sent on the last attempt, as JSON, for audit.</summary>
    public string? PayloadJson { get; set; }

    // ---- Navigations ----
    public REMS? Rems { get; set; }
    public Person? ClientPerson { get; set; }
}
