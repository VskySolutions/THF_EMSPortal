namespace EmsPortal.Domain.Entities;

/// <summary>A checklist line an approver ticks before deciding. The label is copied from the template when the task is created.</summary>
public class RemsApprovalChecklistItem : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TaskId { get; set; }
    public int DisplayOrder { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime? CompletedOnUtc { get; set; }

    public RemsApprovalTask? Task { get; set; }
}
