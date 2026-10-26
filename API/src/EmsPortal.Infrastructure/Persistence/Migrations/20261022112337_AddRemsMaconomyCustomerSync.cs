using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRemsMaconomyCustomerSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerSyncChangedById",
                table: "MaconomyConnections",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CustomerSyncChangedOnUtc",
                table: "MaconomyConnections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CustomerSyncEnabled",
                table: "MaconomyConnections",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "REMSMaconomyCustomerSync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CustomerNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastAttemptOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SyncedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSMaconomyCustomerSync", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSMaconomyCustomerSync_Persons_ClientPersonId",
                        column: x => x.ClientPersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSMaconomyCustomerSync_REMS_REMSId",
                        column: x => x.REMSId,
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSMaconomyCustomerSync_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_REMSMaconomyCustomerSync_ClientPersonId",
                table: "REMSMaconomyCustomerSync",
                column: "ClientPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSMaconomyCustomerSync_REMSId",
                table: "REMSMaconomyCustomerSync",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSMaconomyCustomerSync_TenantId",
                table: "REMSMaconomyCustomerSync",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSMaconomyCustomerSync_TenantId_REMSId",
                table: "REMSMaconomyCustomerSync",
                columns: new[] { "TenantId", "REMSId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSMaconomyCustomerSync_TenantId_Status",
                table: "REMSMaconomyCustomerSync",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "REMSMaconomyCustomerSync");

            migrationBuilder.DropColumn(
                name: "CustomerSyncChangedById",
                table: "MaconomyConnections");

            migrationBuilder.DropColumn(
                name: "CustomerSyncChangedOnUtc",
                table: "MaconomyConnections");

            migrationBuilder.DropColumn(
                name: "CustomerSyncEnabled",
                table: "MaconomyConnections");
        }
    }
}
