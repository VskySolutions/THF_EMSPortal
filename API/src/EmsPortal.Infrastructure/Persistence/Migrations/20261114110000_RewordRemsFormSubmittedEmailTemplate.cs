using EmsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only. The staff "form submitted" email said REMS; the stored platform default now says COP,
    /// as the built-in one does. A tenant's own override is left as it is.
    /// </summary>
    [DbContext(typeof(EmsPortalDbContext))]
    [Migration("20261114110000_RewordRemsFormSubmittedEmailTemplate")]
    public partial class RewordRemsFormSubmittedEmailTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE EmailTemplates
                SET Subject = N'COP form submitted for {{RemsNumber}}',
                    Body = N'<p>{{ClientName}} has submitted their COP form for request <strong>{{RemsNumber}}</strong>.</p>
                <p><strong>Submitted on:</strong> {{SubmittedOn}}</p>
                <p><a href="{{RequestLink}}">Open the request</a></p>',
                    UpdatedOnUtc = SYSUTCDATETIME()
                WHERE TemplateKey = N'RemsFormSubmitted' AND TenantId IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
