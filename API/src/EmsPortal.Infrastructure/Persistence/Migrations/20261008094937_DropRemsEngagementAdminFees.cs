using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropRemsEngagementAdminFees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_REMSEngagementAuditDetail_AdminFeesAmount",
                table: "REMSEngagementAuditDetail");

            migrationBuilder.DropColumn(
                name: "AdminFeesAmount",
                table: "REMSEngagementAuditDetail");

            migrationBuilder.DropColumn(
                name: "AdminFeesApply",
                table: "REMSEngagementAuditDetail");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdminFeesAmount",
                table: "REMSEngagementAuditDetail",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AdminFeesApply",
                table: "REMSEngagementAuditDetail",
                type: "bit",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_REMSEngagementAuditDetail_AdminFeesAmount",
                table: "REMSEngagementAuditDetail",
                sql: "[AdminFeesAmount] IS NULL OR [AdminFeesAmount] >= 0");
        }
    }
}
