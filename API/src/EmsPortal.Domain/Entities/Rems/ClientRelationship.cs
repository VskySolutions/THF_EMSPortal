namespace EmsPortal.Domain.Entities;

/// <summary>
/// How one client relates to another: a spouse, a child, a subsidiary. The related party is itself a
/// <see cref="Client"/>, so it can get requests of its own.
/// </summary>
public class ClientRelationship : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>The primary client the relationship is declared on.</summary>
    public Guid ClientId { get; set; }
    public Guid RelatedClientId { get; set; }
    /// <summary>REMS.RelationType option.</summary>
    public Guid RelationTypeId { get; set; }
    /// <summary>REMS.FilingType option; individuals only.</summary>
    public Guid? FilingTypeId { get; set; }
    /// <summary>REMS.BillingPreference option; individuals only.</summary>
    public Guid? BillingPreferenceId { get; set; }
    public bool IsMinor { get; set; }
    /// <summary>REMS.RelatedEntityStatus option, set by hand on the Related Entities page.</summary>
    public Guid? RelatedStatusId { get; set; }

    public Client? Client { get; set; }
    public Client? RelatedClient { get; set; }
    public OptionSetItem? RelationType { get; set; }
    public OptionSetItem? FilingType { get; set; }
    public OptionSetItem? BillingPreference { get; set; }
    public OptionSetItem? RelatedStatus { get; set; }
}
