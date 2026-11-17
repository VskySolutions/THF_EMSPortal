using EmsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only. The related client status "REMS Initiated" reads "COP Initiated" (the user, 17 Nov 2026).
    /// The code stays rems_initiated; a label a firm has already reworded is left alone.
    /// </summary>
    [DbContext(typeof(EmsPortalDbContext))]
    [Migration("20261117090000_RenameRemsInitiatedToCopInitiated")]
    public partial class RenameRemsInitiatedToCopInitiated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE i SET i.Label = N'COP Initiated', i.UpdatedOnUtc = SYSUTCDATETIME()
                FROM OptionSetItems i
                JOIN OptionSets s ON s.Id = i.OptionSetId
                WHERE s.[Key] = N'REMS.RelatedEntityStatus'
                  AND i.[Value] = N'rems_initiated'
                  AND i.Label = N'REMS Initiated';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE i SET i.Label = N'REMS Initiated', i.UpdatedOnUtc = SYSUTCDATETIME()
                FROM OptionSetItems i
                JOIN OptionSets s ON s.Id = i.OptionSetId
                WHERE s.[Key] = N'REMS.RelatedEntityStatus'
                  AND i.[Value] = N'rems_initiated'
                  AND i.Label = N'COP Initiated';
                """);
        }
    }
}
