using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRemsIntakeOnce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IntakeCompletedByUserId",
                table: "REMS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IntakeOnFileFromRemsId",
                table: "REMS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IntakeSubmittedByUserId",
                table: "Client",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IntakeSubmittedOnUtc",
                table: "Client",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IntakeSubmittedRemsId",
                table: "Client",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_REMS_IntakeCompletedByUserId",
                table: "REMS",
                column: "IntakeCompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_IntakeOnFileFromRemsId",
                table: "REMS",
                column: "IntakeOnFileFromRemsId",
                filter: "[IntakeOnFileFromRemsId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Client_IntakeSubmittedByUserId",
                table: "Client",
                column: "IntakeSubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_IntakeSubmittedRemsId",
                table: "Client",
                column: "IntakeSubmittedRemsId");

            migrationBuilder.AddForeignKey(
                name: "FK_Client_REMS_IntakeSubmittedRemsId",
                table: "Client",
                column: "IntakeSubmittedRemsId",
                principalTable: "REMS",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Client_Users_IntakeSubmittedByUserId",
                table: "Client",
                column: "IntakeSubmittedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_REMS_REMS_IntakeOnFileFromRemsId",
                table: "REMS",
                column: "IntakeOnFileFromRemsId",
                principalTable: "REMS",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_REMS_Users_IntakeCompletedByUserId",
                table: "REMS",
                column: "IntakeCompletedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // A client whose form ever came back has their intake on file from the earliest such request; a copy
            // that was submitted for review on it (no invite of its own) is marked as having reused it.
            // EXEC defers compilation past the ADD COLUMNs above when the migration runs as one scripted batch.
            migrationBuilder.Sql(@"
EXEC(N'
;WITH first_intake AS (
    SELECT r.ClientId, r.Id AS RemsId, r.FormSubmittedOnUtc,
           ROW_NUMBER() OVER (PARTITION BY r.ClientId ORDER BY r.FormSubmittedOnUtc, r.CreatedOnUtc) AS rn
    FROM [REMS] r
    WHERE r.Deleted = 0 AND r.FormSubmittedOnUtc IS NOT NULL AND r.InviteCode IS NOT NULL)
UPDATE c SET c.IntakeSubmittedOnUtc = f.FormSubmittedOnUtc, c.IntakeSubmittedRemsId = f.RemsId
FROM [Client] c
JOIN first_intake f ON f.ClientId = c.Id AND f.rn = 1
WHERE c.IntakeSubmittedOnUtc IS NULL;
UPDATE r SET r.IntakeOnFileFromRemsId = c.IntakeSubmittedRemsId
FROM [REMS] r
JOIN [Client] c ON c.Id = r.ClientId
WHERE r.Deleted = 0 AND r.CopiedFromRemsId IS NOT NULL AND r.FormSubmittedOnUtc IS NOT NULL AND r.InviteCode IS NULL
  AND r.IntakeOnFileFromRemsId IS NULL AND c.IntakeSubmittedRemsId IS NOT NULL AND c.IntakeSubmittedRemsId <> r.Id;');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Client_REMS_IntakeSubmittedRemsId",
                table: "Client");

            migrationBuilder.DropForeignKey(
                name: "FK_Client_Users_IntakeSubmittedByUserId",
                table: "Client");

            migrationBuilder.DropForeignKey(
                name: "FK_REMS_REMS_IntakeOnFileFromRemsId",
                table: "REMS");

            migrationBuilder.DropForeignKey(
                name: "FK_REMS_Users_IntakeCompletedByUserId",
                table: "REMS");

            migrationBuilder.DropIndex(
                name: "IX_REMS_IntakeCompletedByUserId",
                table: "REMS");

            migrationBuilder.DropIndex(
                name: "IX_REMS_IntakeOnFileFromRemsId",
                table: "REMS");

            migrationBuilder.DropIndex(
                name: "IX_Client_IntakeSubmittedByUserId",
                table: "Client");

            migrationBuilder.DropIndex(
                name: "IX_Client_IntakeSubmittedRemsId",
                table: "Client");

            migrationBuilder.DropColumn(
                name: "IntakeCompletedByUserId",
                table: "REMS");

            migrationBuilder.DropColumn(
                name: "IntakeOnFileFromRemsId",
                table: "REMS");

            migrationBuilder.DropColumn(
                name: "IntakeSubmittedByUserId",
                table: "Client");

            migrationBuilder.DropColumn(
                name: "IntakeSubmittedOnUtc",
                table: "Client");

            migrationBuilder.DropColumn(
                name: "IntakeSubmittedRemsId",
                table: "Client");
        }
    }
}
