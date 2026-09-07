using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// A single approver's task within a <see cref="REMSApprovalRound"/> (WO-110). Each approver acts in a
/// specific <see cref="ApproverRole"/> and works through an optional checklist before deciding.
/// </summary>
public class REMSApprovalTask : AuditableEntity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant (tenant-scoped).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Owning approval round.</summary>
    public Guid REMSApprovalRoundId { get; set; }

    /// <summary>The approver (User).</summary>
    public Guid ApproverId { get; set; }

    /// <summary>The role the approver acts in.</summary>
    public RemsApproverRole ApproverRole { get; set; }

    /// <summary>Task decision state.</summary>
    public RemsApprovalTaskStatus Status { get; set; }

    /// <summary>When the approver decided.</summary>
    public DateTime? DecidedOnUtc { get; set; }

    /// <summary>Reason captured when the task is rejected.</summary>
    public string? RejectionReason { get; set; }

    // ---- Navigations ----
    public REMSApprovalRound? Round { get; set; }
    public ICollection<REMSApprovalChecklistItem> ChecklistItems { get; set; } = new List<REMSApprovalChecklistItem>();
}
