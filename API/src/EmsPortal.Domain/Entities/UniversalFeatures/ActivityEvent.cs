using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// An append-only, user-facing event in a record's activity timeline. Written in-process by
/// <c>ActivityEventWriter</c> whenever a module performs a tracked action (Universal Features ADR-002).
/// Write-once: never updated or deleted via any API. <see cref="AuditableEntity.CreatedOnUtc"/> is the
/// event timestamp and <see cref="AuditableEntity.CreatedById"/> mirrors <see cref="ActorId"/>.
/// </summary>
public class ActivityEvent : AuditableEntity
{
    public Guid Id { get; set; }

    /// <summary>Owning tenant (tenant-scoped).</summary>
    public Guid TenantId { get; set; }

    /// <summary>The kind of entity this event is attached to.</summary>
    public EntityType EntityType { get; set; }

    /// <summary>The id of the entity this event is attached to.</summary>
    public Guid EntityId { get; set; }

    /// <summary>The event kind — see <see cref="ActivityEventTypes"/> (open set, stored as text).</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The user who caused the event; null for system-generated events.</summary>
    public Guid? ActorId { get; set; }

    /// <summary>Previous value (for change-style events); null when not applicable.</summary>
    public string? OldValue { get; set; }

    /// <summary>New value (for change-style events); null when not applicable.</summary>
    public string? NewValue { get; set; }
}

/// <summary>The well-known activity event type keys (open set — modules may add new values).</summary>
public static class ActivityEventTypes
{
    public const string StatusChanged = "StatusChanged";
    public const string FieldEdited = "FieldEdited";
    public const string ConversationMessageAdded = "ConversationMessageAdded";
    public const string TagApplied = "TagApplied";
    public const string TagRemoved = "TagRemoved";
    public const string AttachmentUploaded = "AttachmentUploaded";
    public const string AttachmentDeleted = "AttachmentDeleted";
    public const string ChecklistItemCompleted = "ChecklistItemCompleted";
    public const string Restored = "Restored";

    // ---- REMS request lifecycle (WO-111) ----
    public const string RemsCreated = "RemsCreated";
    public const string RemsAssigned = "RemsAssigned";
    public const string RemsDeleted = "RemsDeleted";

    // What takes a request out of draft is the initiator sending the intake link, which RemsFormSent
    // records — there is no separate "submitted" event. Rows written under retired type names are left as
    // they are; the timeline renders an unmapped type by its own name.

    /// <summary>The Admin returned a request to its initiator for engagement-setup rework, with a reason.</summary>
    public const string RemsSentBack = "RemsSentBack";

    /// <summary>The initiator handed the revised setup back to the Admin to confirm.</summary>
    public const string RemsReturnedToAdmin = "RemsReturnedToAdmin";

    // ---- REMS EMS form lifecycle (WO-112) ----
    public const string RemsFormBuilt = "RemsFormBuilt";
    public const string RemsFormSent = "RemsFormSent";

    /// <summary>A client who had not submitted yet was chased. Repeatable — one row per reminder.</summary>
    public const string RemsFormReminderSent = "RemsFormReminderSent";

    // ---- REMS public client form (WO-113) ----
    public const string RemsFormSubmitted = "RemsFormSubmitted";

    /// <summary>
    /// An Admin corrected what the client submitted. The correction overwrites the snapshot, so this row
    /// is the timeline's record that the answers on file are no longer only the client's own.
    /// </summary>
    public const string RemsFormCorrected = "RemsFormCorrected";

    /// <summary>
    /// Somebody moved a related client along on the Related Entities list — a spouse, a child, another
    /// business the client named. Written on the PARENT request, because that is the only record these
    /// rows have a timeline on, and it carries the old and new codes: the status moves only by hand, so
    /// who changed it and to what is the whole audit of it.
    /// </summary>
    public const string RemsRelatedEntityStatusChanged = "RemsRelatedEntityStatusChanged";

    // ---- REMS engagement workspace + approval (WO-114) ----
    public const string RemsEngagementUpdated = "RemsEngagementUpdated";
    public const string RemsApprovalSent = "RemsApprovalSent";
    public const string RemsApprovalResubmitted = "RemsApprovalResubmitted";
    public const string RemsApproved = "RemsApproved";
    public const string RemsRejected = "RemsRejected";
    public const string RemsFullyApproved = "RemsFullyApproved";
}
