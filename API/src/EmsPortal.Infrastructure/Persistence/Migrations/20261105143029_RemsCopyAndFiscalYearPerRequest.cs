using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemsCopyAndFiscalYearPerRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CopiedFromRemsId",
                table: "REMS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FiscalYearEnd",
                table: "REMS",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_REMS_CopiedFromRemsId",
                table: "REMS",
                column: "CopiedFromRemsId",
                filter: "[CopiedFromRemsId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_REMS_REMS_CopiedFromRemsId",
                table: "REMS",
                column: "CopiedFromRemsId",
                principalTable: "REMS",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // The year end moves off the client onto the engagement: every tax engagement so far takes the one its client held.
            // EXEC defers compilation past the ADD COLUMN above when the migration runs as one scripted batch.
            migrationBuilder.Sql(@"
EXEC(N'UPDATE r SET r.FiscalYearEnd = c.FiscalYearEnd
FROM [REMS] r
JOIN [Client] c ON c.Id = r.ClientId
WHERE r.FiscalYearEnd IS NULL AND c.FiscalYearEnd IS NOT NULL
  AND (EXISTS (SELECT 1 FROM [REMS_TaxForm] f WHERE f.REMSId = r.Id AND f.Deleted = 0)
       OR EXISTS (SELECT 1 FROM [REMS_TaxDueDate] d WHERE d.REMSId = r.Id AND d.Deleted = 0));');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_REMS_REMS_CopiedFromRemsId",
                table: "REMS");

            migrationBuilder.DropIndex(
                name: "IX_REMS_CopiedFromRemsId",
                table: "REMS");

            migrationBuilder.DropColumn(
                name: "CopiedFromRemsId",
                table: "REMS");

            migrationBuilder.DropColumn(
                name: "FiscalYearEnd",
                table: "REMS");
        }
    }
}
