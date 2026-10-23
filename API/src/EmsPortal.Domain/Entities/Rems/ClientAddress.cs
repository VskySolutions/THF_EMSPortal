namespace EmsPortal.Domain.Entities;

/// <summary>One of a client's addresses, typed by the REMS.AddressType option (Physical, Mailing, Billing).</summary>
public class ClientAddress : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ClientId { get; set; }
    public Guid AddressId { get; set; }
    public Guid AddressTypeId { get; set; }
    public bool IsCurrent { get; set; } = true;

    public Client? Client { get; set; }
    public Address? Address { get; set; }
    public OptionSetItem? AddressType { get; set; }
}
