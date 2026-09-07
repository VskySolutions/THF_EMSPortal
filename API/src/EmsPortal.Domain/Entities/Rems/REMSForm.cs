using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// The customer-facing onboarding form for a <see cref="REMS"/> request (WO-110). One active form per
/// request. <see cref="InviteCode"/> backs the public link and is immutable once the form is sent.
/// <see cref="EntityTypeId"/> references the <c>REMS.EntityType</c> option-set item.
/// </summary>
public class REMSForm : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant (tenant-scoped).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Owning REMS request.</summary>
    public Guid REMSId { get; set; }

    /// <summary>
    /// What kind of entity the client is — shown as "Entity Type" — as a foreign key to the
    /// <c>REMS.EntityType</c> option-set item. Required, and frozen once the form is sent: the CODE it
    /// resolves to decides which questions the client is asked (RemsFormPayloadValidator).
    /// </summary>
    public Guid EntityTypeId { get; set; }

    /// <summary>Public invite code, unique per tenant; immutable after send.</summary>
    public string InviteCode { get; set; } = string.Empty;

    /// <summary>Form lifecycle status.</summary>
    public RemsFormStatus Status { get; set; }

    /// <summary>Staff user who created the form.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>When the form was emailed to the customer.</summary>
    public DateTime? SentOnUtc { get; set; }

    /// <summary>When the customer submitted the form.</summary>
    public DateTime? SubmittedOnUtc { get; set; }

    /// <summary>When the invite code was locked (on send).</summary>
    public DateTime? InviteLockedOnUtc { get; set; }

    // ---- Navigations ----
    public REMS? Rems { get; set; }
    public OptionSetItem? EntityType { get; set; }
    public ICollection<REMSFormDraft> Drafts { get; set; } = new List<REMSFormDraft>();
    public ICollection<REMSFormSubmission> Submissions { get; set; } = new List<REMSFormSubmission>();
    public ICollection<REMSFormEmailEvent> EmailEvents { get; set; } = new List<REMSFormEmailEvent>();
}
