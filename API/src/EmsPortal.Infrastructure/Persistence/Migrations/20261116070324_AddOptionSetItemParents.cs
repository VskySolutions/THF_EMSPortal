using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOptionSetItemParents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OptionSetItems_OptionSetItems_ParentItemId",
                table: "OptionSetItems");

            migrationBuilder.DropIndex(
                name: "IX_OptionSetItems_ParentItemId",
                table: "OptionSetItems");

            migrationBuilder.DropColumn(
                name: "ParentItemId",
                table: "OptionSetItems");

            migrationBuilder.CreateTable(
                name: "OptionSetItemParents",
                columns: table => new
                {
                    OptionSetItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentOptionSetItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptionSetItemParents", x => new { x.OptionSetItemId, x.ParentOptionSetItemId });
                    table.ForeignKey(
                        name: "FK_OptionSetItemParents_OptionSetItems_OptionSetItemId",
                        column: x => x.OptionSetItemId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OptionSetItemParents_OptionSetItems_ParentOptionSetItemId",
                        column: x => x.ParentOptionSetItemId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetItemParents_ParentOptionSetItemId",
                table: "OptionSetItemParents",
                column: "ParentOptionSetItemId");

            // THF's lists of 16 Nov 2026, on every copy of the five lists (the standard one and each tenant's).
            // A label is only changed while it still reads as seeded, so a tenant's own wording stays.
            migrationBuilder.Sql("""
                UPDATE i SET i.Label = v.NewLabel, i.UpdatedOnUtc = SYSUTCDATETIME()
                FROM OptionSetItems i
                JOIN OptionSets s ON s.Id = i.OptionSetId
                JOIN (VALUES
                    (N'REMS.EntityType', N'trust_estate', N'Trust and Estate', N'Trust/Estate'),
                    (N'REMS.ServiceLine', N'plan_administration', N'Plan Administration', N'Employee Benefits Plan Administration'),
                    (N'REMS.ServiceLine', N'employee_benefits', N'Employee Benefits', N'Employee Benefits Audit'),
                    (N'REMS.JobType', N'plan_administration_and_5500', N'Plan Administration and 5500', N'EBP Administration and 5500')
                ) v (SetKey, ItemValue, OldLabel, NewLabel)
                    ON v.SetKey = s.[Key] AND v.ItemValue = i.[Value] AND v.OldLabel = i.Label
                WHERE i.Deleted = 0;
                """);

            // The three values the lists gain. No Maconomy number: the tenant sets it under Option Sets.
            migrationBuilder.Sql("""
                DECLARE @new TABLE (SetKey nvarchar(100), ItemValue nvarchar(100), Label nvarchar(200), SortOrder int);
                INSERT INTO @new VALUES
                    (N'REMS.ServiceLine', N'business_valuation', N'Business Valuation', 5),
                    (N'REMS.ServiceLine', N'peer_review', N'Peer Review', 9),
                    (N'REMS.JobType', N'ebp_audit', N'EBP Audit', 15);

                -- Where an earlier seed left one on the list but hidden, it is offered again.
                UPDATE i SET i.IsActive = 1, i.UpdatedOnUtc = SYSUTCDATETIME()
                FROM OptionSetItems i
                JOIN OptionSets s ON s.Id = i.OptionSetId
                JOIN @new n ON n.SetKey = s.[Key] AND n.ItemValue = i.[Value]
                WHERE i.Deleted = 0 AND i.IsActive = 0;

                INSERT INTO OptionSetItems
                    (Id, OptionSetId, TenantId, [Value], Label, SortOrder, IsDefault, IsActive, IsSystem, CreatedOnUtc, UpdatedOnUtc, Deleted)
                SELECT NEWID(), s.Id, s.TenantId, n.ItemValue, n.Label, n.SortOrder, 0, 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), 0
                FROM OptionSets s
                JOIN @new n ON n.SetKey = s.[Key]
                WHERE s.Deleted = 0
                  AND NOT EXISTS (SELECT 1 FROM OptionSetItems i
                                  WHERE i.OptionSetId = s.Id AND i.[Value] = n.ItemValue AND i.Deleted = 0);
                """);

            // The values THF's lists no longer carry. Deleted where nothing points at one; where a record
            // still does, the foreign key refuses the delete and the value is hidden instead.
            migrationBuilder.Sql("""
                DECLARE @id uniqueidentifier;
                DECLARE old_values CURSOR LOCAL FAST_FORWARD FOR
                    SELECT i.Id
                    FROM OptionSetItems i
                    JOIN OptionSets s ON s.Id = i.OptionSetId
                    JOIN (VALUES
                        (N'REMS.ServiceLine', N'outsourced_cfo'),
                        (N'REMS.ServiceLine', N'internal_it'),
                        (N'REMS.JobType', N'5500'),
                        (N'REMS.Industry', N'government'),
                        (N'REMS.EntityType', N'business')
                    ) v (SetKey, ItemValue) ON v.SetKey = s.[Key] AND v.ItemValue = i.[Value];
                OPEN old_values;
                FETCH NEXT FROM old_values INTO @id;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    BEGIN TRY
                        DELETE FROM OptionSetItems WHERE Id = @id;
                    END TRY
                    BEGIN CATCH
                        UPDATE OptionSetItems SET IsActive = 0, UpdatedOnUtc = SYSUTCDATETIME() WHERE Id = @id;
                    END CATCH;
                    FETCH NEXT FROM old_values INTO @id;
                END;
                CLOSE old_values;
                DEALLOCATE old_values;
                """);

            // Which values each parent offers. A copy is linked to the tenant's own parent list where it has
            // one, the standard list otherwise.
            migrationBuilder.Sql("""
                DECLARE @map TABLE (ChildKey nvarchar(100), ParentKey nvarchar(100), ParentValue nvarchar(100), ChildValue nvarchar(100));
                INSERT INTO @map VALUES
                    (N'REMS.Industry', N'REMS.EntityType', N'individual', N'individual'),
                    (N'REMS.Industry', N'REMS.EntityType', N'government', N'state_government'),
                    (N'REMS.Industry', N'REMS.EntityType', N'government', N'local_government'),
                    (N'REMS.Industry', N'REMS.EntityType', N'government', N'federal_government'),
                    (N'REMS.Industry', N'REMS.EntityType', N'not_for_profit', N'health_care'),
                    (N'REMS.Industry', N'REMS.EntityType', N'not_for_profit', N'educational_institutions'),
                    (N'REMS.Industry', N'REMS.EntityType', N'not_for_profit', N'trade_associations'),
                    (N'REMS.Industry', N'REMS.EntityType', N'not_for_profit', N'charitable_organizations_foundations'),
                    (N'REMS.Industry', N'REMS.EntityType', N'not_for_profit', N'other_not_for_profit'),
                    (N'REMS.Industry', N'REMS.EntityType', N'insurance', N'insurance_health'),
                    (N'REMS.Industry', N'REMS.EntityType', N'insurance', N'insurance_property_casualty'),
                    (N'REMS.Industry', N'REMS.EntityType', N'insurance', N'insurance_life'),
                    (N'REMS.Industry', N'REMS.EntityType', N'insurance', N'insurance_other'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'affordable_housing'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'agribusiness'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'auto_dealers'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'construction'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'entertainment'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'financial_institutions_banking'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'hospitality'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'manufacturing'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'professional_service_firms'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'real_estate'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'retail'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'health_care'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'oil_gas_distribution'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'wholesale'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'technology'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'educational_institutions'),
                    (N'REMS.Industry', N'REMS.EntityType', N'commercial', N'distribution'),
                    (N'REMS.Industry', N'REMS.EntityType', N'trust_estate', N'trust_estate'),

                    (N'REMS.ServiceLine', N'REMS.Department', N'assurance', N'attest_services'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'assurance', N'consulting'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'assurance', N'business_valuation'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'assurance', N'it_services'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'assurance', N'mergers_acquisitions'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'assurance', N'peer_review'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'assurance', N'soc'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'assurance', N'employee_benefits'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'assurance', N'litigation_support'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'assurance', N'forensic_accounting'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'cas', N'client_accounting_services'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'cas', N'consulting'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'cas', N'business_valuation'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'cas', N'it_services'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'cas', N'mergers_acquisitions'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'tax', N'tax_compliance'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'tax', N'consulting'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'tax', N'business_valuation'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'tax', N'it_services'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'tax', N'plan_administration'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'tax', N'estate_planning'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'tax', N'litigation_support'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'tax', N'forensic_accounting'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'tax', N'mergers_acquisitions'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'gcs', N'consulting'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'gcs', N'business_valuation'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'gcs', N'it_services'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'gcs', N'mergers_acquisitions'),
                    (N'REMS.ServiceLine', N'REMS.Department', N'admin', N'non_chargeable_internal'),

                    (N'REMS.JobType', N'REMS.ServiceLine', N'attest_services', N'agreed_upon_procedures'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'attest_services', N'audit'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'attest_services', N'examination'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'attest_services', N'compilation'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'attest_services', N'review'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'tax_compliance', N'1040'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'tax_compliance', N'1041_estate'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'tax_compliance', N'1120_pc'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'tax_compliance', N'990'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'tax_compliance', N'1065'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'tax_compliance', N'1041_trust'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'tax_compliance', N'1120'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'tax_compliance', N'1120_s'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'tax_compliance', N'tax_other'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'tax_compliance', N'706'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'tax_compliance', N'709'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'tax_compliance', N'8752'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'client_accounting_services', N'client_accounting_services'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'consulting', N'consulting'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'consulting', N'governmental_consulting_services'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'business_valuation', N'business_valuation'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'it_services', N'it_services'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'plan_administration', N'plan_administration_and_5500'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'mergers_acquisitions', N'mergers_acquisitions'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'peer_review', N'peer_review'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'soc', N'soc'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'employee_benefits', N'ebp_audit'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'estate_planning', N'estate_planning'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'litigation_support', N'litigation_support'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'forensic_accounting', N'forensic_accounting'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_administrative'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_internal_firm_accounting'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_leave_time'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_personnel'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_internal_it'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_recruiting'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_education'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_professional'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_marketing'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_chamber_of_commerce_conference'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_client_social'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_finrep'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_firm_forum'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_synergy'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_shareholder_meetings'),
                    (N'REMS.JobType', N'REMS.ServiceLine', N'non_chargeable_internal', N'nonchargeable_holiday_party');

                DECLARE @pairs TABLE (ChildSetId uniqueidentifier, ParentSetId uniqueidentifier, ChildKey nvarchar(100));
                INSERT INTO @pairs
                SELECT c.Id,
                       (SELECT TOP 1 p.Id FROM OptionSets p
                        WHERE p.[Key] = k.ParentKey AND p.EntityType = c.EntityType AND p.Deleted = 0
                          AND (p.TenantId = c.TenantId OR p.TenantId IS NULL)
                        ORDER BY CASE WHEN p.TenantId IS NULL THEN 1 ELSE 0 END),
                       k.ChildKey
                FROM OptionSets c
                JOIN (SELECT DISTINCT ChildKey, ParentKey FROM @map) k ON k.ChildKey = c.[Key]
                WHERE c.Deleted = 0 AND c.ParentSetId IS NULL;

                INSERT INTO OptionSetItemParents (OptionSetItemId, ParentOptionSetItemId)
                SELECT DISTINCT ci.Id, pi.Id
                FROM @pairs x
                JOIN @map m ON m.ChildKey = x.ChildKey
                JOIN OptionSetItems ci ON ci.OptionSetId = x.ChildSetId AND ci.[Value] = m.ChildValue AND ci.Deleted = 0
                JOIN OptionSetItems pi ON pi.OptionSetId = x.ParentSetId AND pi.[Value] = m.ParentValue AND pi.Deleted = 0;

                UPDATE c SET c.ParentSetId = x.ParentSetId, c.UpdatedOnUtc = SYSUTCDATETIME()
                FROM OptionSets c
                JOIN @pairs x ON x.ChildSetId = c.Id
                WHERE x.ParentSetId IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OptionSetItemParents");

            migrationBuilder.AddColumn<Guid>(
                name: "ParentItemId",
                table: "OptionSetItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetItems_ParentItemId",
                table: "OptionSetItems",
                column: "ParentItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_OptionSetItems_OptionSetItems_ParentItemId",
                table: "OptionSetItems",
                column: "ParentItemId",
                principalTable: "OptionSetItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
