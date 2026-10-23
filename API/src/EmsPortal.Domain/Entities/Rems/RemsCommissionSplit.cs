namespace EmsPortal.Domain.Entities;

/// <summary>An employee's share of the commission on a request; one row per employee.</summary>
public class RemsCommissionSplit : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid REMSId { get; set; }
    public Guid EmployeeId { get; set; }
    public decimal CommissionPercentage { get; set; }

    public RemsRequest? Request { get; set; }
    public User? Employee { get; set; }
}
