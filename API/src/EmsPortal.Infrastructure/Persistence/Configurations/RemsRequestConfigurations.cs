using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

// EF Core configurations for the REMS request root and its file links (WO-110). Table names keep the
// REMS casing. Every table is tenant-owned (TenantId FK → Tenant, Restrict) and soft-deletable;
// non-deleted unique indexes are filtered on [Deleted] = 0.

internal sealed class RemsConfiguration : IEntityTypeConfiguration<REMS>
{
    public void Configure(EntityTypeBuilder<REMS> builder)
    {
        builder.ToTable("REMS");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.REMSNumber).IsRequired().HasMaxLength(64);
        // The partner's message is client-facing now and holds pasted correspondence, so it is uncapped.
        // The old nvarchar(500) measured the markup rather than the words in it, which a forwarded email
        // exceeded almost immediately.
        builder.Property(r => r.Description).HasColumnType("nvarchar(max)");
        // Type and Status are option-set items, referenced by id. Restrict on both: a value a request is
        // recorded against is not one to delete, and the application branches on the code behind it.
        builder.HasOne(r => r.Type).WithMany().HasForeignKey(r => r.TypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Status).WithMany().HasForeignKey(r => r.StatusId).OnDelete(DeleteBehavior.Restrict);

        // ALWAYS loaded. The code behind each of these is read by everything that touches a request -- the
        // list rows, the workflow guards, the status badge -- and a query that forgot to Include one does
        // not fail at the query: it fails much later, as a NullReferenceException while the response is
        // being serialised. AutoInclude makes forgetting impossible. Both are single rows on a tiny table
        // joined by primary key; a caller that genuinely wants neither can say IgnoreAutoIncludes().
        builder.Navigation(r => r.Type).AutoInclude();
        builder.Navigation(r => r.Status).AutoInclude();
        // AND SO IS THE CLIENT, for exactly the same reason. The request's name, suffix, email and mobile
        // are read-throughs onto this Person now (see REMS), so a query that forgot it would not fail at
        // the query — it would hand every surface a blank client name and a null email much later.
        builder.Navigation(r => r.ClientPerson).AutoInclude();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.TenantId);

        builder.HasOne<User>().WithMany().HasForeignKey(r => r.AdminAssignedToId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.CSEId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.OnBehalfOfUserId).OnDelete(DeleteBehavior.Restrict);

        // "Everything raised for me", including what a delegate prepared — the principal's own list.
        builder.HasIndex(r => new { r.TenantId, r.OnBehalfOfUserId, r.StatusId });

        // The client's Person master record. A real FK, unlike the loose ExistingClientReferenceId beside
        // it: a person may be shared by many requests and may later become a User, so the link has to hold.
        // Restrict, like every other FK here — purging a REMS request leaves the person standing, and a
        // person cannot be purged out from under the requests that name them as the client.
        builder.HasOne(r => r.ClientPerson).WithMany().HasForeignKey(r => r.ClientPersonId).OnDelete(DeleteBehavior.Restrict);

        // "Every request for this client" — the read behind a client's history, and behind converting one
        // into a user.
        builder.HasIndex(r => new { r.TenantId, r.ClientPersonId });

        // One active request number per tenant.
        builder.HasIndex(r => new { r.TenantId, r.REMSNumber }).IsUnique().HasFilter("[Deleted] = 0");

        // Admin work pool and partner (creator) views.
        builder.HasIndex(r => new { r.TenantId, r.StatusId, r.AdminAssignedToId, r.CreatedOnUtc });
        builder.HasIndex(r => new { r.TenantId, r.CreatedById, r.StatusId });
    }
}

internal sealed class RemsFilesConfiguration : IEntityTypeConfiguration<REMSFiles>
{
    public void Configure(EntityTypeBuilder<REMSFiles> builder)
    {
        builder.ToTable("REMSFiles");
        builder.HasKey(f => f.Id);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(f => f.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => f.TenantId);

        builder.HasOne(f => f.Rems).WithMany(r => r.Files).HasForeignKey(f => f.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(f => f.Media).WithMany().HasForeignKey(f => f.MediaId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(f => new { f.TenantId, f.REMSId, f.MediaId }).IsUnique().HasFilter("[Deleted] = 0");
    }
}

internal sealed class RemsAdditionalEntityConfiguration : IEntityTypeConfiguration<REMSAdditionalEntity>
{
    public void Configure(EntityTypeBuilder<REMSAdditionalEntity> builder)
    {
        builder.ToTable("REMSAdditionalEntity");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.SourceKey).IsRequired().HasMaxLength(64);
        builder.Property(a => a.FullName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.EmailAddress).HasMaxLength(256);
        builder.Property(a => a.PhoneNumber).HasMaxLength(32);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.TenantId);

        builder.HasOne(a => a.Rems).WithMany(r => r.AdditionalEntities).HasForeignKey(a => a.REMSId).OnDelete(DeleteBehavior.Restrict);

        // CreatedREMSId is deliberately NOT a foreign key. The follow-up request stands on its own — it is
        // not a child of the request that revealed it — and modelling it as a relationship would invite
        // exactly the parent/child reads that decision rules out.

        // The hand-set progress status. Restrict like every other option-set reference: a value rows are
        // recorded against is not one to delete out from under them.
        builder.HasOne(a => a.RelatedStatus).WithMany().HasForeignKey(a => a.RelatedStatusId)
            .OnDelete(DeleteBehavior.Restrict);
        // ALWAYS loaded, for the same reason REMS.Status is: the Related Entities list reads the CODE
        // behind this on every row, and a query that forgot to Include it fails during serialisation
        // rather than at the query. One row on a tiny table, joined by primary key.
        builder.Navigation(a => a.RelatedStatus).AutoInclude();

        builder.HasIndex(a => new { a.TenantId, a.REMSId, a.SourceKey }).IsUnique().HasFilter("[Deleted] = 0");

        // "Which of this client's other businesses still need an EMS" — the read behind the list flag.
        builder.HasIndex(a => new { a.TenantId, a.REMSId, a.CreatedREMSId });
    }
}

