using EmsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only. The seeder adds a missing default template but never rewrites one, so THF's wording of
    /// 14 Nov 2026 for the client's invitation and reminder is put on the stored platform defaults here.
    /// A tenant's own override is left as it is.
    /// </summary>
    [DbContext(typeof(EmsPortalDbContext))]
    [Migration("20261114090000_UpdateRemsClientEmailTemplates")]
    public partial class UpdateRemsClientEmailTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE EmailTemplates
                SET Subject = N'Welcome to THF | Complete Your Client Set-up',
                    Body = N'<p>Hello {{ClientName}},</p>
                <p>Welcome to THF! We''re excited for the opportunity to work with you.</p>
                <p>As a first step, please complete our online Client Set-Up Form. This will provide our team with the information needed to establish your client account.</p>
                <p>A few things to know before you begin:</p>
                <ul>
                  <li>Time required: Approximately 5&ndash;10 minutes.</li>
                  <li>Deadline: Please complete the form within five (5) business days.</li>
                  <li>Important: The form must be completed in one sitting. It cannot be started by one person and finished by another, so please ensure the appropriate person in your organization is available to complete it.</li>
                </ul>
                <p><a href="{{FormLink}}" style="display:inline-block;padding:10px 20px;background-color:#1f4e79;color:#ffffff;text-decoration:none;font-weight:bold;border-radius:4px;">COMPLETE CLIENT SET-UP FORM</a></p>',
                    UpdatedOnUtc = SYSUTCDATETIME()
                WHERE TemplateKey = N'RemsFormLink' AND TenantId IS NULL;

                UPDATE EmailTemplates
                SET Subject = N'A Quick Reminder | Your THF Client Set-up',
                    Body = N'<p>Hello {{ClientName}},</p>
                <p>We''re looking forward to working with you! As a reminder, your THF Client Set-Up Form is still awaiting completion.</p>
                <p>The form takes just 5&ndash;10 minutes and provides the information we need to establish your account.</p>
                <p>Please complete it within five business days of your original invitation.</p>
                <p><a href="{{FormLink}}" style="display:inline-block;padding:10px 20px;background-color:#1f4e79;color:#ffffff;text-decoration:none;font-weight:bold;border-radius:4px;">COMPLETE CLIENT SET-UP FORM</a></p>
                <p>Before you begin: The form must be completed in one sitting by the same person.</p>
                <p>If you have any questions or need assistance, please contact your THF representative.</p>
                <p>Thank you!</p>',
                    UpdatedOnUtc = SYSUTCDATETIME()
                WHERE TemplateKey = N'RemsFormReminder' AND TenantId IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
