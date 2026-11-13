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

    // ---- Maconomy document: an attachment under the client's customer, the CAF and purchase order under the job ----
    public RemsMaconomyDocumentSyncStatus MaconomyDocumentSyncStatus { get; set; } = RemsMaconomyDocumentSyncStatus.NotSynced;
    /// <summary>Maconomy's Document Archive No. the file was added to: "Customer 900004", "Job 10234".</summary>
    public string? MaconomyDocumentArchiveNumber { get; set; }
    /// <summary>The name the document carries in that archive; with the archive number it is Maconomy's key for it.</summary>
    public string? MaconomyDocumentName { get; set; }
    /// <summary>Maconomy's own key for the document (its instance key), where the reply gave one.</summary>
    public string? MaconomyDocumentKey { get; set; }
    public DateTime? MaconomyDocumentSyncedOnUtc { get; set; }
    public string? MaconomyDocumentSyncError { get; set; }

    public RemsRequest? Request { get; set; }
    public Media? Media { get; set; }
}
