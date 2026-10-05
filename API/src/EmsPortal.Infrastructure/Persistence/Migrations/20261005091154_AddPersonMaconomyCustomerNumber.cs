using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonMaconomyCustomerNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MaconomyCustomerNumber",
                table: "Persons",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Persons_TenantId_MaconomyCustomerNumber",
                table: "Persons",
                columns: new[] { "TenantId", "MaconomyCustomerNumber" },
                unique: true,
                filter: "[MaconomyCustomerNumber] IS NOT NULL AND [Deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Persons_TenantId_MaconomyCustomerNumber",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "MaconomyCustomerNumber",
                table: "Persons");
        }
    }
}
