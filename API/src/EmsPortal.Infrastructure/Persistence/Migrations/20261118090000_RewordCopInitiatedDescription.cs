using EmsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only. The tooltip of the related client status "COP Initiated" says COP request, not REMS request (the user, 18 Nov 2026).
    /// A description a firm has already reworded is left alone.
    /// </summary>
    [DbContext(typeof(EmsPortalDbContext))]
    [Migration("20261118090000_RewordCopInitiatedDescription")]
    public partial class RewordCopInitiatedDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE i SET i.Description = N'A COP request has been raised for this related client and is being worked.', i.UpdatedOnUtc = SYSUTCDATETIME()
                FROM OptionSetItems i
                JOIN OptionSets s ON s.Id = i.OptionSetId
                WHERE s.[Key] = N'REMS.RelatedEntityStatus'
                  AND i.[Value] = N'rems_initiated'
                  AND i.Description = N'A REMS request has been raised for this related client and is being worked.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE i SET i.Description = N'A REMS request has been raised for this related client and is being worked.', i.UpdatedOnUtc = SYSUTCDATETIME()
                FROM OptionSetItems i
                JOIN OptionSets s ON s.Id = i.OptionSetId
                WHERE s.[Key] = N'REMS.RelatedEntityStatus'
                  AND i.[Value] = N'rems_initiated'
                  AND i.Description = N'A COP request has been raised for this related client and is being worked.';
                """);
        }
    }
}
