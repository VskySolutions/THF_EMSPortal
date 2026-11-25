using EmsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only. The approver seat "Shareholder" is the "Managing Shareholder" (THF, 21 Nov 2026):
    /// RemsApproverRole is stored as a string, so every row carrying the old member is renamed. The role
    /// itself is new and seeded at start; the Shareholder role and its holders are left as they are.
    /// </summary>
    [DbContext(typeof(EmsPortalDbContext))]
    [Migration("20261121090000_RenameShareholderApproverToManagingShareholder")]
    public partial class RenameShareholderApproverToManagingShareholder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [REMS_ApprovalTask]
                SET [ApproverRole] = N'ManagingShareholder', [UpdatedOnUtc] = SYSUTCDATETIME()
                WHERE [ApproverRole] = N'Shareholder';
                """);

            // A template row that would collide with one already under the new name is retired first.
            migrationBuilder.Sql("""
                UPDATE t SET t.[Deleted] = 1, t.[DeletedOnUtc] = SYSUTCDATETIME()
                FROM [REMS_ChecklistTemplate] t
                WHERE t.[ApproverRole] = N'Shareholder' AND t.[Deleted] = 0
                  AND EXISTS (
                      SELECT 1 FROM [REMS_ChecklistTemplate] x
                      WHERE x.[TenantId] = t.[TenantId] AND x.[SettingsId] = t.[SettingsId]
                        AND x.[DisplayOrder] = t.[DisplayOrder]
                        AND x.[ApproverRole] = N'ManagingShareholder' AND x.[Deleted] = 0);

                UPDATE [REMS_ChecklistTemplate]
                SET [ApproverRole] = N'ManagingShareholder', [UpdatedOnUtc] = SYSUTCDATETIME()
                WHERE [ApproverRole] = N'Shareholder';
                """);

            // The list the badge is read from. A label or description a firm has reworded is left alone.
            migrationBuilder.Sql("""
                UPDATE i SET
                    i.[Value] = N'ManagingShareholder',
                    i.[Label] = CASE WHEN i.[Label] = N'Shareholder' THEN N'Managing Shareholder' ELSE i.[Label] END,
                    i.[Description] = CASE
                        WHEN i.[Description] LIKE N'A holder of the Shareholder role % on every engagement''s list by standing, and not removable.'
                            THEN REPLACE(i.[Description], N'the Shareholder role', N'the Managing Shareholder role')
                        ELSE i.[Description] END,
                    i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.ApproverRole' AND i.[Value] = N'Shareholder';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [REMS_ApprovalTask]
                SET [ApproverRole] = N'Shareholder', [UpdatedOnUtc] = SYSUTCDATETIME()
                WHERE [ApproverRole] = N'ManagingShareholder';

                UPDATE [REMS_ChecklistTemplate]
                SET [ApproverRole] = N'Shareholder', [UpdatedOnUtc] = SYSUTCDATETIME()
                WHERE [ApproverRole] = N'ManagingShareholder';

                UPDATE i SET
                    i.[Value] = N'Shareholder',
                    i.[Label] = CASE WHEN i.[Label] = N'Managing Shareholder' THEN N'Shareholder' ELSE i.[Label] END,
                    i.[Description] = REPLACE(i.[Description], N'the Managing Shareholder role', N'the Shareholder role'),
                    i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.ApproverRole' AND i.[Value] = N'ManagingShareholder';
                """);
        }
    }
}
