using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Two REMS option lists brought to the dependency tables THF supplied in September 2026: which
    /// industries each entity type offers, and which service lines each department offers.
    /// <para>
    /// Industry gains Trust/Estate (the one trade a Trust and Estate entity is in), the Insurance trade
    /// "Healthcare" becomes "Health" and moves ahead of Property and Casualty so the four read in THF's
    /// order, and the unqualified Government value is withdrawn: a government client is filed as state,
    /// local or federal. Service Line withdraws Business Valuation and Peer Review, which the table places
    /// as job templates rather than lines of their own, and its Attest Services line reads "Assurance
    /// Services", the name of the department it belongs to.
    /// </para>
    /// <para>
    /// As before, changing <c>DefaultOptionSets</c> alone reaches nobody already running, so every
    /// statement applies to every existing copy, matched on the VALUE. A withdrawn value is soft-deleted
    /// where no engagement records it and only hidden where one does; the relabel and the move touch a
    /// copy only where it still holds the seeded label and order.
    /// </para>
    /// </summary>
    public partial class AlignRemsIndustryAndServiceLineDependencies : Migration
    {
        private const string WithdrawnServiceLines = "N'business_valuation', N'peer_review'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- Industry: Trust/Estate, at MAX + 1 because the positions in an edited copy are that tenant's own ----
            migrationBuilder.Sql(
                """
                INSERT INTO [OptionSetItems]
                    ([Id], [OptionSetId], [TenantId], [Value], [Label], [Description], [SortOrder],
                     [IsDefault], [IsActive], [IsSystem], [CreatedOnUtc], [UpdatedOnUtc], [Deleted])
                SELECT NEWID(), s.[Id], s.[TenantId], N'trust_estate', N'Trust/Estate', NULL,
                       ISNULL((SELECT MAX(x.[SortOrder]) FROM [OptionSetItems] x
                               WHERE x.[OptionSetId] = s.[Id] AND x.[Deleted] = 0), 0) + 1,
                       0, 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), 0
                FROM [OptionSets] s
                WHERE s.[Key] = N'REMS.Industry' AND s.[Deleted] = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM [OptionSetItems] i
                      WHERE i.[OptionSetId] = s.[Id] AND i.[Value] = N'trust_estate' AND i.[Deleted] = 0);
                """);

            // ---- Industry: the Insurance "Healthcare" trade reads "Health", where nobody has relabelled it ----
            migrationBuilder.Sql(
                """
                UPDATE i
                SET i.[Label] = N'Health', i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.Industry' AND s.[Deleted] = 0
                  AND i.[Deleted] = 0 AND i.[Value] = N'insurance_health' AND i.[Label] = N'Healthcare';
                """);

            // ---- Industry: Health moves to just ahead of Property and Casualty ----
            // It was appended at the end of the list, which put it after "Other" in the Insurance picker. One
            // statement, so every row reads the positions as they were: Health takes P&C's position and the
            // rows from there up to Health's old one each move down by one. Nothing happens in a copy where
            // Health already sorts ahead.
            migrationBuilder.Sql(
                """
                UPDATE i
                SET i.[SortOrder] = CASE WHEN i.[Value] = N'insurance_health' THEN x.[Target] ELSE i.[SortOrder] + 1 END,
                    i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                CROSS APPLY (
                    SELECT pc.[SortOrder] AS [Target], h.[SortOrder] AS [Health]
                    FROM [OptionSetItems] pc
                    INNER JOIN [OptionSetItems] h
                        ON h.[OptionSetId] = pc.[OptionSetId] AND h.[Value] = N'insurance_health' AND h.[Deleted] = 0
                    WHERE pc.[OptionSetId] = s.[Id] AND pc.[Value] = N'insurance_property_casualty' AND pc.[Deleted] = 0
                ) x
                WHERE s.[Key] = N'REMS.Industry' AND s.[Deleted] = 0 AND i.[Deleted] = 0
                  AND x.[Health] > x.[Target]
                  AND i.[SortOrder] >= x.[Target] AND i.[SortOrder] <= x.[Health];
                """);

            // ---- Industry: the unqualified Government value is withdrawn ----
            migrationBuilder.Sql(
                """
                UPDATE i
                SET i.[Deleted] = 1, i.[DeletedOnUtc] = SYSUTCDATETIME(), i.[IsActive] = 0,
                    i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.Industry' AND s.[Deleted] = 0 AND i.[Deleted] = 0
                  AND i.[Value] = N'government'
                  AND NOT EXISTS (SELECT 1 FROM [REMSEngagement] e WHERE e.[IndustryId] = i.[Id]);

                UPDATE i
                SET i.[IsActive] = 0, i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.Industry' AND s.[Deleted] = 0 AND i.[Deleted] = 0 AND i.[IsActive] = 1
                  AND i.[Value] = N'government';
                """);

            // ---- Service Line: "Attest Services" reads "Assurance Services", where nobody has relabelled it ----
            migrationBuilder.Sql(
                """
                UPDATE i
                SET i.[Label] = N'Assurance Services', i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.ServiceLine' AND s.[Deleted] = 0
                  AND i.[Deleted] = 0 AND i.[Value] = N'attest_services' AND i.[Label] = N'Attest Services';
                """);

            // ---- Service Line: Business Valuation and Peer Review are withdrawn ----
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
                WHERE s.[Deleted] = 0
                  AND ((s.[Key] = N'REMS.ServiceLine' AND i.[Value] IN ({WithdrawnServiceLines}))
                    OR (s.[Key] = N'REMS.Industry' AND i.[Value] = N'government'));
                """);

            // The service line goes back to its old label.
            migrationBuilder.Sql(
                """
                UPDATE i
                SET i.[Label] = N'Attest Services', i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.ServiceLine' AND s.[Deleted] = 0
                  AND i.[Deleted] = 0 AND i.[Value] = N'attest_services' AND i.[Label] = N'Assurance Services';
                """);

            // Health goes back to its old label and to the end of the list.
            migrationBuilder.Sql(
                """
                UPDATE i
                SET i.[Label] = N'Healthcare', i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.Industry' AND s.[Deleted] = 0
                  AND i.[Deleted] = 0 AND i.[Value] = N'insurance_health' AND i.[Label] = N'Health';

                UPDATE i
                SET i.[SortOrder] = m.[Last] + 1, i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                CROSS APPLY (SELECT MAX(x.[SortOrder]) AS [Last] FROM [OptionSetItems] x
                             WHERE x.[OptionSetId] = s.[Id] AND x.[Deleted] = 0) m
                WHERE s.[Key] = N'REMS.Industry' AND s.[Deleted] = 0
                  AND i.[Deleted] = 0 AND i.[Value] = N'insurance_health';
                """);

            // The added value is withdrawn only where nobody has since edited it, per the convention.
            migrationBuilder.Sql(
                """
                UPDATE i
                SET i.[Deleted] = 1, i.[DeletedOnUtc] = SYSUTCDATETIME(), i.[IsActive] = 0,
                    i.[UpdatedOnUtc] = SYSUTCDATETIME()
                FROM [OptionSetItems] i
                INNER JOIN [OptionSets] s ON s.[Id] = i.[OptionSetId]
                WHERE s.[Key] = N'REMS.Industry' AND s.[Deleted] = 0 AND i.[Deleted] = 0
                  AND i.[Value] = N'trust_estate' AND i.[UpdatedById] IS NULL;
                """);
        }
    }
}
