using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>A file attached to a request: an attachment, the client acceptance form or the purchase order.</summary>
public class RemsFile : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid REMSId { get; set; }
    public Guid MediaId { get; set; }
    public RemsFileKind Kind { get; set; } = RemsFileKind.Attachment;

    public RemsRequest? Request { get; set; }
    public Media? Media { get; set; }
}