internal sealed class RemsAdditionalIndividualConfiguration : IEntityTypeConfiguration<REMSAdditionalIndividual>
{
    public void Configure(EntityTypeBuilder<REMSAdditionalIndividual> builder)
    {
        builder.ToTable("REMSAdditionalIndividual");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.SourceKey).IsRequired().HasMaxLength(64);
        // Codes, not display words — spouse / child / other, joint / individual, primary / separate.
        builder.Property(a => a.RelationType).IsRequired().HasMaxLength(32);
        builder.Property(a => a.FilingType).IsRequired().HasMaxLength(32);
        builder.Property(a => a.BillingPreference).IsRequired().HasMaxLength(32);
        builder.Property(a => a.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(a => a.LastName).IsRequired().HasMaxLength(100);
        // 16, the same cap the client's own suffix carries — it is the same box asking the same question.
        builder.Property(a => a.Suffix).HasMaxLength(16);
        builder.Property(a => a.Email).HasMaxLength(256);
        builder.Property(a => a.PhoneNumber).HasMaxLength(32);
        builder.Property(a => a.BillingFirstName).HasMaxLength(100);
        builder.Property(a => a.BillingLastName).HasMaxLength(100);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.TenantId);

        // No inverse navigations: these rows are read through the request they were declared on, and a
        // collection hanging off REMS would pull them into every graph traversal that saves one.
        builder.HasOne(a => a.Rems).WithMany().HasForeignKey(a => a.REMSId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Entity).WithMany().HasForeignKey(a => a.REMSEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Person).WithMany().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Restrict);

        // The hand-set progress status, exactly as on REMSAdditionalEntity — the Related Entities list
        // shows both kinds of related client side by side and reads this on every row.
        builder.HasOne(a => a.RelatedStatus).WithMany().HasForeignKey(a => a.RelatedStatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(a => a.RelatedStatus).AutoInclude();

        // One row per declared person per request. NOT keyed on the name: a client may genuinely have two
        // children with the same first name, and the payload's own key is what identifies the block.
        builder.HasIndex(a => new { a.TenantId, a.REMSId, a.SourceKey }).IsUnique().HasFilter("[Deleted] = 0");

        // "Who else is on this entity's return" — the read every surface that shows them does.
        builder.HasIndex(a => new { a.TenantId, a.REMSEntityId });
    }
}

internal sealed class RemsDelegationConfiguration : IEntityTypeConfiguration<REMSDelegation>
{
    public void Configure(EntityTypeBuilder<REMSDelegation> builder)
    {
        builder.ToTable("REMSDelegation", t => t.HasCheckConstraint(
            "CK_REMSDelegation_Dates",
            "[StartsOn] IS NULL OR [EndsOn] IS NULL OR [EndsOn] >= [StartsOn]"));
        builder.HasKey(d => d.Id);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.TenantId);

        builder.HasOne(d => d.Principal).WithMany().HasForeignKey(d => d.PrincipalUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.Delegate).WithMany().HasForeignKey(d => d.DelegateUserId).OnDelete(DeleteBehavior.Restrict);

        // One live grant per pair. Re-granting with different rights edits the row rather than stacking a
        // second one, so there is never a question of which of two grants applies.
        builder.HasIndex(d => new { d.TenantId, d.PrincipalUserId, d.DelegateUserId })
            .IsUnique()
            .HasFilter("[Deleted] = 0");

        // "Who can I act for?" — the read behind the acting-as picker.
        builder.HasIndex(d => new { d.TenantId, d.DelegateUserId });
    }
}

internal sealed class RemsSendBackConfiguration : IEntityTypeConfiguration<REMSSendBack>
{
    public void Configure(EntityTypeBuilder<REMSSendBack> builder)
    {
        builder.ToTable("REMSSendBack");
        builder.HasKey(s => s.Id);

        // Matches the request's own description: an admin explaining what is wrong with the setup should
        // not be rationed, and a truncated reason is not actionable.
        builder.Property(s => s.Reason).IsRequired().HasColumnType("nvarchar(max)");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.TenantId);

        builder.HasOne(s => s.Rems).WithMany(r => r.SendBacks).HasForeignKey(s => s.REMSId).OnDelete(DeleteBehavior.Restrict);

        // At most one unresolved return per request: the loop is sequential, so a second open return would
        // mean the request was in two places at once.
        builder.HasIndex(s => new { s.TenantId, s.REMSId })
            .IsUnique()
            .HasFilter("[Deleted] = 0 AND [ResolvedOnUtc] IS NULL");

        // The history read, newest last.
        builder.HasIndex(s => new { s.TenantId, s.REMSId, s.CreatedOnUtc });
    }
}
