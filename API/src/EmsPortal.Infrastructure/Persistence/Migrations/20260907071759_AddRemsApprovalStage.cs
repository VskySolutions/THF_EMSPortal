using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// STATIC-APPROVAL-POLICY. A round can now run in stages: each task carries the stage it belongs to,
    /// and a task of a later stage is Waiting until the stage before it approves. Every task on file is
    /// stage 1, which is what an unstaged round is, so nothing is backfilled.
    /// <para>
    /// The Waiting value is added to every REMS.ApprovalStatus list on file — the platform default and
    /// each tenant's copy — because the seeder only reaches tenants created after today. Reverting the
    /// policy leaves the value harmless: nothing writes it when rounds are unstaged.
    /// </para>
    /// </summary>
    public partial class AddRemsApprovalStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Stage",
                table: "REMSApprovalTask",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(
                """
                INSERT INTO [OptionSetItems]
                    ([Id], [OptionSetId], [TenantId], [ParentItemId], [Value], [Label], [Description],
                     [SortOrder], [IsDefault], [IsActive], [BackgroundColor], [TextColor], [Icon],
                     [IsSystem], [MetadataJson], [CreatedById], [CreatedOnUtc], [UpdatedById],
                     [UpdatedOnUtc], [Deleted], [DeletedOnUtc])
                SELECT
                    NEWID(), s.[Id], s.[TenantId], NULL, N'Waiting', N'Waiting',
                    N'Not asked yet. Their turn comes once the stage before them has approved.',
                    5, 0, 1, N'#9e9e9e', N'#ffffff', NULL,
                    1, NULL, NULL, SYSUTCDATETIME(), NULL,
                    SYSUTCDATETIME(), 0, NULL
                FROM [OptionSets] s
                WHERE s.[Key] = N'REMS.ApprovalStatus'
                  AND s.[Deleted] = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM [OptionSetItems] i
                      WHERE i.[OptionSetId] = s.[Id] AND i.[Value] = N'Waiting' AND i.[Deleted] = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE i FROM [OptionSetItems] i
                JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.ApprovalStatus' AND i.[Value] = N'Waiting';
                """);

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "REMSApprovalTask");
        }
    }
}
