using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropRemsTaxForm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "REMS_TaxForm");

            // The REMS Tax Form option list, every tenant's copy: nothing references it once the table is gone.
            migrationBuilder.Sql(@"
DELETE i FROM OptionSetItems i JOIN OptionSets s ON s.Id = i.OptionSetId WHERE s.[Key] = 'REMS.TaxForm';
DELETE FROM OptionSets WHERE [Key] = 'REMS.TaxForm';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "REMS_TaxForm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxFormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS_TaxForm", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMS_TaxForm_OptionSetItems_TaxFormId",
                        column: x => x.TaxFormId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_TaxForm_REMS_REMSId",
                        column: x => x.REMSId,
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_TaxForm_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TaxForm_REMSId",
                table: "REMS_TaxForm",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TaxForm_TaxFormId",
                table: "REMS_TaxForm",
                column: "TaxFormId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TaxForm_TenantId",
                table: "REMS_TaxForm",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TaxForm_TenantId_REMSId_TaxFormId",
                table: "REMS_TaxForm",
                columns: new[] { "TenantId", "REMSId", "TaxFormId" },
                unique: true,
                filter: "[Deleted] = 0");
        }
    }
}
