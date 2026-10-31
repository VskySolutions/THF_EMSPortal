using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRemsMaconomyJobSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MaconomyJobNumber",
                table: "REMS",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaconomyJobSyncError",
                table: "REMS",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaconomyJobSyncStatus",
                table: "REMS",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotSynced");

            migrationBuilder.AddColumn<Guid>(
                name: "MaconomyJobSyncedById",
                table: "REMS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MaconomyJobSyncedOnUtc",
                table: "REMS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JobDefaultTemplateNumber",
                table: "MaconomyConnections",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JobProjectManagerNumber",
                table: "MaconomyConnections",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "JobSyncChangedById",
                table: "MaconomyConnections",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "JobSyncChangedOnUtc",
                table: "MaconomyConnections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "JobSyncEnabled",
                table: "MaconomyConnections",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MaconomyJobTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateJobNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaconomyJobTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaconomyJobTemplates_OptionSetItems_JobTemplateId",
                        column: x => x.JobTemplateId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaconomyJobTemplates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_MaconomyJobSyncedById",
                table: "REMS",
                column: "MaconomyJobSyncedById");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId_MaconomyJobNumber",
                table: "REMS",
                columns: new[] { "TenantId", "MaconomyJobNumber" },
                filter: "[MaconomyJobNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MaconomyJobTemplates_JobTemplateId",
                table: "MaconomyJobTemplates",
                column: "JobTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MaconomyJobTemplates_TenantId_JobTemplateId",
                table: "MaconomyJobTemplates",
                columns: new[] { "TenantId", "JobTemplateId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_REMS_Users_MaconomyJobSyncedById",
                table: "REMS",
                column: "MaconomyJobSyncedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_REMS_Users_MaconomyJobSyncedById",
                table: "REMS");

            migrationBuilder.DropTable(
                name: "MaconomyJobTemplates");

            migrationBuilder.DropIndex(
                name: "IX_REMS_MaconomyJobSyncedById",
                table: "REMS");

            migrationBuilder.DropIndex(
                name: "IX_REMS_TenantId_MaconomyJobNumber",
                table: "REMS");

            migrationBuilder.DropColumn(
                name: "MaconomyJobNumber",
                table: "REMS");

            migrationBuilder.DropColumn(
                name: "MaconomyJobSyncError",
                table: "REMS");

            migrationBuilder.DropColumn(
                name: "MaconomyJobSyncStatus",
                table: "REMS");

            migrationBuilder.DropColumn(
                name: "MaconomyJobSyncedById",
                table: "REMS");

            migrationBuilder.DropColumn(
                name: "MaconomyJobSyncedOnUtc",
                table: "REMS");

            migrationBuilder.DropColumn(
                name: "JobDefaultTemplateNumber",
                table: "MaconomyConnections");

            migrationBuilder.DropColumn(
                name: "JobProjectManagerNumber",
                table: "MaconomyConnections");

            migrationBuilder.DropColumn(
                name: "JobSyncChangedById",
                table: "MaconomyConnections");

            migrationBuilder.DropColumn(
                name: "JobSyncChangedOnUtc",
                table: "MaconomyConnections");

            migrationBuilder.DropColumn(
                name: "JobSyncEnabled",
                table: "MaconomyConnections");
        }
    }
}
