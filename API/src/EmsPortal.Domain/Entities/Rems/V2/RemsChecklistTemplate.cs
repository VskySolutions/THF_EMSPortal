using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>A checklist line the tenant wants every approver in a role to tick. Copied onto each task when a round is routed.</summary>
public class RemsChecklistTemplate : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SettingsId { get; set; }
    public RemsApproverRole ApproverRole { get; set; }
    public int DisplayOrder { get; set; }
    public string Label { get; set; } = string.Empty;

    public RemsSettings? Settings { get; set; }
}
