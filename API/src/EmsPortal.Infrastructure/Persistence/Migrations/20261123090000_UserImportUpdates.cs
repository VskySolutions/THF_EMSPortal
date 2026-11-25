using EmsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A user import may now update the users it finds by email: the file's count of users changed, and
    /// the Status cell of each line as typed.
    /// </summary>
    [DbContext(typeof(EmsPortalDbContext))]
    [Migration("20261123090000_UserImportUpdates")]
    public partial class UserImportUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UpdatedCount",
                table: "UserImports",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AccountStatus",
                table: "UserImportRows",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "UpdatedCount", table: "UserImports");
            migrationBuilder.DropColumn(name: "AccountStatus", table: "UserImportRows");
        }
    }
}
