using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

// The client master and its sub-tables (Client_*). Every FK is Restrict, every "one per parent" rule is a
// filtered unique index, and option-set values are referenced by id, like the rest of the platform.

internal sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("Client", t => t.HasCheckConstraint(
            "CK_Client_Party",
            "[PersonId] IS NOT NULL OR [LegalName] IS NOT NULL"));
        builder.HasKey(c => c.Id);
        builder.Ignore(c => c.DisplayName);
        builder.Property(c => c.ClientNumber).IsRequired().HasMaxLength(64);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.LegalName).HasMaxLength(200);
        builder.Property(c => c.EIN).HasMaxLength(32);
        builder.Property(c => c.PrimaryEmail).HasMaxLength(256);
        builder.Property(c => c.PrimaryPhone).HasMaxLength(32);
        builder.Property(c => c.ReferralSourceDetail).HasMaxLength(500);
        builder.Property(c => c.MaconomyCustomerNumber).HasMaxLength(64);
        builder.Property(c => c.MaconomySyncStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.MaconomySyncError).HasMaxLength(1000);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.TenantId);
        builder.HasOne(c => c.Person).WithMany().HasForeignKey(c => c.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.EntityType).WithMany().HasForeignKey(c => c.EntityTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Industry).WithMany().HasForeignKey(c => c.IndustryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.ReferralSource).WithMany().HasForeignKey(c => c.ReferralSourceId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(c => c.Person).AutoInclude();
        builder.Navigation(c => c.EntityType).AutoInclude();
        builder.HasIndex(c => new { c.TenantId, c.ClientNumber }).IsUnique().HasFilter("[Deleted] = 0");
        // A person is one client. An email is one client too, but that rule lives in the lookup code so a
        // household sharing an address does not block a spouse from being a related client.
        builder.HasIndex(c => new { c.TenantId, c.PersonId }).IsUnique().HasFilter("[Deleted] = 0 AND [PersonId] IS NOT NULL");
        builder.HasIndex(c => new { c.TenantId, c.PrimaryEmail }).HasFilter("[PrimaryEmail] IS NOT NULL");
        builder.HasIndex(c => new { c.TenantId, c.Status });
        builder.HasIndex(c => new { c.TenantId, c.MaconomyCustomerNumber }).HasFilter("[MaconomyCustomerNumber] IS NOT NULL");
    }
}

internal sealed class ClientAddressConfiguration : IEntityTypeConfiguration<ClientAddress>
{
    public void Configure(EntityTypeBuilder<ClientAddress> builder)
    {
        builder.ToTable("Client_Address");
        builder.HasKey(a => a.Id);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.TenantId);
        builder.HasOne(a => a.Client).WithMany(c => c.Addresses).HasForeignKey(a => a.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Address).WithMany().HasForeignKey(a => a.AddressId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.AddressType).WithMany().HasForeignKey(a => a.AddressTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(a => a.Address).AutoInclude();
        builder.Navigation(a => a.AddressType).AutoInclude();
        // Physical and mailing are one each, billing may be several: a rule in code, not in the index.
        builder.HasIndex(a => new { a.TenantId, a.ClientId, a.AddressTypeId });
    }
}

internal sealed class ClientContactConfiguration : IEntityTypeConfiguration<ClientContact>
{
    public void Configure(EntityTypeBuilder<ClientContact> builder)
    {
        builder.ToTable("Client_Contact");
        builder.HasKey(c => c.Id);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.TenantId);
        builder.HasOne(c => c.Client).WithMany(x => x.Contacts).HasForeignKey(c => c.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Person).WithMany().HasForeignKey(c => c.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.ContactRole).WithMany().HasForeignKey(c => c.ContactRoleId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(c => c.Person).AutoInclude();
        builder.Navigation(c => c.ContactRole).AutoInclude();
        builder.HasIndex(c => new { c.TenantId, c.ClientId, c.PersonId, c.ContactRoleId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class ClientRelationshipConfiguration : IEntityTypeConfiguration<ClientRelationship>
{
    public void Configure(EntityTypeBuilder<ClientRelationship> builder)
    {
        builder.ToTable("Client_Relationship", t => t.HasCheckConstraint(
            "CK_Client_Relationship_Distinct",
            "[ClientId] <> [RelatedClientId]"));
        builder.HasKey(r => r.Id);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.TenantId);
        builder.HasOne(r => r.Client).WithMany(c => c.Relationships).HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.RelatedClient).WithMany().HasForeignKey(r => r.RelatedClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.RelationType).WithMany().HasForeignKey(r => r.RelationTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.FilingType).WithMany().HasForeignKey(r => r.FilingTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.BillingPreference).WithMany().HasForeignKey(r => r.BillingPreferenceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.RelatedStatus).WithMany().HasForeignKey(r => r.RelatedStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(r => r.RelatedClient).AutoInclude();
        builder.Navigation(r => r.RelationType).AutoInclude();
        builder.Navigation(r => r.RelatedStatus).AutoInclude();
        builder.HasIndex(r => new { r.TenantId, r.ClientId, r.RelatedClientId }).IsUnique().HasFilter("[Deleted] = 0");
        builder.HasIndex(r => new { r.TenantId, r.RelatedClientId });
    }
}

internal sealed class ClientContractConfiguration : IEntityTypeConfiguration<ClientContract>
{
    public void Configure(EntityTypeBuilder<ClientContract> builder)
    {
        builder.ToTable("Client_Contract");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.OriginalTerm).HasMaxLength(500);
        builder.Property(c => c.RenewalTerms).HasMaxLength(500);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.TenantId);
        builder.HasOne(c => c.Client).WithMany(x => x.Contracts).HasForeignKey(c => c.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.TenantId, c.ClientId });
        builder.HasIndex(c => new { c.TenantId, c.ClientId }, "IX_Client_Contract_Current")
            .IsUnique()
            .HasFilter("[Deleted] = 0 AND [IsCurrent] = 1");
    }
}
