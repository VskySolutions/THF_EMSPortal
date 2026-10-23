namespace EmsPortal.Domain.Entities;

/// <summary>
/// One row per status transition: from, to, who, when and why. Append-only. A send-back is the
/// transition to returned_to_initiator with <see cref="ReturnedToUserId"/> set, and it is unresolved
/// while it is the latest row.
/// </summary>
public class RemsStatusHistory : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid REMSId { get; set; }
    public Guid? FromStatusId { get; set; }
    public Guid ToStatusId { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public Guid? ReturnedToUserId { get; set; }
    public DateTime ChangedOnUtc { get; set; }
    public string? Reason { get; set; }

    public RemsRequest? Request { get; set; }
    public OptionSetItem? FromStatus { get; set; }
    public OptionSetItem? ToStatus { get; set; }
    public User? ChangedByUser { get; set; }
    public User? ReturnedToUser { get; set; }
}
