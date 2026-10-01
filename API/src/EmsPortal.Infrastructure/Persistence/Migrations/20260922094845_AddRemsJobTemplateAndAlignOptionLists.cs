using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A job template on the engagement, and two REMS option lists as THF supplied them in September 2026.
    /// <para>
    /// The engagement gains <c>JobTemplateId</c>, a foreign key to a value of the new
    /// <c>REMS.JobTemplate</c> list, which is inserted for every scope holding REMS.Department — the
    /// platform-standard row and each tenant's own copy — the way REMS.PersonnelLevel was. Two lists
    /// are brought to the supplied ones: Admin is offered again in Department; Service Line gains
    /// Non-Chargeable Internal and loses Outsourced CFO, Payroll Services and the six Internal-* values.
    /// </para>
    /// <para>
    /// Changing <c>DefaultOptionSets</c> alone reaches nobody already running — the seeders are idempotent
    /// per LIST — so every statement here applies to every existing copy, matched on the VALUE (the code)
    /// so that a relabel does not hide a row from it. A withdrawn value is soft-deleted where no engagement
    /// records it and only hidden from the picker where one does, which is what the RESTRICT foreign keys
    /// would force on a hard delete; the engagement keeps reading correctly either way.
    /// </para>
    /// </summary>
    public partial class AddRemsJobTemplateAndAlignOptionLists : Migration
    {
        private const string WithdrawnServiceLines =
            "N'outsourced_cfo', N'payroll_services', N'internal_accounting', N'internal_billing', " +
            "N'internal_operations', N'internal_marketing', N'internal_it', N'internal_miscellaneous'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- The engagement's job template ----
            migrationBuilder.AddColumn<Guid>(
                name: "JobTemplateId",
                table: "REMSEngagement",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagement_JobTemplateId",
                table: "REMSEngagement",
                column: "JobTemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_REMSEngagement_OptionSetItems_JobTemplateId",
                table: "REMSEngagement",
                column: "JobTemplateId",
                principalTable: "OptionSetItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // A whole new list, so its OptionSets row per scope comes before its items. IsSystem is copied
            // from REMS.Department so the platform row stays read-only and each tenant's copy stays theirs.
            migrationBuilder.Sql(
                """
                INSERT INTO [OptionSets]
                    ([Id], [TenantId], [EntityType], [Key], [Name], [ParentSetId], [ItemSortMode],
                     [IsSystem], [IsClosed], [IsActive], [CreatedOnUtc], [UpdatedOnUtc], [Deleted])
                SELECT NEWID(), d.[TenantId], d.[EntityType], N'REMS.JobTemplate', N'REMS Job Template',
                       NULL, N'Custom', d.[IsSystem], 0, 1, SYSUTCDATETIME(), SYSUTCDATETIME(), 0
                FROM [OptionSets] d
                WHERE d.[Key] = N'REMS.Department'
                  AND d.[Deleted] = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM [OptionSets] p
                      WHERE p.[Key] = N'REMS.JobTemplate' AND p.[Deleted] = 0
                        AND p.[EntityType] = d.[EntityType]
                        AND ((p.[TenantId] IS NULL AND d.[TenantId] IS NULL) OR p.[TenantId] = d.[TenantId]));
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO [OptionSetItems]
                    ([Id], [OptionSetId], [TenantId], [Value], [Label], [Description], [SortOrder],
                     [IsDefault], [IsActive], [IsSystem], [CreatedOnUtc], [UpdatedOnUtc], [Deleted])
                SELECT NEWID(), s.[Id], s.[TenantId], v.[Value], v.[Label], NULL, v.[SortOrder], 0, 1, 0,
                       SYSUTCDATETIME(), SYSUTCDATETIME(), 0
                FROM [OptionSets] s
                CROSS JOIN (VALUES
                    (N'tax_compliance',                        N'Tax Compliance',                             1),
                    (N'consulting_engagement',                 N'Consulting Engagement',                      2),
                    (N'mergers_acquisitions',                  N'Mergers and Acquisitions',                   3),
                    (N'pension_administration_tax_compliance', N'Pension Administration and Tax Compliance',  4),
                    (N'business_valuation',                    N'Business Valuation',                         5),
                    (N'agreed_upon_procedures',                N'Agreed Upon Procedures',                     6),
                    (N'audit',                                 N'Audit',                                      7),
                    (N'examination',                           N'Examination',                                8),
                    (N'compilation',                           N'Compilation',                                9),
                    (N'review',                                N'Review',                                    10),
                    (N'peer_review',                           N'Peer Review',                               11),
                    (N'information_technology_services',       N'Information Technology Services',           12),
                    (N'soc',                                   N'SOC',                                       13),
                    (N'litigation',                            N'Litigation',                                14),
                    (N'forensic_accounting',                   N'Forensic Accounting',                       15),
                    (N'governmental_consulting_services',      N'Governmental Consulting Services',          16),
                    (N'client_accounting_services',            N'Client Accounting Services',                17)
                ) AS v([Value], [Label], [SortOrder])
                WHERE s.[Key] = N'REMS.JobTemplate'
                  AND s.[Deleted] = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM [OptionSetItems] i
                      WHERE i.[OptionSetId] = s.[Id] AND i.[Value] = v.[Value] AND i.[Deleted] = 0);
                """);

            // ---- Department: Admin is offered again ----
            // Retired on 3 September (RetireAdminAndAuditDepartments) and asked for back; Audit stays retired.
            migrationBuilder.Sql(
                """
                UPDATE i
                SET i.[IsActive] = 1, i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.Department' AND s.[Deleted] = 0
                  AND i.[Deleted] = 0 AND i.[IsActive] = 0 AND i.[Value] = N'admin';
                """);

            // ---- Service Line ----
            // Non-Chargeable Internal goes last, at MAX + 1 rather than a fixed position, because the
            // positions in an edited copy are that tenant's own.
            migrationBuilder.Sql(
                """
                INSERT INTO [OptionSetItems]
                    ([Id], [OptionSetId], [TenantId], [Value], [Label], [Description], [SortOrder],
                     [IsDefault], [IsActive], [IsSystem], [CreatedOnUtc], [UpdatedOnUtc], [Deleted])
                SELECT NEWID(), s.[Id], s.[TenantId], N'non_chargeable_internal', N'Non-Chargeable Internal', NULL,
                       ISNULL((SELECT MAX(x.[SortOrder]) FROM [OptionSetItems] x
                               WHERE x.[OptionSetId] = s.[Id] AND x.[Deleted] = 0), 0) + 1,
                       0, 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), 0
                FROM [OptionSets] s
                WHERE s.[Key] = N'REMS.ServiceLine' AND s.[Deleted] = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM [OptionSetItems] i
                      WHERE i.[OptionSetId] = s.[Id] AND i.[Value] = N'non_chargeable_internal' AND i.[Deleted] = 0);
                """);

            // The eight withdrawn values: soft-deleted where no engagement records them, hidden where one does.
            migrationBuilder.Sql(
                $"""
                UPDATE i
                SET i.[Deleted] = 1, i.[DeletedOnUtc] = SYSUTCDATETIME(), i.[IsActive] = 0,
                    i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.ServiceLine' AND s.[Deleted] = 0 AND i.[Deleted] = 0
                  AND i.[Value] IN ({WithdrawnServiceLines})
                  AND NOT EXISTS (SELECT 1 FROM [REMSEngagement] e WHERE e.[ServiceLineId] = i.[Id]);

                UPDATE i
                SET i.[IsActive] = 0, i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.ServiceLine' AND s.[Deleted] = 0 AND i.[Deleted] = 0 AND i.[IsActive] = 1
                  AND i.[Value] IN ({WithdrawnServiceLines});
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The withdrawn values come back wherever this migration removed or hid them.
            migrationBuilder.Sql(
                $"""
                UPDATE i
                SET i.[Deleted] = 0, i.[DeletedOnUtc] = NULL, i.[IsActive] = 1, i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.ServiceLine' AND s.[Deleted] = 0
                  AND i.[Value] IN ({WithdrawnServiceLines});

                UPDATE i
                SET i.[IsActive] = 0, i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.Department' AND s.[Deleted] = 0
                  AND i.[Deleted] = 0 AND i.[IsActive] = 1 AND i.[Value] = N'admin';
                """);

            // The added value and the whole job template list are withdrawn only where nobody has since
            // edited them, per the convention; an engagement holding a job template keeps its id.
            migrationBuilder.Sql(
                """
                UPDATE i
                SET i.[Deleted] = 1, i.[DeletedOnUtc] = SYSUTCDATETIME(), i.[IsActive] = 0,
                    i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Deleted] = 0 AND i.[Deleted] = 0 AND i.[UpdatedById] IS NULL
                  AND ((s.[Key] = N'REMS.ServiceLine' AND i.[Value] = N'non_chargeable_internal')
                    OR  s.[Key] = N'REMS.JobTemplate');

                UPDATE s
                SET s.[Deleted] = 1, s.[DeletedOnUtc] = SYSUTCDATETIME(), s.[IsActive] = 0,
                    s.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSets] s
                WHERE s.[Key] = N'REMS.JobTemplate' AND s.[Deleted] = 0 AND s.[UpdatedById] IS NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_REMSEngagement_OptionSetItems_JobTemplateId",
                table: "REMSEngagement");

            migrationBuilder.DropIndex(
                name: "IX_REMSEngagement_JobTemplateId",
                table: "REMSEngagement");

            migrationBuilder.DropColumn(
                name: "JobTemplateId",
                table: "REMSEngagement");
        }
    }
}
