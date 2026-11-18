using EmsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only. AddRemsJobType as first released put the template job numbers ("ZZ…") on the job types;
    /// a database that ran that version still sends them as specification 2, which Maconomy refuses.
    /// </summary>
    [DbContext(typeof(EmsPortalDbContext))]
    [Migration("20261114060000_ClearRemsJobTypeTemplateNumbers")]
    public partial class ClearRemsJobTypeTemplateNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A job type's number is a specification 2 override, never a template job: the label is sent instead.
            migrationBuilder.Sql("""
                UPDATE i SET i.MaconomyNumber = NULL, i.UpdatedOnUtc = SYSUTCDATETIME()
                FROM OptionSetItems i
                JOIN OptionSets s ON s.Id = i.OptionSetId AND s.[Key] = N'REMS.JobType'
                WHERE i.MaconomyNumber LIKE N'ZZ%';
                """);

            // The template jobs belong on the service lines; the same list AddRemsJobType now seeds, where none is set.
            migrationBuilder.Sql("""
                UPDATE i SET i.MaconomyNumber = v.Number, i.UpdatedOnUtc = SYSUTCDATETIME()
                FROM OptionSetItems i
                JOIN OptionSets s ON s.Id = i.OptionSetId AND s.[Key] = N'REMS.ServiceLine'
                JOIN (VALUES
                    (N'tax_compliance', N'ZZTax Compliance'),
                    (N'client_accounting_services', N'ZZClient Accounting Services'),
                    (N'consulting', N'ZZConsulting Engagement'),
                    (N'business_valuation', N'ZZBusiness Valuation'),
                    (N'it_services', N'ZZInformation Technology Services'),
                    (N'plan_administration', N'ZZPension Administration and Tax Compliance'),
                    (N'mergers_acquisitions', N'ZZMergers and Acquisitions'),
                    (N'peer_review', N'ZZPeer Review'),
                    (N'soc', N'ZZSOC'),
                    (N'employee_benefits', N'ZZPension Administration and Tax Compliance'),
                    (N'estate_planning', N'ZZTax Compliance'),
                    (N'litigation_support', N'ZZLitigation'),
                    (N'forensic_accounting', N'ZZForensic Accounting')
                ) v (ItemValue, Number) ON i.[Value] = v.ItemValue
                WHERE i.MaconomyNumber IS NULL AND i.Deleted = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
