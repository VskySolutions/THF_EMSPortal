namespace EmsPortal.Domain.Entities;

/// <summary>A person who acts for a client in a role from the REMS.ContactRole option set.</summary>
public class ClientContact : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ClientId { get; set; }
    public Guid PersonId { get; set; }
    public Guid ContactRoleId { get; set; }
    public bool IsPrimary { get; set; }

    public Client? Client { get; set; }
    public Person? Person { get; set; }
    public OptionSetItem? ContactRole { get; set; }
}
