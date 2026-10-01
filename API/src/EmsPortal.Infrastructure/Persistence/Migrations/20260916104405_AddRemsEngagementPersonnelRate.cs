using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A GCS engagement is priced at every personnel level it is staffed at, not one. The single
    /// level-and-rate pair on the government detail becomes a rate-card table hanging off it, and the
    /// pair each engagement already carried is copied across as the first line of its card before the
    /// two columns go — where both halves were answered, since a level with no rate priced nothing.
    /// </summary>
    public partial class AddRemsEngagementPersonnelRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "REMSEngagementPersonnelRate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSEngagementGovernmentDetailId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonnelLevelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillRatePerHour = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSEngagementPersonnelRate", x => x.Id);
                    table.CheckConstraint("CK_REMSEngagementPersonnelRate_BillRatePerHour", "[BillRatePerHour] >= 0");
                    table.ForeignKey(
                        name: "FK_REMSEngagementPersonnelRate_OptionSetItems_PersonnelLevelId",
                        column: x => x.PersonnelLevelId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementPersonnelRate_REMSEngagementGovernmentDetail_REMSEngagementGovernmentDetailId",
                        column: x => x.REMSEngagementGovernmentDetailId,
                        principalTable: "REMSEngagementGovernmentDetail",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementPersonnelRate_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_REMSEngagementGovernmentDetail_PurchaseOrderAmount",
                table: "REMSEngagementGovernmentDetail",
                sql: "[PurchaseOrderAmount] IS NULL OR [PurchaseOrderAmount] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementPersonnelRate_PersonnelLevelId",
                table: "REMSEngagementPersonnelRate",
                column: "PersonnelLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementPersonnelRate_REMSEngagementGovernmentDetailId",
                table: "REMSEngagementPersonnelRate",
                column: "REMSEngagementGovernmentDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementPersonnelRate_TenantId",
                table: "REMSEngagementPersonnelRate",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementPersonnelRate_TenantId_REMSEngagementGovernmentDetailId_PersonnelLevelId",
                table: "REMSEngagementPersonnelRate",
                columns: new[] { "TenantId", "REMSEngagementGovernmentDetailId", "PersonnelLevelId" },
                unique: true,
                filter: "[Deleted] = 0");

            // The pair becomes the first line of the card, stamped with the save that wrote it.
            migrationBuilder.Sql(
                """
                INSERT INTO [REMSEngagementPersonnelRate]
                    ([Id], [TenantId], [REMSEngagementGovernmentDetailId], [PersonnelLevelId], [BillRatePerHour],
                     [CreatedById], [CreatedOnUtc], [UpdatedById], [UpdatedOnUtc], [Deleted], [DeletedOnUtc])
                SELECT NEWID(), d.[TenantId], d.[Id], d.[PersonnelLevelId], d.[BillRatePerHour],
                       d.[UpdatedById], d.[UpdatedOnUtc], d.[UpdatedById], d.[UpdatedOnUtc], 0, NULL
                FROM [REMSEngagementGovernmentDetail] d
                WHERE d.[Deleted] = 0 AND d.[PersonnelLevelId] IS NOT NULL AND d.[BillRatePerHour] IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_REMSEngagementGovernmentDetail_OptionSetItems_PersonnelLevelId",
                table: "REMSEngagementGovernmentDetail");

            migrationBuilder.DropIndex(
                name: "IX_REMSEngagementGovernmentDetail_PersonnelLevelId",
                table: "REMSEngagementGovernmentDetail");

            migrationBuilder.DropCheckConstraint(
                name: "CK_REMSEngagementGovernmentDetail_PoAmounts",
                table: "REMSEngagementGovernmentDetail");

            migrationBuilder.DropColumn(
                name: "BillRatePerHour",
                table: "REMSEngagementGovernmentDetail");

            migrationBuilder.DropColumn(
                name: "PersonnelLevelId",
                table: "REMSEngagementGovernmentDetail");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_REMSEngagementGovernmentDetail_PurchaseOrderAmount",
                table: "REMSEngagementGovernmentDetail");

            migrationBuilder.AddColumn<decimal>(
                name: "BillRatePerHour",
                table: "REMSEngagementGovernmentDetail",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PersonnelLevelId",
                table: "REMSEngagementGovernmentDetail",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementGovernmentDetail_PersonnelLevelId",
                table: "REMSEngagementGovernmentDetail",
                column: "PersonnelLevelId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_REMSEngagementGovernmentDetail_PoAmounts",
                table: "REMSEngagementGovernmentDetail",
                sql: "([PurchaseOrderAmount] IS NULL OR [PurchaseOrderAmount] >= 0) AND ([BillRatePerHour] IS NULL OR [BillRatePerHour] >= 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_REMSEngagementGovernmentDetail_OptionSetItems_PersonnelLevelId",
                table: "REMSEngagementGovernmentDetail",
                column: "PersonnelLevelId",
                principalTable: "OptionSetItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // The pair holds one line, so each card's first goes back and the rest are lost with the table.
            migrationBuilder.Sql(
                """
                UPDATE d
                SET d.[PersonnelLevelId] = r.[PersonnelLevelId], d.[BillRatePerHour] = r.[BillRatePerHour]
                FROM [REMSEngagementGovernmentDetail] d
                CROSS APPLY (
                    SELECT TOP 1 x.[PersonnelLevelId], x.[BillRatePerHour]
                    FROM [REMSEngagementPersonnelRate] x
                    WHERE x.[REMSEngagementGovernmentDetailId] = d.[Id] AND x.[Deleted] = 0
                    ORDER BY x.[CreatedOnUtc]) r;
                """);

            migrationBuilder.DropTable(
                name: "REMSEngagementPersonnelRate");
        }
    }
}
