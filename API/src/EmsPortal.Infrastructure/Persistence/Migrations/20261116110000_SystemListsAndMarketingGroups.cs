using EmsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only (the user, 16 Nov 2026: a flag for system lists, and the lists still written in code moved
    /// under it). A tenant's copy of a seeded list becomes a system list; the contact roles get the tooltip
    /// text the client form held; the marketing methods get a group list to depend on, linked from the
    /// group tag in each method's metadata. The two person lists are new and arrive with the startup seeder.
    /// </summary>
    [DbContext(typeof(EmsPortalDbContext))]
    [Migration("20261116110000_SystemListsAndMarketingGroups")]
    public partial class SystemListsAndMarketingGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE t SET IsSystem = 1, UpdatedOnUtc = SYSUTCDATETIME()
                FROM OptionSets t
                WHERE t.TenantId IS NOT NULL AND t.IsSystem = 0
                  AND EXISTS (SELECT 1 FROM OptionSets s WHERE s.TenantId IS NULL AND s.[Key] = t.[Key]);

                UPDATE i SET Description = d.Description, UpdatedOnUtc = SYSUTCDATETIME()
                FROM OptionSetItems i
                JOIN OptionSets s ON s.Id = i.OptionSetId
                JOIN (VALUES
                    (N'primary_client_contact', N'Who we speak to about this engagement — the main person on your side.'),
                    (N'financial_contact', N'Who we speak to about your finances and reporting.'),
                    (N'other_contact', N'Anyone else you would like us to have on file.'),
                    (N'finance_director', N'The finance director for this entity.'),
                    (N'trust_estate_contact', N'The trustee or personal representative who acts for the trust or estate.')
                ) d (ItemValue, Description) ON d.ItemValue = i.[Value]
                WHERE s.[Key] = N'REMS.ContactRole' AND i.Description IS NULL;

                DECLARE @groups TABLE (ItemValue nvarchar(100), Label nvarchar(200), SortOrder int, Tag nvarchar(100));
                INSERT INTO @groups VALUES
                    (N'global', N'Global', 1, N'Global'),
                    (N'geography', N'Geography', 2, N'Geography'),
                    (N'service_education', N'Service / Education', 3, N'Service/Education'),
                    (N'event', N'Event', 4, N'Event');

                -- Every marketing list not yet grouped, each with the group list of its own scope.
                DECLARE @methods TABLE (SetId uniqueidentifier, TenantId uniqueidentifier NULL, GroupSetId uniqueidentifier);
                INSERT INTO @methods
                SELECT s.Id, s.TenantId, NEWID()
                FROM OptionSets s
                WHERE s.[Key] = N'REMSMarketing_MarketingMethods.MarketingMethodId' AND s.Deleted = 0 AND s.ParentSetId IS NULL;

                UPDATE m SET GroupSetId = g.Id
                FROM @methods m
                JOIN OptionSets g ON g.[Key] = N'REMS.MarketingGroup' AND g.Deleted = 0
                    AND ((g.TenantId IS NULL AND m.TenantId IS NULL) OR g.TenantId = m.TenantId);

                INSERT INTO OptionSets
                    (Id, TenantId, [Key], Name, ParentSetId, ItemSortMode, IsSystem, IsClosed, IsActive, CreatedOnUtc, UpdatedOnUtc, Deleted)
                SELECT m.GroupSetId, m.TenantId, N'REMS.MarketingGroup', N'REMS Marketing Group', NULL, N'AlphabeticalAsc',
                    1, 0, 1, SYSUTCDATETIME(), SYSUTCDATETIME(), 0
                FROM @methods m
                WHERE NOT EXISTS (SELECT 1 FROM OptionSets g WHERE g.Id = m.GroupSetId);

                INSERT INTO OptionSetItems
                    (Id, OptionSetId, TenantId, [Value], Label, SortOrder, IsDefault, IsActive, IsSystem, CreatedOnUtc, UpdatedOnUtc, Deleted)
                SELECT NEWID(), m.GroupSetId, m.TenantId, g.ItemValue, g.Label, g.SortOrder, 0, 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), 0
                FROM @methods m
                CROSS JOIN @groups g
                WHERE NOT EXISTS (SELECT 1 FROM OptionSetItems i
                                  WHERE i.OptionSetId = m.GroupSetId AND i.[Value] = g.ItemValue AND i.Deleted = 0);

                UPDATE s SET ParentSetId = m.GroupSetId, UpdatedOnUtc = SYSUTCDATETIME()
                FROM OptionSets s
                JOIN @methods m ON m.SetId = s.Id;

                INSERT INTO OptionSetItemParents (OptionSetItemId, ParentOptionSetItemId)
                SELECT i.Id, p.Id
                FROM @methods m
                JOIN OptionSetItems i ON i.OptionSetId = m.SetId AND i.Deleted = 0
                JOIN @groups g ON g.Tag = CASE WHEN ISJSON(i.MetadataJson) = 1 THEN JSON_VALUE(i.MetadataJson, '$.group') END
                JOIN OptionSetItems p ON p.OptionSetId = m.GroupSetId AND p.[Value] = g.ItemValue AND p.Deleted = 0
                WHERE NOT EXISTS (SELECT 1 FROM OptionSetItemParents l
                                  WHERE l.OptionSetItemId = i.Id AND l.ParentOptionSetItemId = p.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
