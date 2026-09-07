using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameRemsClassificationColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_REMSEngagement_OptionSetItems_SubIndustryId",
                table: "REMSEngagement");

            migrationBuilder.DropForeignKey(
                name: "FK_REMSEngagement_OptionSetItems_SubServiceLineId",
                table: "REMSEngagement");

            migrationBuilder.DropForeignKey(
                name: "FK_REMSForm_OptionSetItems_IndustryGroupId",
                table: "REMSForm");

            migrationBuilder.RenameColumn(
                name: "IndustryGroupId",
                table: "REMSForm",
                newName: "EntityTypeId");

            migrationBuilder.RenameIndex(
                name: "IX_REMSForm_IndustryGroupId",
                table: "REMSForm",
                newName: "IX_REMSForm_EntityTypeId");

            migrationBuilder.RenameColumn(
                name: "SubServiceLineId",
                table: "REMSEngagement",
                newName: "ServiceLineId");

            migrationBuilder.RenameColumn(
                name: "SubIndustryId",
                table: "REMSEngagement",
                newName: "IndustryId");

            migrationBuilder.RenameIndex(
                name: "IX_REMSEngagement_SubServiceLineId",
                table: "REMSEngagement",
                newName: "IX_REMSEngagement_ServiceLineId");

            migrationBuilder.RenameIndex(
                name: "IX_REMSEngagement_SubIndustryId",
                table: "REMSEngagement",
                newName: "IX_REMSEngagement_IndustryId");

            migrationBuilder.AddForeignKey(
                name: "FK_REMSEngagement_OptionSetItems_IndustryId",
                table: "REMSEngagement",
                column: "IndustryId",
                principalTable: "OptionSetItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_REMSEngagement_OptionSetItems_ServiceLineId",
                table: "REMSEngagement",
                column: "ServiceLineId",
                principalTable: "OptionSetItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_REMSForm_OptionSetItems_EntityTypeId",
                table: "REMSForm",
                column: "EntityTypeId",
                principalTable: "OptionSetItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // The option-set KEYS, brought into line with the columns above and with what the screen has
            // called these lists since RenameRemsEngagementClassifications:
            //
            //   REMS.IndustryGroup   -> REMS.EntityType
            //   REMS.SubIndustry     -> REMS.Industry
            //   REMS.SubServiceLine  -> REMS.ServiceLine
            //
            // That migration deliberately left the keys alone, because a key is how a tenant's own copy of
            // a list is found and renaming one would strand it. This one renames EVERY row carrying the key
            // — the platform default and every tenant copy — in a single statement, so no copy is left
            // behind. The stored values are foreign keys to OptionSetItem.Id and are untouched.
            //
            // The retired REMS.ServiceLine list goes first: it is soft-deleted but still holds the key
            // REMS.SubServiceLine is about to take. The unique index is filtered on [Deleted] = 0 so the
            // two could coexist, but a dead row under a live key is exactly the confusion this is fixing.
            migrationBuilder.Sql(
                """
                UPDATE [OptionSets] SET [Key] = N'REMS.ServiceLine.Retired', [UpdatedOnUtc] = SYSUTCDATETIME()
                WHERE [Key] = N'REMS.ServiceLine' AND [Deleted] = 1;

                UPDATE [OptionSets] SET [Key] = N'REMS.EntityType', [UpdatedOnUtc] = SYSUTCDATETIME()
                WHERE [Key] = N'REMS.IndustryGroup';

                UPDATE [OptionSets] SET [Key] = N'REMS.Industry', [UpdatedOnUtc] = SYSUTCDATETIME()
                WHERE [Key] = N'REMS.SubIndustry';

                UPDATE [OptionSets] SET [Key] = N'REMS.ServiceLine', [UpdatedOnUtc] = SYSUTCDATETIME()
                WHERE [Key] = N'REMS.SubServiceLine';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Keys back first, in reverse order — REMS.ServiceLine has to be vacated before the retired
            // list can reclaim it.
            migrationBuilder.Sql(
                """
                UPDATE [OptionSets] SET [Key] = N'REMS.SubServiceLine', [UpdatedOnUtc] = SYSUTCDATETIME()
                WHERE [Key] = N'REMS.ServiceLine' AND [Deleted] = 0;

                UPDATE [OptionSets] SET [Key] = N'REMS.SubIndustry', [UpdatedOnUtc] = SYSUTCDATETIME()
                WHERE [Key] = N'REMS.Industry';

                UPDATE [OptionSets] SET [Key] = N'REMS.IndustryGroup', [UpdatedOnUtc] = SYSUTCDATETIME()
                WHERE [Key] = N'REMS.EntityType';

                UPDATE [OptionSets] SET [Key] = N'REMS.ServiceLine', [UpdatedOnUtc] = SYSUTCDATETIME()
                WHERE [Key] = N'REMS.ServiceLine.Retired';
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_REMSEngagement_OptionSetItems_IndustryId",
                table: "REMSEngagement");

            migrationBuilder.DropForeignKey(
                name: "FK_REMSEngagement_OptionSetItems_ServiceLineId",
                table: "REMSEngagement");

            migrationBuilder.DropForeignKey(
                name: "FK_REMSForm_OptionSetItems_EntityTypeId",
                table: "REMSForm");

            migrationBuilder.RenameColumn(
                name: "EntityTypeId",
                table: "REMSForm",
                newName: "IndustryGroupId");

            migrationBuilder.RenameIndex(
                name: "IX_REMSForm_EntityTypeId",
                table: "REMSForm",
                newName: "IX_REMSForm_IndustryGroupId");

            migrationBuilder.RenameColumn(
                name: "ServiceLineId",
                table: "REMSEngagement",
                newName: "SubServiceLineId");

            migrationBuilder.RenameColumn(
                name: "IndustryId",
                table: "REMSEngagement",
                newName: "SubIndustryId");

            migrationBuilder.RenameIndex(
                name: "IX_REMSEngagement_ServiceLineId",
                table: "REMSEngagement",
                newName: "IX_REMSEngagement_SubServiceLineId");

            migrationBuilder.RenameIndex(
                name: "IX_REMSEngagement_IndustryId",
                table: "REMSEngagement",
                newName: "IX_REMSEngagement_SubIndustryId");

            migrationBuilder.AddForeignKey(
                name: "FK_REMSEngagement_OptionSetItems_SubIndustryId",
                table: "REMSEngagement",
                column: "SubIndustryId",
                principalTable: "OptionSetItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_REMSEngagement_OptionSetItems_SubServiceLineId",
                table: "REMSEngagement",
                column: "SubServiceLineId",
                principalTable: "OptionSetItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_REMSForm_OptionSetItems_IndustryGroupId",
                table: "REMSForm",
                column: "IndustryGroupId",
                principalTable: "OptionSetItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
