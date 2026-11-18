using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MaconomyNumberOnOptionItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaconomyJobTemplates");

            migrationBuilder.DropColumn(
                name: "JobDefaultTemplateNumber",
                table: "MaconomyConnections");

            migrationBuilder.DropColumn(
                name: "JobProjectManagerNumber",
                table: "MaconomyConnections");

            migrationBuilder.AddColumn<string>(
                name: "MaconomyNumber",
                table: "OptionSetItems",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            // Today's numbers onto every existing REMS Entity Type and REMS Industry value — the standard
            // list and every tenant's copy — so the first start after this migration sends and reads exactly
            // what the hard-coded tables did. Job templates get none: THF supplies those under Option Sets.
            migrationBuilder.Sql("""
                UPDATE i SET i.MaconomyNumber = v.Number
                FROM OptionSetItems i
                JOIN OptionSets s ON s.Id = i.OptionSetId
                JOIN (VALUES
                    ('REMS.EntityType', 'individual', '1'),
                    ('REMS.EntityType', 'government', '2'),
                    ('REMS.EntityType', 'not_for_profit', '3'),
                    ('REMS.EntityType', 'insurance', '4'),
                    ('REMS.EntityType', 'commercial', '5'),
                    ('REMS.EntityType', 'trust_estate', '6'),
                    ('REMS.Industry', 'manufacturing', '17'),
                    ('REMS.Industry', 'professional_service_firms', '18'),
                    ('REMS.Industry', 'real_estate', '19'),
                    ('REMS.Industry', 'retail', '20'),
                    ('REMS.Industry', 'health_care', '21'),
                    ('REMS.Industry', 'oil_gas_distribution', '22'),
                    ('REMS.Industry', 'wholesale', '23'),
                    ('REMS.Industry', 'technology', '24'),
                    ('REMS.Industry', 'state_government', '25'),
                    ('REMS.Industry', 'local_government', '26'),
                    ('REMS.Industry', 'federal_government', '27'),
                    ('REMS.Industry', 'educational_institutions', '28'),
                    ('REMS.Industry', 'insurance_health', '29'),
                    ('REMS.Industry', 'insurance_property_casualty', '30'),
                    ('REMS.Industry', 'insurance_life', '31'),
                    ('REMS.Industry', 'insurance_other', '32'),
                    ('REMS.Industry', 'trade_associations', '33'),
                    ('REMS.Industry', 'charitable_organizations_foundations', '34'),
                    ('REMS.Industry', 'other_not_for_profit', '35')
                ) v (SetKey, ItemValue, Number) ON s.[Key] = v.SetKey AND i.[Value] = v.ItemValue
                WHERE i.MaconomyNumber IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaconomyNumber",
                table: "OptionSetItems");

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

            migrationBuilder.CreateTable(
                name: "MaconomyJobTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TemplateJobNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
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
                name: "IX_MaconomyJobTemplates_JobTemplateId",
                table: "MaconomyJobTemplates",
                column: "JobTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MaconomyJobTemplates_TenantId_JobTemplateId",
                table: "MaconomyJobTemplates",
                columns: new[] { "TenantId", "JobTemplateId" },
                unique: true,
                filter: "[Deleted] = 0");
        }
    }
}
