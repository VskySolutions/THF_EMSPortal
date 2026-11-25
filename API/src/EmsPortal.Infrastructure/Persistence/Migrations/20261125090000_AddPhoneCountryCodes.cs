using EmsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The country picked for a phone number is kept beside the number (ISO-2): the number alone cannot
    /// tell Canada from the US. Persons already has the column (CountryCode).
    /// </summary>
    [DbContext(typeof(EmsPortalDbContext))]
    [Migration("20261125090000_AddPhoneCountryCodes")]
    public partial class AddPhoneCountryCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrimaryPhoneCountryCode",
                table: "Client",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneCountryCode",
                table: "Addresses",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PrimaryPhoneCountryCode", table: "Client");
            migrationBuilder.DropColumn(name: "PhoneCountryCode", table: "Addresses");
        }
    }
}
