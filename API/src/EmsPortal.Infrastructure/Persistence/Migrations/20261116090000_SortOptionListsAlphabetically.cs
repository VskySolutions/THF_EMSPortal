using EmsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only. Every option list is ordered A → Z (the user, 16 Nov 2026), which is also the default for
    /// a new one. Each value keeps its custom position, so switching a list back restores the old order.
    /// </summary>
    [DbContext(typeof(EmsPortalDbContext))]
    [Migration("20261116090000_SortOptionListsAlphabetically")]
    public partial class SortOptionListsAlphabetically : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE OptionSets SET ItemSortMode = N'AlphabeticalAsc', UpdatedOnUtc = SYSUTCDATETIME()
                WHERE ItemSortMode <> N'AlphabeticalAsc';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
