using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// One approver's decision on one routing of a request. <see cref="RoundNumber"/> groups the tasks of a
/// routing; the round's outcome is derived from its tasks, and the current round is the highest number.
/// </summary>
public class RemsApprovalTask : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid REMSId { get; set; }
    public int RoundNumber { get; set; }
    public Guid ApproverId { get; set; }
    public RemsApproverRole ApproverRole { get; set; }
    public RemsApprovalTaskStatus Status { get; set; } = RemsApprovalTaskStatus.Pending;
    public DateTime? DecidedOnUtc { get; set; }
    public string? RejectionReason { get; set; }

    public RemsRequest? Request { get; set; }
    public User? Approver { get; set; }
    public ICollection<RemsApprovalChecklistItem> ChecklistItems { get; set; } = new List<RemsApprovalChecklistItem>();
}
