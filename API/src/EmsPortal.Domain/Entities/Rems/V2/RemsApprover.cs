namespace EmsPortal.Domain.Entities;

/// <summary>An approver the initiator picked by hand for a request, over and above the standing seats.</summary>
public class RemsApprover : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid REMSId { get; set; }
    public Guid UserId { get; set; }

    public RemsRequest? Request { get; set; }
    public User? User { get; set; }
}
