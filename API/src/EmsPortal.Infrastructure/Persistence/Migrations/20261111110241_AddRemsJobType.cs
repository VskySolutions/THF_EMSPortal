using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Job Type in place of Job Template on the engagement setup (11 Nov 2026): the REMS.JobType list is
    /// seeded here rather than left to the bootstrap seeder, because the column is remapped in the same
    /// step — every request that named a template now names the job type that stands for it, Tax
    /// Compliance by the request's tax form — and the REMS.JobTemplate list is then retired. The one
    /// template number already set (Audit) is carried across.
    /// </summary>
    public partial class AddRemsJobType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. The list, platform-level, insert-if-missing (so a catalog the seeder already reached is left alone).
            migrationBuilder.Sql("""
                DECLARE @now datetime2 = SYSUTCDATETIME();
                DECLARE @setId uniqueidentifier = (SELECT TOP 1 Id FROM OptionSets WHERE TenantId IS NULL AND [Key] = N'REMS.JobType' AND Deleted = 0);
                IF @setId IS NULL
                BEGIN
                    SET @setId = NEWID();
                    INSERT INTO OptionSets (Id, TenantId, EntityType, [Key], Name, ParentSetId, ItemSortMode, IsSystem, IsClosed, IsActive, CreatedOnUtc, UpdatedOnUtc, Deleted)
                    VALUES (@setId, NULL, 6, N'REMS.JobType', N'REMS Job Type', NULL, N'Custom', 1, 0, 1, @now, @now, 0);
                END;

                DECLARE @items TABLE (Value nvarchar(100), Label nvarchar(200), SortOrder int);
                INSERT INTO @items VALUES
                    (N'agreed_upon_procedures', N'Agreed Upon Procedures', 1),
                    (N'audit', N'Audit', 2),
                    (N'examination', N'Examination', 3),
                    (N'compilation', N'Compilation', 4),
                    (N'review', N'Review', 5),
                    (N'soc', N'SOC', 6),
                    (N'1040', N'1040 - Individual', 7),
                    (N'1041_estate', N'1041 - Estate', 8),
                    (N'1120_pc', N'1120-PC - Property & Casualty Insurance', 9),
                    (N'990', N'990 - Tax-Exempt', 10),
                    (N'1065', N'1065 - Partnership', 11),
                    (N'1041_trust', N'1041 - Trust', 12),
                    (N'1120', N'1120 - C Corporation', 13),
                    (N'1120_s', N'1120-S - S Corporation', 14),
                    (N'5500', N'5500 - Employee Benefit Plan', 15),
                    (N'tax_other', N'Tax Other', 16),
                    (N'706', N'706 - Estate', 17),
                    (N'709', N'709 - Gift', 18),
                    (N'8752', N'8752 - Tax', 19),
                    (N'estate_planning', N'Estate Planning', 20),
                    (N'consulting', N'Consulting', 21),
                    (N'governmental_consulting_services', N'Governmental Consulting Services', 22),
                    (N'plan_administration_and_5500', N'Plan Administration and 5500', 23),
                    (N'client_accounting_services', N'Client Accounting Services', 24),
                    (N'litigation_support', N'Litigation Support', 25),
                    (N'forensic_accounting', N'Forensic Accounting', 26),
                    (N'business_valuation', N'Business Valuation', 27),
                    (N'it_services', N'IT Services', 28),
                    (N'mergers_acquisitions', N'Mergers & Acquisitions', 29),
                    (N'peer_review', N'Peer Review', 30),
                    (N'nonchargeable_administrative', N'Nonchargeable - Administrative', 31),
                    (N'nonchargeable_internal_firm_accounting', N'NONCHARGEABLE - INTERNAL FIRM ACCOUNTING', 32),
                    (N'nonchargeable_leave_time', N'Nonchargeable - Leave Time', 33),
                    (N'nonchargeable_personnel', N'Nonchargeable - Personnel', 34),
                    (N'nonchargeable_internal_it', N'NONCHARGEABLE - INTERNAL IT', 35),
                    (N'nonchargeable_recruiting', N'Nonchargeable - Recruiting', 36),
                    (N'nonchargeable_education', N'Nonchargeable - Education', 37),
                    (N'nonchargeable_professional', N'Nonchargeable - Professional', 38),
                    (N'nonchargeable_marketing', N'Nonchargeable - Marketing', 39),
                    (N'nonchargeable_chamber_of_commerce_conference', N'Nonchargeable - Chamber of Commerce Conference', 40),
                    (N'nonchargeable_client_social', N'Nonchargeable - Client Social', 41),
                    (N'nonchargeable_finrep', N'Nonchargeable - FINREP', 42),
                    (N'nonchargeable_firm_forum', N'Nonchargeable - Firm Forum', 43),
                    (N'nonchargeable_synergy', N'Nonchargeable - Synergy', 44),
                    (N'nonchargeable_shareholder_meetings', N'Nonchargeable - Shareholder Meetings', 45),
                    (N'nonchargeable_holiday_party', N'Nonchargeable - Holiday Party', 46);

                INSERT INTO OptionSetItems (Id, OptionSetId, TenantId, ParentItemId, Value, Label, Description, SortOrder, IsDefault, IsActive,
                                            BackgroundColor, TextColor, Icon, IsSystem, MetadataJson, MaconomyNumber, CreatedOnUtc, UpdatedOnUtc, Deleted)
                SELECT NEWID(), @setId, NULL, NULL, i.Value, i.Label, NULL, i.SortOrder, 0, 1, NULL, NULL, NULL, 0, NULL, NULL, @now, @now, 0
                FROM @items i
                WHERE NOT EXISTS (SELECT 1 FROM OptionSetItems x WHERE x.OptionSetId = @setId AND x.TenantId IS NULL AND x.Value = i.Value AND x.Deleted = 0);
                """);

            // 2. The column, index and foreign key, renamed in place: the rows keep pointing at template items until step 3.
            migrationBuilder.DropForeignKey(
                name: "FK_REMS_OptionSetItems_JobTemplateId",
                table: "REMS");

            migrationBuilder.RenameColumn(
                name: "JobTemplateId",
                table: "REMS",
                newName: "JobTypeId");

            migrationBuilder.RenameIndex(
                name: "IX_REMS_JobTemplateId",
                table: "REMS",
                newName: "IX_REMS_JobTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_REMS_OptionSetItems_JobTypeId",
                table: "REMS",
                column: "JobTypeId",
                principalTable: "OptionSetItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // 3. Every request that named a template names the job type that stands for it. The Tax Compliance
            //    template is the job type of the request's tax form (Tax Other without one). A template value
            //    with no counterpart — a tenant's own addition — is left empty rather than guessed.
            //    Then the template's Maconomy number moves to the job type where it has none, and the old list goes.
            migrationBuilder.Sql("""
                DECLARE @now datetime2 = SYSUTCDATETIME();
                DECLARE @setId uniqueidentifier = (SELECT TOP 1 Id FROM OptionSets WHERE TenantId IS NULL AND [Key] = N'REMS.JobType' AND Deleted = 0);

                DECLARE @map TABLE (OldValue nvarchar(100), NewValue nvarchar(100));
                INSERT INTO @map VALUES
                    (N'consulting_engagement', N'consulting'),
                    (N'mergers_acquisitions', N'mergers_acquisitions'),
                    (N'pension_administration_tax_compliance', N'plan_administration_and_5500'),
                    (N'business_valuation', N'business_valuation'),
                    (N'agreed_upon_procedures', N'agreed_upon_procedures'),
                    (N'audit', N'audit'),
                    (N'examination', N'examination'),
                    (N'compilation', N'compilation'),
                    (N'review', N'review'),
                    (N'peer_review', N'peer_review'),
                    (N'information_technology_services', N'it_services'),
                    (N'soc', N'soc'),
                    (N'litigation', N'litigation_support'),
                    (N'forensic_accounting', N'forensic_accounting'),
                    (N'governmental_consulting_services', N'governmental_consulting_services'),
                    (N'client_accounting_services', N'client_accounting_services');

                UPDATE r SET r.JobTypeId = n.Id
                FROM REMS r
                JOIN OptionSetItems o ON o.Id = r.JobTypeId
                JOIN OptionSets os ON os.Id = o.OptionSetId AND os.[Key] = N'REMS.JobTemplate'
                JOIN @map m ON m.OldValue = o.Value
                JOIN OptionSetItems n ON n.OptionSetId = @setId AND n.Value = m.NewValue AND n.Deleted = 0;

                UPDATE r SET r.JobTypeId = n.Id
                FROM REMS r
                JOIN OptionSetItems o ON o.Id = r.JobTypeId
                JOIN OptionSets os ON os.Id = o.OptionSetId AND os.[Key] = N'REMS.JobTemplate'
                OUTER APPLY (
                    SELECT TOP 1 tf.Value
                    FROM REMS_TaxForm t JOIN OptionSetItems tf ON tf.Id = t.TaxFormId
                    WHERE t.REMSId = r.Id AND t.Deleted = 0
                    ORDER BY tf.SortOrder) f
                JOIN OptionSetItems n ON n.OptionSetId = @setId AND n.Deleted = 0
                    AND n.Value = CASE WHEN f.Value IN (N'1040', N'1041_trust', N'1041_estate', N'1065', N'1120', N'1120_pc', N'1120_s', N'990', N'5500')
                                       THEN f.Value ELSE N'tax_other' END
                WHERE o.Value = N'tax_compliance';

                UPDATE r SET r.JobTypeId = NULL
                FROM REMS r
                JOIN OptionSetItems o ON o.Id = r.JobTypeId
                JOIN OptionSets os ON os.Id = o.OptionSetId AND os.[Key] = N'REMS.JobTemplate';

                UPDATE n SET n.MaconomyNumber = o.MaconomyNumber, n.UpdatedOnUtc = @now
                FROM OptionSetItems n
                JOIN @map m ON m.NewValue = n.Value
                JOIN OptionSetItems o ON o.Value = m.OldValue AND o.MaconomyNumber IS NOT NULL AND o.Deleted = 0
                JOIN OptionSets os ON os.Id = o.OptionSetId AND os.[Key] = N'REMS.JobTemplate' AND os.TenantId IS NULL
                WHERE n.OptionSetId = @setId AND n.MaconomyNumber IS NULL;

                UPDATE i SET i.Deleted = 1, i.DeletedOnUtc = @now, i.UpdatedOnUtc = @now
                FROM OptionSetItems i JOIN OptionSets s ON s.Id = i.OptionSetId
                WHERE s.[Key] = N'REMS.JobTemplate' AND i.Deleted = 0;

                UPDATE OptionSets SET Deleted = 1, DeletedOnUtc = @now, UpdatedOnUtc = @now
                WHERE [Key] = N'REMS.JobTemplate' AND Deleted = 0;
                """);

            // 4. THF's template jobs onto the job types that stand for them (the probe of 11 Nov 2026: `template =
            //    true` on the jobs container lists 38, numbered "ZZ" + the job's name). Only where none is set yet.
            //    Every tax form takes the one tax template, ZZTax Compliance (the user, 11 Nov 2026); Estate
            //    Planning and Internal IT have no template and stay empty.
            migrationBuilder.Sql("""
                DECLARE @now datetime2 = SYSUTCDATETIME();
                UPDATE i SET i.MaconomyNumber = v.Number, i.UpdatedOnUtc = @now
                FROM OptionSetItems i
                JOIN OptionSets s ON s.Id = i.OptionSetId AND s.[Key] = N'REMS.JobType'
                JOIN (VALUES
                    (N'agreed_upon_procedures', N'ZZAgreed Upon Procedures'),
                    (N'audit', N'ZZAudit'),
                    (N'examination', N'ZZExamination'),
                    (N'compilation', N'ZZCompilation'),
                    (N'review', N'ZZReview'),
                    (N'soc', N'ZZSOC'),
                    (N'consulting', N'ZZConsulting Engagement'),
                    (N'governmental_consulting_services', N'ZZGovernmental Consulting Services'),
                    (N'plan_administration_and_5500', N'ZZPension Administration and Tax Compliance'),
                    (N'client_accounting_services', N'ZZClient Accounting Services'),
                    (N'litigation_support', N'ZZLitigation'),
                    (N'forensic_accounting', N'ZZForensic Accounting'),
                    (N'business_valuation', N'ZZBusiness Valuation'),
                    (N'it_services', N'ZZInformation Technology Services'),
                    (N'mergers_acquisitions', N'ZZMergers and Acquisitions'),
                    (N'peer_review', N'ZZPeer Review'),
                    (N'nonchargeable_administrative', N'ZZNonchargeable - Administrative'),
                    (N'nonchargeable_internal_firm_accounting', N'ZZNONCHARGEABLE - INTERNAL FIRM ACCOUNTING'),
                    (N'nonchargeable_leave_time', N'ZZNonchargeable - Leave Time'),
                    (N'nonchargeable_personnel', N'ZZNonchargeable - Personnel'),
                    (N'nonchargeable_recruiting', N'ZZNonchargeable - Recruiting'),
                    (N'nonchargeable_education', N'ZZNonchargeable - Education'),
                    (N'nonchargeable_professional', N'ZZNonchargeable - Professional'),
                    (N'nonchargeable_marketing', N'ZZNonchargeable - Marketing'),
                    (N'nonchargeable_chamber_of_commerce_conference', N'ZZNonchargeable - Chamber of Commerce Conference'),
                    (N'nonchargeable_client_social', N'ZZNonchargeable - Client Social'),
                    (N'nonchargeable_finrep', N'ZZNonchargeable - FINREP'),
                    (N'nonchargeable_firm_forum', N'ZZNonchargeable - Firm Forum'),
                    (N'nonchargeable_synergy', N'ZZNonchargeable - Synergy'),
                    (N'nonchargeable_shareholder_meetings', N'ZZNonchargeable - Shareholder Meetings'),
                    (N'nonchargeable_holiday_party', N'ZZNonchargeable - Holiday Party'),
                    (N'1040', N'ZZTax Compliance'),
                    (N'1041_estate', N'ZZTax Compliance'),
                    (N'1120_pc', N'ZZTax Compliance'),
                    (N'990', N'ZZTax Compliance'),
                    (N'1065', N'ZZTax Compliance'),
                    (N'1041_trust', N'ZZTax Compliance'),
                    (N'1120', N'ZZTax Compliance'),
                    (N'1120_s', N'ZZTax Compliance'),
                    (N'5500', N'ZZTax Compliance'),
                    (N'tax_other', N'ZZTax Compliance'),
                    (N'706', N'ZZTax Compliance'),
                    (N'709', N'ZZTax Compliance'),
                    (N'8752', N'ZZTax Compliance')
                ) v (ItemValue, Number) ON i.[Value] = v.ItemValue
                WHERE i.MaconomyNumber IS NULL AND i.Deleted = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The schema only: the remapped values and the retired list are not put back.
            migrationBuilder.DropForeignKey(
                name: "FK_REMS_OptionSetItems_JobTypeId",
                table: "REMS");

            migrationBuilder.RenameColumn(
                name: "JobTypeId",
                table: "REMS",
                newName: "JobTemplateId");

            migrationBuilder.RenameIndex(
                name: "IX_REMS_JobTypeId",
                table: "REMS",
                newName: "IX_REMS_JobTemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_REMS_OptionSetItems_JobTemplateId",
                table: "REMS",
                column: "JobTemplateId",
                principalTable: "OptionSetItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
