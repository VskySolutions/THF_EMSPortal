using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropOptionSetEntityType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OptionSets_TenantId_EntityType_Key",
                table: "OptionSets");

            migrationBuilder.DropColumn(
                name: "EntityType",
                table: "OptionSets");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSets_TenantId_Key",
                table: "OptionSets",
                columns: new[] { "TenantId", "Key" },
                unique: true,
                filter: "[Deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OptionSets_TenantId_Key",
                table: "OptionSets");

            migrationBuilder.AddColumn<int>(
                name: "EntityType",
                table: "OptionSets",
                type: "int",
                nullable: false,
                // 6 = EntityType.Rems, which every seeded list was filed under.
                defaultValue: 6);

            migrationBuilder.CreateIndex(
                name: "IX_OptionSets_TenantId_EntityType_Key",
                table: "OptionSets",
                columns: new[] { "TenantId", "EntityType", "Key" },
                unique: true,
                filter: "[Deleted] = 0");
        }
    }
}
