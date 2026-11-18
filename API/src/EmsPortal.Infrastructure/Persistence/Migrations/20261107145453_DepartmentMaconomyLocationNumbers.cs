using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DepartmentMaconomyLocationNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Maconomy location number onto every existing REMS Department value — the standard list and
            // every tenant's copy — where none is set yet. Audit, retired, gets none; Consulting (6) is a
            // location no department maps to.
            migrationBuilder.Sql("""
                UPDATE i SET i.MaconomyNumber = v.Number
                FROM OptionSetItems i
                JOIN OptionSets s ON s.Id = i.OptionSetId
                JOIN (VALUES
                    ('assurance', '1'),
                    ('tax', '2'),
                    ('admin', '3'),
                    ('gcs', '4'),
                    ('cas', '5')
                ) v (ItemValue, Number) ON i.[Value] = v.ItemValue
                WHERE s.[Key] = 'REMS.Department' AND i.MaconomyNumber IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
