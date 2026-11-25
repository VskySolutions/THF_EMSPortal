using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmsPortal.Infrastructure.Persistence.Configurations;

internal sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("Addresses");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.AddressType).IsRequired().HasConversion<string>().HasMaxLength(20);

        builder.Property(a => a.AddressLine1).HasMaxLength(256);
        builder.Property(a => a.AddressLine2).HasMaxLength(256);
        builder.Property(a => a.Landmark).HasMaxLength(128);
        builder.Property(a => a.BuildingName).HasMaxLength(128);
        builder.Property(a => a.FloorNumber).HasMaxLength(32);
        builder.Property(a => a.UnitNumber).HasMaxLength(32);

        builder.Property(a => a.CountryCode).HasMaxLength(3);
        builder.Property(a => a.CountryName).HasMaxLength(100);
        builder.Property(a => a.StateCode).HasMaxLength(10);
        builder.Property(a => a.StateName).HasMaxLength(100);
        builder.Property(a => a.CityName).HasMaxLength(100);
        builder.Property(a => a.PostalCode).HasMaxLength(20);

        // The addressee. Sized like the equivalent columns on Person, so a name that fits there fits
        // here — the two hold the same kind of value and a form can copy one into the other.
        builder.Property(a => a.Suffix).HasMaxLength(16);
        builder.Property(a => a.FirstName).HasMaxLength(100);
        builder.Property(a => a.LastName).HasMaxLength(100);
        builder.Property(a => a.Email).HasMaxLength(256);
        builder.Property(a => a.PhoneNumber).HasMaxLength(32);
        builder.Property(a => a.PhoneCountryCode).HasMaxLength(2);
    }
}
