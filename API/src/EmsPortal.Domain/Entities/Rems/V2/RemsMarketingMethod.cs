namespace EmsPortal.Domain.Entities;

/// <summary>A marketing method chosen for a request (REMS.MarketingMethod option); one row per method.</summary>
public class RemsMarketingMethod : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid REMSId { get; set; }
    public Guid MarketingMethodId { get; set; }

    public RemsRequest? Request { get; set; }
    public OptionSetItem? MarketingMethod { get; set; }
}
