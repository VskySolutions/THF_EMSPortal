using EmsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only. Request numbers are named after the site since 14 Nov 2026 ("COP-81"); the ones already
    /// issued as "REMS-n" take the same prefix: the branding's application name, else the tenant's name.
    /// </summary>
    [DbContext(typeof(EmsPortalDbContext))]
    [Migration("20261114130000_RenameRemsNumbersToSiteName")]
    public partial class RenameRemsNumbersToSiteName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SUBSTRING from 5 keeps "-n"; the prefix is cut to 50 as the generator cuts it.
            migrationBuilder.Sql("""
                UPDATE r
                SET r.RemsNumber = LEFT(site.Name, 50) + SUBSTRING(r.RemsNumber, 5, 14)
                FROM REMS r
                JOIN Tenants t ON t.Id = r.TenantId
                OUTER APPLY (
                    SELECT TOP 1 b.ThemeJson FROM TenantBrandings b
                    WHERE b.TenantId = t.Id AND b.Deleted = 0 AND ISJSON(b.ThemeJson) = 1) theme
                CROSS APPLY (
                    SELECT COALESCE(
                        NULLIF(LTRIM(RTRIM(JSON_VALUE(theme.ThemeJson, '$.identity.applicationName'))), N''),
                        NULLIF(LTRIM(RTRIM(t.Name)), N''),
                        N'COP Portal') AS Name) site
                WHERE r.RemsNumber LIKE N'REMS-%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
