namespace EmsPortal.Domain.Entities;

/// <summary>A tax form the engagement covers (REMS.TaxForm option); one row per form.</summary>
public class RemsTaxForm : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid REMSId { get; set; }
    public Guid TaxFormId { get; set; }

    public RemsRequest? Request { get; set; }
    public OptionSetItem? TaxForm { get; set; }
}
