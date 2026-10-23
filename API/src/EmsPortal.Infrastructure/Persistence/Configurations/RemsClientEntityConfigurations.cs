using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

// EF Core configurations for the REMS client materialised from a submission, its entities, and each
// entity's addresses and contacts (WO-110).

internal sealed class RemsClientConfiguration : IEntityTypeConfiguration<REMSClient>
{
    public void Configure(EntityTypeBuilder<REMSClient> builder)
    {
        builder.ToTable("REMSClient", "legacy");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Email).IsRequired().HasMaxLength(256);
        builder.Property(c => c.MobileNumber).HasMaxLength(32);
        // The referral source is an option-set item, referenced by id. Restrict, like every other
        // option reference: a value a client is recorded against is not one to delete.
        builder.HasOne(c => c.ReferralSource)
            .WithMany()
            .HasForeignKey(c => c.ReferralSourceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(c => c.ReferralSource).AutoInclude();
        builder.Property(c => c.ReferralSourceDetail).HasMaxLength(256);
        builder.Property(c => c.BillingContactName).HasMaxLength(200);
        builder.Property(c => c.BillingEmail).HasMaxLength(256);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.TenantId);

        builder.HasOne(c => c.Rems).WithMany(r => r.Clients).HasForeignKey(c => c.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.SourceFormSubmission).WithMany().HasForeignKey(c => c.SourceFormSubmissionId).OnDelete(DeleteBehavior.Restrict);

        // One active client per request.
        builder.HasIndex(c => new { c.TenantId, c.REMSId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsEntityConfiguration : IEntityTypeConfiguration<REMSEntity>
{
    public void Configure(EntityTypeBuilder<REMSEntity> builder)
    {
        builder.ToTable("REMSEntity", "legacy");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.EIN).HasMaxLength(32);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.TenantId);

        builder.HasOne(e => e.Client).WithMany(c => c.Entities).HasForeignKey(e => e.REMSClientId).OnDelete(DeleteBehavior.Restrict);

        // Exactly one active main entity per client. Named explicitly so it stays distinct from the plain
        // nav index on the same columns below (an unnamed HasIndex on the same property set would merge
        // into one index).
        builder.HasIndex(e => new { e.TenantId, e.REMSClientId }, "IX_REMSEntity_TenantId_REMSClientId_Main")
            .IsUnique()
            .HasFilter("[IsMainEntity] = 1 AND [Deleted] = 0");

        // Navigation lookup by client.
        builder.HasIndex(e => new { e.TenantId, e.REMSClientId });
    }
}

internal sealed class RemsEntityAddressConfiguration : IEntityTypeConfiguration<REMSEntityAddress>
{
    public void Configure(EntityTypeBuilder<REMSEntityAddress> builder)
    {
        builder.ToTable("REMSEntityAddress", "legacy");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.AddressType).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.TenantId);

        builder.HasOne(a => a.Entity).WithMany(e => e.Addresses).HasForeignKey(a => a.REMSEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Address).WithMany().HasForeignKey(a => a.AddressId).OnDelete(DeleteBehavior.Restrict);

        // One address per (entity, type) — with ONE exception, which is why the filter names it.
        //
        // Billing addresses are deliberately plural: the client intake form asks where invoices should be
        // sent and lets the client name more than one place, each with its own addressee, and being given
        // second does not make an address a different kind of address. Exactly the rule REMSEntityContact
        // already applies to the billing CONTACT, and for the same reason — under a plain unique index the
        // second billing address failed the insert at the end of a submit that had already built the
        // client, the entity and every contact, so the client lost the whole form.
        //
        // Physical and Mailing ARE singular — an entity operates from one place and takes post at one —
        // and the index still says so. Dropping uniqueness altogether would give that up everywhere to
        // make room for the one type that does not want it.
        builder.HasIndex(a => new { a.TenantId, a.REMSEntityId, a.AddressType })
            .IsUnique()
            .HasFilter("[Deleted] = 0 AND [AddressType] <> 'Billing'");
    }
}

internal sealed class RemsEntityContactConfiguration : IEntityTypeConfiguration<REMSEntityContact>
{
    public void Configure(EntityTypeBuilder<REMSEntityContact> builder)
    {
        builder.ToTable("REMSEntityContact", "legacy");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ContactRole).IsRequired().HasMaxLength(64);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.TenantId);

        builder.HasOne(c => c.Entity).WithMany(e => e.Contacts).HasForeignKey(c => c.REMSEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Person).WithMany().HasForeignKey(c => c.PersonId).OnDelete(DeleteBehavior.Restrict);

        // One contact per (entity, role) — with ONE exception, which is why the filter names it.
        //
        // Billing contacts are deliberately plural: the client intake form asks who should be invoiced and
        // lets the client name more than one, and being named second does not make somebody a different
        // kind of contact — they are all the BillingContact role. Under a plain unique index the second one
        // failed the insert, and it failed at the end of a submit that had already built the client, the
        // entity, its addresses and every other contact, so the client lost the whole form.
        //
        // Every other role IS singular — an entity has one Primary Contact, one Financial Contact — and the
        // index still says so. Dropping uniqueness altogether would have given that up everywhere to make
        // room for the one role that does not want it.
        builder.HasIndex(c => new { c.TenantId, c.REMSEntityId, c.ContactRole })
            .IsUnique()
            .HasFilter("[Deleted] = 0 AND [ContactRole] <> 'BillingContact'");
    }
}
