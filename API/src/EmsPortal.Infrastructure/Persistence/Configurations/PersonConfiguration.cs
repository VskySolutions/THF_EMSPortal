using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("Persons");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.PersonCode).IsRequired().HasMaxLength(64);
        builder.HasIndex(p => p.PersonCode).IsUnique().HasFilter("[Deleted] = 0");

        // Personal information
        // Room for a written-out particle ("the Third") as well as the abbreviations offered, and no
        // more — the same 16 the REMS client name's suffix gets, for the same reason.
        builder.Property(p => p.Suffix).HasMaxLength(16);
        // Which shape this row is. Stored as the enum's int, like SourceEntityType beside it — the
        // application branches on it absolutely and there is no third kind of party to add.
        builder.Property(p => p.PartyType).HasConversion<int>();
        // An organisation's legal name. Longer than a person's name field because it is a whole legal
        // name — "Coastal Assurance Claims Services LLC" — not one part of one.
        builder.Property(p => p.CorporateName).HasMaxLength(200);
        // "Every individual client, by surname" — the client picker's read, and the one place the type is
        // used to narrow rather than merely to render.
        builder.HasIndex(p => new { p.TenantId, p.PartyType, p.LastName });

        // THE CLIENT NAME, composed once, by the database.
        //
        // "LastName FirstName Suffix" for a human — "Smith John Jr." — and the plain legal name for an
        // organisation. Surname first because a client list is scanned and sorted by family name, which
        // means the composition has to be something SQL can ORDER BY and LIKE over the whole set, not
        // something C# does to the twenty rows a page happens to hold.
        //
        // PERSISTED, so it is stored and indexable rather than recomputed per row per query. Deterministic,
        // which is what allows that.
        //
        // Each part carries its own leading space and only when it is present, so an empty first name
        // cannot leave "Smith  Jr." with a double space in it; the whole is trimmed for the case where the
        // surname itself is missing. A row with nothing at all falls back to DisplayName, which is what a
        // contact captured from a single free-text box before the split existed still holds.
        builder.Property(p => p.ClientDisplayName)
            .HasMaxLength(400)
            .HasComputedColumnSql(
                """
                COALESCE(NULLIF(LTRIM(RTRIM(
                    CASE WHEN [PartyType] = 1 THEN ISNULL([CorporateName], N'')
                         ELSE ISNULL(NULLIF(LTRIM(RTRIM([LastName])), N''), N'')
                            + CASE WHEN NULLIF(LTRIM(RTRIM([FirstName])), N'') IS NULL THEN N''
                                   ELSE N' ' + LTRIM(RTRIM([FirstName])) END
                            + CASE WHEN NULLIF(LTRIM(RTRIM([Suffix])), N'') IS NULL THEN N''
                                   ELSE N' ' + LTRIM(RTRIM([Suffix])) END
                    END)), N''), [DisplayName])
                """,
                stored: true);
        // One client per Maconomy customer in a tenant: a second pick of the same customer finds the first.
        builder.Property(p => p.MaconomyCustomerNumber).HasMaxLength(64);
        builder.HasIndex(p => new { p.TenantId, p.MaconomyCustomerNumber })
            .IsUnique()
            .HasFilter("[MaconomyCustomerNumber] IS NOT NULL AND [Deleted] = 0");

        builder.Property(p => p.FirstName).HasMaxLength(100);
        builder.Property(p => p.MiddleName).HasMaxLength(100);
        builder.Property(p => p.LastName).HasMaxLength(100);
        builder.Property(p => p.DisplayName).HasMaxLength(200);
        builder.Property(p => p.PreferredName).HasMaxLength(100);
        builder.Property(p => p.Gender).HasMaxLength(32);
        builder.Property(p => p.MaritalStatus).HasMaxLength(32);
        builder.Property(p => p.Nationality).HasMaxLength(100);

        // Contact information
        builder.Property(p => p.PrimaryEmail).HasMaxLength(256);
        builder.Property(p => p.SecondaryEmail).HasMaxLength(256);
        builder.Property(p => p.MobileNumber).HasMaxLength(32);
        builder.Property(p => p.CountryCode).HasMaxLength(8);
        builder.Property(p => p.AlternateMobileNumber).HasMaxLength(32);
        builder.Property(p => p.EmergencyContactName).HasMaxLength(150);
        builder.Property(p => p.EmergencyContactRelationship).HasMaxLength(64);
        builder.Property(p => p.EmergencyContactNumber).HasMaxLength(32);

        // Professional information
        builder.Property(p => p.EmployeeCode).HasMaxLength(64);

        builder.Property(p => p.Notes).HasMaxLength(2000);

        // Primary address + profile image (no cascade — addresses/media are shared, reusable).
        builder.HasOne(p => p.Address)
            .WithMany()
            .HasForeignKey(p => p.AddressId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.ProfileMedia)
            .WithMany()
            .HasForeignKey(p => p.ProfileMediaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Owning tenant (optional). Restrict so a tenant cannot be removed out from under its persons.
        builder.HasOne(p => p.Tenant)
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.TenantId);

        // Provenance. Deliberately not an FK — the source is polymorphic, and the pair is indexed
        // together because it is always read together ("everyone this REMS request brought in").
        builder.HasIndex(p => new { p.SourceEntityType, p.SourceEntityId });
    }
}
