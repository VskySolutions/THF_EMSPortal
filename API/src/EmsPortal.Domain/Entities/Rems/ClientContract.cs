namespace EmsPortal.Domain.Entities;

/// <summary>
/// A government client's contract with the firm as the client declares it on the intake form. A request
/// for that client points at the contract it runs under.
/// </summary>
public class ClientContract : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ClientId { get; set; }
    public DateOnly? ContractStartDate { get; set; }
    public DateOnly? ContractEndDate { get; set; }
    public string? OriginalTerm { get; set; }
    public string? RenewalTerms { get; set; }
    public DateOnly? PurchaseOrderStartDate { get; set; }
    public DateOnly? PurchaseOrderEndDate { get; set; }
    public bool IsCurrent { get; set; } = true;

    public Client? Client { get; set; }
}
