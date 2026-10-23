namespace EmsPortal.Domain.Entities;

/// <summary>A government engagement's bill rate for one personnel level (REMS.PersonnelLevel option).</summary>
public class RemsPersonnelRate : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid REMSId { get; set; }
    public Guid PersonnelLevelId { get; set; }
    public decimal BillRatePerHour { get; set; }

    public RemsRequest? Request { get; set; }
    public OptionSetItem? PersonnelLevel { get; set; }
}
