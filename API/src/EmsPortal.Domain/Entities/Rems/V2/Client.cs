using System.ComponentModel.DataAnnotations.Schema;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Domain.Entities;

/// <summary>
/// A legal party the firm deals with: a person or an organisation. Created when an initiator names a new
/// client, filled in by the intake form, kept by staff afterwards. Outlives any one <see cref="RemsRequest"/>.
/// </summary>
public class Client : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>"CL-n" from the client sequence; never reused.</summary>
    public string ClientNumber { get; set; } = string.Empty;
    public ClientStatus Status { get; set; } = ClientStatus.Prospect;

    /// <summary>The person, when the client is an individual. Null for an organisation.</summary>
    public Guid? PersonId { get; set; }
    /// <summary>The organisation's legal name. Null for an individual.</summary>
    public string? LegalName { get; set; }

    /// <summary>REMS.EntityType option. Locked once a request references the client.</summary>
    public Guid? EntityTypeId { get; set; }
    /// <summary>REMS.Industry option.</summary>
    public Guid? IndustryId { get; set; }
    public string? EIN { get; set; }
    public DateOnly? FiscalYearEnd { get; set; }

    /// <summary>The channel the firm uses for this client. One email belongs to one client.</summary>
    public string? PrimaryEmail { get; set; }
    public string? PrimaryPhone { get; set; }

    /// <summary>REMS.ReferralSource option, with the free-text detail the form asks for.</summary>
    public Guid? ReferralSourceId { get; set; }
    public string? ReferralSourceDetail { get; set; }

    // ---- Maconomy ----
    public string? MaconomyCustomerNumber { get; set; }
    public RemsMaconomyCustomerSyncStatus MaconomySyncStatus { get; set; } = RemsMaconomyCustomerSyncStatus.NotSynced;
    public DateTime? MaconomySyncedOnUtc { get; set; }
    public string? MaconomySyncError { get; set; }

    // ---- Navigations ----
    public Person? Person { get; set; }
    public OptionSetItem? EntityType { get; set; }
    public OptionSetItem? Industry { get; set; }
    public OptionSetItem? ReferralSource { get; set; }
    public ICollection<ClientAddress> Addresses { get; set; } = new List<ClientAddress>();
    public ICollection<ClientContact> Contacts { get; set; } = new List<ClientContact>();
    public ICollection<ClientRelationship> Relationships { get; set; } = new List<ClientRelationship>();
    public ICollection<ClientContract> Contracts { get; set; } = new List<ClientContract>();
    public ICollection<RemsRequest> Requests { get; set; } = new List<RemsRequest>();

    /// <summary>The person's display name or the organisation's legal name; not a column.</summary>
    [NotMapped]
    public string DisplayName => Person?.DisplayName ?? LegalName ?? string.Empty;

    [NotMapped]
    public bool IsOrganisation => PersonId is null;

    /// <summary>Whether this client is a Maconomy customer; the directory, not the row, then holds the name and contact.</summary>
    [NotMapped]
    public bool IsMaconomyClient => !string.IsNullOrWhiteSpace(MaconomyCustomerNumber);

    /// <summary>The Maconomy customer card, hydrated from the directory on read; never stored.</summary>
    [NotMapped]
    public MaconomyClientDetails? Maconomy { get; set; }

    /// <summary>What to call the client: the Maconomy card's name for a linked customer, the row's name otherwise.</summary>
    [NotMapped]
    public string ClientName => IsMaconomyClient
        ? Maconomy?.Name ?? (DisplayName.Length > 0 ? DisplayName : Person.MaconomyPlaceholderName(MaconomyCustomerNumber!))
        : DisplayName;

    [NotMapped]
    public string? ClientEmail => IsMaconomyClient ? Maconomy?.Email ?? PrimaryEmail : PrimaryEmail;

    [NotMapped]
    public string? ClientPhone => IsMaconomyClient ? Maconomy?.Phone ?? PrimaryPhone : PrimaryPhone;
}
