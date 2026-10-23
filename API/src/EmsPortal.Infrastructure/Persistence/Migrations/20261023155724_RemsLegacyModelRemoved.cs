using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemsLegacyModelRemoved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Organisation person rows backed the old REMSClient; their clients are Client rows with a LegalName now.
            migrationBuilder.Sql("UPDATE [Persons] SET [Deleted] = 1, [DeletedOnUtc] = SYSUTCDATETIME() WHERE [PartyType] = 1 AND [Deleted] = 0;");

            migrationBuilder.DropForeignKey(
                name: "FK_REMS_OptionSetItems_StatusId",
                table: "REMS");

            migrationBuilder.DropForeignKey(
                name: "FK_REMS_OptionSetItems_TypeId",
                table: "REMS");

            migrationBuilder.DropForeignKey(
                name: "FK_REMS_Tenants_TenantId",
                table: "REMS");

            migrationBuilder.DropForeignKey(
                name: "FK_REMS_Users_AdminAssignedToId",
                table: "REMS");

            migrationBuilder.DropForeignKey(
                name: "FK_REMS_Users_CSEId",
                table: "REMS");

            migrationBuilder.DropForeignKey(
                name: "FK_REMS_Users_OnBehalfOfUserId",
                table: "REMS");

            migrationBuilder.DropTable(
                name: "REMSAdditionalEntity",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSAdditionalIndividual",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSApprovalChecklistItem",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSEngagementApprover",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSEngagementAuditDetail",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSEngagementCommissionSplit",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSEngagementMarketingMethod",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSEngagementPersonnelRate",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSEngagementTaxForm",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSEntityAddress",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSEntityContact",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSFiles",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSFormDraft",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSFormEmailEvent",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSMaconomyCustomerSync",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSSendBack",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSApprovalTask",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSEngagementGovernmentDetail",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSEngagementTaxDetail",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSEntity",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSApprovalRound",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSClient",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSEngagement",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSFormSubmission",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMSForm",
                schema: "legacy");

            migrationBuilder.DropTable(
                name: "REMS",
                schema: "legacy");

            migrationBuilder.DropIndex(
                name: "IX_Persons_TenantId_MaconomyCustomerNumber",
                table: "Persons");

            migrationBuilder.DropIndex(
                name: "IX_Persons_TenantId_PartyType_LastName",
                table: "Persons");

            migrationBuilder.AlterColumn<string>(
                name: "ClientDisplayName",
                table: "Persons",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                computedColumnSql: "COALESCE(NULLIF(LTRIM(RTRIM(\n    ISNULL(NULLIF(LTRIM(RTRIM([LastName])), N''), N'')\n            + CASE WHEN NULLIF(LTRIM(RTRIM([FirstName])), N'') IS NULL THEN N''\n                   ELSE N' ' + LTRIM(RTRIM([FirstName])) END\n            + CASE WHEN NULLIF(LTRIM(RTRIM([Suffix])), N'') IS NULL THEN N''\n                   ELSE N' ' + LTRIM(RTRIM([Suffix])) END)), N''), [DisplayName])",
                stored: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(400)",
                oldMaxLength: 400,
                oldComputedColumnSql: "COALESCE(NULLIF(LTRIM(RTRIM(\n    CASE WHEN [PartyType] = 1 THEN ISNULL([CorporateName], N'')\n         ELSE ISNULL(NULLIF(LTRIM(RTRIM([LastName])), N''), N'')\n            + CASE WHEN NULLIF(LTRIM(RTRIM([FirstName])), N'') IS NULL THEN N''\n                   ELSE N' ' + LTRIM(RTRIM([FirstName])) END\n            + CASE WHEN NULLIF(LTRIM(RTRIM([Suffix])), N'') IS NULL THEN N''\n                   ELSE N' ' + LTRIM(RTRIM([Suffix])) END\n    END)), N''), [DisplayName])",
                oldStored: true);

            migrationBuilder.DropColumn(
                name: "CorporateName",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "MaconomyCustomerNumber",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "PartyType",
                table: "Persons");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_TenantId_LastName",
                table: "Persons",
                columns: new[] { "TenantId", "LastName" });

            migrationBuilder.Sql("IF SCHEMA_ID(N'legacy') IS NOT NULL EXEC(N'DROP SCHEMA [legacy]');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Persons_TenantId_LastName",
                table: "Persons");

            migrationBuilder.EnsureSchema(
                name: "legacy");

            migrationBuilder.AddColumn<string>(
                name: "CorporateName",
                table: "Persons",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaconomyCustomerNumber",
                table: "Persons",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartyType",
                table: "Persons",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "ClientDisplayName",
                table: "Persons",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                computedColumnSql: "COALESCE(NULLIF(LTRIM(RTRIM(\n    CASE WHEN [PartyType] = 1 THEN ISNULL([CorporateName], N'')\n         ELSE ISNULL(NULLIF(LTRIM(RTRIM([LastName])), N''), N'')\n            + CASE WHEN NULLIF(LTRIM(RTRIM([FirstName])), N'') IS NULL THEN N''\n                   ELSE N' ' + LTRIM(RTRIM([FirstName])) END\n            + CASE WHEN NULLIF(LTRIM(RTRIM([Suffix])), N'') IS NULL THEN N''\n                   ELSE N' ' + LTRIM(RTRIM([Suffix])) END\n    END)), N''), [DisplayName])",
                stored: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(400)",
                oldMaxLength: 400,
                oldComputedColumnSql: "COALESCE(NULLIF(LTRIM(RTRIM(\n    ISNULL(NULLIF(LTRIM(RTRIM([LastName])), N''), N'')\n            + CASE WHEN NULLIF(LTRIM(RTRIM([FirstName])), N'') IS NULL THEN N''\n                   ELSE N' ' + LTRIM(RTRIM([FirstName])) END\n            + CASE WHEN NULLIF(LTRIM(RTRIM([Suffix])), N'') IS NULL THEN N''\n                   ELSE N' ' + LTRIM(RTRIM([Suffix])) END)), N''), [DisplayName])",
                oldStored: true);

            migrationBuilder.CreateTable(
                name: "REMS",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdminAssignedToId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CSEId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExistingClientReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OnBehalfOfUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    REMSNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMS_OptionSetItems_StatusId",
                        column: x => x.StatusId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_OptionSetItems_TypeId",
                        column: x => x.TypeId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_Persons_ClientPersonId",
                        column: x => x.ClientPersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_Users_AdminAssignedToId",
                        column: x => x.AdminAssignedToId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_Users_CSEId",
                        column: x => x.CSEId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_Users_OnBehalfOfUserId",
                        column: x => x.OnBehalfOfUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSAdditionalEntity",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelatedStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedREMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmailAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SourceKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSAdditionalEntity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSAdditionalEntity_OptionSetItems_RelatedStatusId",
                        column: x => x.RelatedStatusId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSAdditionalEntity_REMS_REMSId",
                        column: x => x.REMSId,
                        principalSchema: "legacy",
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSAdditionalEntity_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSEngagement",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IndustryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JobTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BillingManagerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BillingProcessDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DepartmentDirectorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EngagementExecutiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EngagementFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FirstYearFeeEstimate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RealizationPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSEngagement", x => x.Id);
                    table.CheckConstraint("CK_REMSEngagement_Realization", "[RealizationPercentage] IS NULL OR ([RealizationPercentage] >= 0 AND [RealizationPercentage] <= 100)");
                    table.ForeignKey(
                        name: "FK_REMSEngagement_OptionSetItems_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagement_OptionSetItems_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagement_OptionSetItems_IndustryId",
                        column: x => x.IndustryId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagement_OptionSetItems_JobTemplateId",
                        column: x => x.JobTemplateId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagement_OptionSetItems_ServiceLineId",
                        column: x => x.ServiceLineId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagement_REMS_REMSId",
                        column: x => x.REMSId,
                        principalSchema: "legacy",
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagement_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagement_Users_BillingManagerId",
                        column: x => x.BillingManagerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagement_Users_DepartmentDirectorId",
                        column: x => x.DepartmentDirectorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagement_Users_EngagementExecutiveId",
                        column: x => x.EngagementExecutiveId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSFiles",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSFiles_Media_MediaId",
                        column: x => x.MediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSFiles_REMS_REMSId",
                        column: x => x.REMSId,
                        principalSchema: "legacy",
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSFiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSForm",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InviteCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InviteLockedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubmittedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSForm", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSForm_OptionSetItems_EntityTypeId",
                        column: x => x.EntityTypeId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSForm_REMS_REMSId",
                        column: x => x.REMSId,
                        principalSchema: "legacy",
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSForm_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSForm_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSMaconomyCustomerSync",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastAttemptOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SyncedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSMaconomyCustomerSync", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSMaconomyCustomerSync_Persons_ClientPersonId",
                        column: x => x.ClientPersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSMaconomyCustomerSync_REMS_REMSId",
                        column: x => x.REMSId,
                        principalSchema: "legacy",
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSMaconomyCustomerSync_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSSendBack",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResolvedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSSendBack", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSSendBack_REMS_REMSId",
                        column: x => x.REMSId,
                        principalSchema: "legacy",
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSSendBack_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSApprovalRound",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSEngagementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RoundNumber = table.Column<int>(type: "int", nullable: false),
                    SentByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SentOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSApprovalRound", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSApprovalRound_REMSEngagement_REMSEngagementId",
                        column: x => x.REMSEngagementId,
                        principalSchema: "legacy",
                        principalTable: "REMSEngagement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSApprovalRound_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSApprovalRound_Users_SentByUserId",
                        column: x => x.SentByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSEngagementApprover",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSEngagementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSEngagementApprover", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSEngagementApprover_REMSEngagement_REMSEngagementId",
                        column: x => x.REMSEngagementId,
                        principalSchema: "legacy",
                        principalTable: "REMSEngagement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementApprover_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementApprover_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSEngagementAuditDetail",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientAcceptanceFormMediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    REMSEngagementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientFiscalYearEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSEngagementAuditDetail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSEngagementAuditDetail_Media_ClientAcceptanceFormMediaId",
                        column: x => x.ClientAcceptanceFormMediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementAuditDetail_REMSEngagement_REMSEngagementId",
                        column: x => x.REMSEngagementId,
                        principalSchema: "legacy",
                        principalTable: "REMSEngagement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementAuditDetail_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSEngagementCommissionSplit",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSEngagementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommissionPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSEngagementCommissionSplit", x => x.Id);
                    table.CheckConstraint("CK_REMSEngagementCommissionSplit_Pct", "[CommissionPercentage] > 0 AND [CommissionPercentage] <= 100");
                    table.ForeignKey(
                        name: "FK_REMSEngagementCommissionSplit_REMSEngagement_REMSEngagementId",
                        column: x => x.REMSEngagementId,
                        principalSchema: "legacy",
                        principalTable: "REMSEngagement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementCommissionSplit_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementCommissionSplit_Users_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSEngagementGovernmentDetail",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderMediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    REMSEngagementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ContractNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ContractStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FloridaOnePercentStateFeeApplies = table.Column<bool>(type: "bit", nullable: true),
                    OriginalTerm = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PurchaseOrderAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PurchaseOrderEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PurchaseOrderNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PurchaseOrderStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RenewalTerms = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSEngagementGovernmentDetail", x => x.Id);
                    table.CheckConstraint("CK_REMSEngagementGovernmentDetail_PurchaseOrderAmount", "[PurchaseOrderAmount] IS NULL OR [PurchaseOrderAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_REMSEngagementGovernmentDetail_Media_PurchaseOrderMediaId",
                        column: x => x.PurchaseOrderMediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementGovernmentDetail_REMSEngagement_REMSEngagementId",
                        column: x => x.REMSEngagementId,
                        principalSchema: "legacy",
                        principalTable: "REMSEngagement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementGovernmentDetail_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSEngagementMarketingMethod",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MarketingMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSEngagementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSEngagementMarketingMethod", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSEngagementMarketingMethod_OptionSetItems_MarketingMethodId",
                        column: x => x.MarketingMethodId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementMarketingMethod_REMSEngagement_REMSEngagementId",
                        column: x => x.REMSEngagementId,
                        principalSchema: "legacy",
                        principalTable: "REMSEngagement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementMarketingMethod_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSEngagementTaxDetail",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSEngagementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalculatedDueDates = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstExtensionDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FiscalYearEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    OriginalDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSEngagementTaxDetail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSEngagementTaxDetail_REMSEngagement_REMSEngagementId",
                        column: x => x.REMSEngagementId,
                        principalSchema: "legacy",
                        principalTable: "REMSEngagement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementTaxDetail_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSFormDraft",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSFormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DraftPayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastSavedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSFormDraft", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSFormDraft_REMSForm_REMSFormId",
                        column: x => x.REMSFormId,
                        principalSchema: "legacy",
                        principalTable: "REMSForm",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_REMSFormDraft_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSFormEmailEvent",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSFormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ProviderPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecipientEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSFormEmailEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSFormEmailEvent_REMSForm_REMSFormId",
                        column: x => x.REMSFormId,
                        principalSchema: "legacy",
                        principalTable: "REMSForm",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSFormEmailEvent_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSFormSubmission",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSFormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedPayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSFormSubmission", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSFormSubmission_REMSForm_REMSFormId",
                        column: x => x.REMSFormId,
                        principalSchema: "legacy",
                        principalTable: "REMSForm",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSFormSubmission_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSApprovalTask",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSApprovalRoundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApproverRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSApprovalTask", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSApprovalTask_REMSApprovalRound_REMSApprovalRoundId",
                        column: x => x.REMSApprovalRoundId,
                        principalSchema: "legacy",
                        principalTable: "REMSApprovalRound",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSApprovalTask_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSApprovalTask_Users_ApproverId",
                        column: x => x.ApproverId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSEngagementPersonnelRate",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonnelLevelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSEngagementGovernmentDetailId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillRatePerHour = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSEngagementPersonnelRate", x => x.Id);
                    table.CheckConstraint("CK_REMSEngagementPersonnelRate_BillRatePerHour", "[BillRatePerHour] >= 0");
                    table.ForeignKey(
                        name: "FK_REMSEngagementPersonnelRate_OptionSetItems_PersonnelLevelId",
                        column: x => x.PersonnelLevelId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementPersonnelRate_REMSEngagementGovernmentDetail_REMSEngagementGovernmentDetailId",
                        column: x => x.REMSEngagementGovernmentDetailId,
                        principalSchema: "legacy",
                        principalTable: "REMSEngagementGovernmentDetail",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementPersonnelRate_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSEngagementTaxForm",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSEngagementTaxDetailId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxFormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSEngagementTaxForm", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSEngagementTaxForm_OptionSetItems_TaxFormId",
                        column: x => x.TaxFormId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementTaxForm_REMSEngagementTaxDetail_REMSEngagementTaxDetailId",
                        column: x => x.REMSEngagementTaxDetailId,
                        principalSchema: "legacy",
                        principalTable: "REMSEngagementTaxDetail",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEngagementTaxForm_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSClient",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceFormSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingContactName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BillingEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MobileNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReferralSourceDetail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSClient", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSClient_OptionSetItems_ReferralSourceId",
                        column: x => x.ReferralSourceId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSClient_REMSFormSubmission_SourceFormSubmissionId",
                        column: x => x.SourceFormSubmissionId,
                        principalSchema: "legacy",
                        principalTable: "REMSFormSubmission",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSClient_REMS_REMSId",
                        column: x => x.REMSId,
                        principalSchema: "legacy",
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSClient_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSApprovalChecklistItem",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSApprovalTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSApprovalChecklistItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSApprovalChecklistItem_REMSApprovalTask_REMSApprovalTaskId",
                        column: x => x.REMSApprovalTaskId,
                        principalSchema: "legacy",
                        principalTable: "REMSApprovalTask",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSApprovalChecklistItem_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSEntity",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EIN = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    IsMainEntity = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSEntity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSEntity_REMSClient_REMSClientId",
                        column: x => x.REMSClientId,
                        principalSchema: "legacy",
                        principalTable: "REMSClient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEntity_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSAdditionalIndividual",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelatedStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    REMSEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingFirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BillingLastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BillingPreference = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FilingType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsMinor = table.Column<bool>(type: "bit", nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RelationType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Suffix = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSAdditionalIndividual", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSAdditionalIndividual_OptionSetItems_RelatedStatusId",
                        column: x => x.RelatedStatusId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSAdditionalIndividual_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSAdditionalIndividual_REMSEntity_REMSEntityId",
                        column: x => x.REMSEntityId,
                        principalSchema: "legacy",
                        principalTable: "REMSEntity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSAdditionalIndividual_REMS_REMSId",
                        column: x => x.REMSId,
                        principalSchema: "legacy",
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSAdditionalIndividual_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSEntityAddress",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddressId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddressType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSEntityAddress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSEntityAddress_Addresses_AddressId",
                        column: x => x.AddressId,
                        principalTable: "Addresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEntityAddress_REMSEntity_REMSEntityId",
                        column: x => x.REMSEntityId,
                        principalSchema: "legacy",
                        principalTable: "REMSEntity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEntityAddress_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMSEntityContact",
                schema: "legacy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactRole = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMSEntityContact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMSEntityContact_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEntityContact_REMSEntity_REMSEntityId",
                        column: x => x.REMSEntityId,
                        principalSchema: "legacy",
                        principalTable: "REMSEntity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMSEntityContact_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Persons_TenantId_MaconomyCustomerNumber",
                table: "Persons",
                columns: new[] { "TenantId", "MaconomyCustomerNumber" },
                unique: true,
                filter: "[MaconomyCustomerNumber] IS NOT NULL AND [Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_TenantId_PartyType_LastName",
                table: "Persons",
                columns: new[] { "TenantId", "PartyType", "LastName" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_AdminAssignedToId",
                schema: "legacy",
                table: "REMS",
                column: "AdminAssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ClientPersonId",
                schema: "legacy",
                table: "REMS",
                column: "ClientPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_CSEId",
                schema: "legacy",
                table: "REMS",
                column: "CSEId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_OnBehalfOfUserId",
                schema: "legacy",
                table: "REMS",
                column: "OnBehalfOfUserId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_StatusId",
                schema: "legacy",
                table: "REMS",
                column: "StatusId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId",
                schema: "legacy",
                table: "REMS",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId_ClientPersonId",
                schema: "legacy",
                table: "REMS",
                columns: new[] { "TenantId", "ClientPersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId_CreatedById_StatusId",
                schema: "legacy",
                table: "REMS",
                columns: new[] { "TenantId", "CreatedById", "StatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId_OnBehalfOfUserId_StatusId",
                schema: "legacy",
                table: "REMS",
                columns: new[] { "TenantId", "OnBehalfOfUserId", "StatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId_REMSNumber",
                schema: "legacy",
                table: "REMS",
                columns: new[] { "TenantId", "REMSNumber" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId_StatusId_AdminAssignedToId_CreatedOnUtc",
                schema: "legacy",
                table: "REMS",
                columns: new[] { "TenantId", "StatusId", "AdminAssignedToId", "CreatedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TypeId",
                schema: "legacy",
                table: "REMS",
                column: "TypeId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSAdditionalEntity_RelatedStatusId",
                schema: "legacy",
                table: "REMSAdditionalEntity",
                column: "RelatedStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSAdditionalEntity_REMSId",
                schema: "legacy",
                table: "REMSAdditionalEntity",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSAdditionalEntity_TenantId",
                schema: "legacy",
                table: "REMSAdditionalEntity",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSAdditionalEntity_TenantId_REMSId_CreatedREMSId",
                schema: "legacy",
                table: "REMSAdditionalEntity",
                columns: new[] { "TenantId", "REMSId", "CreatedREMSId" });

            migrationBuilder.CreateIndex(
                name: "IX_REMSAdditionalEntity_TenantId_REMSId_SourceKey",
                schema: "legacy",
                table: "REMSAdditionalEntity",
                columns: new[] { "TenantId", "REMSId", "SourceKey" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSAdditionalIndividual_PersonId",
                schema: "legacy",
                table: "REMSAdditionalIndividual",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSAdditionalIndividual_RelatedStatusId",
                schema: "legacy",
                table: "REMSAdditionalIndividual",
                column: "RelatedStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSAdditionalIndividual_REMSEntityId",
                schema: "legacy",
                table: "REMSAdditionalIndividual",
                column: "REMSEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSAdditionalIndividual_REMSId",
                schema: "legacy",
                table: "REMSAdditionalIndividual",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSAdditionalIndividual_TenantId",
                schema: "legacy",
                table: "REMSAdditionalIndividual",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSAdditionalIndividual_TenantId_REMSEntityId",
                schema: "legacy",
                table: "REMSAdditionalIndividual",
                columns: new[] { "TenantId", "REMSEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_REMSAdditionalIndividual_TenantId_REMSId_SourceKey",
                schema: "legacy",
                table: "REMSAdditionalIndividual",
                columns: new[] { "TenantId", "REMSId", "SourceKey" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSApprovalChecklistItem_REMSApprovalTaskId",
                schema: "legacy",
                table: "REMSApprovalChecklistItem",
                column: "REMSApprovalTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSApprovalChecklistItem_TenantId",
                schema: "legacy",
                table: "REMSApprovalChecklistItem",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSApprovalChecklistItem_TenantId_REMSApprovalTaskId_DisplayOrder",
                schema: "legacy",
                table: "REMSApprovalChecklistItem",
                columns: new[] { "TenantId", "REMSApprovalTaskId", "DisplayOrder" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSApprovalRound_REMSEngagementId",
                schema: "legacy",
                table: "REMSApprovalRound",
                column: "REMSEngagementId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSApprovalRound_SentByUserId",
                schema: "legacy",
                table: "REMSApprovalRound",
                column: "SentByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSApprovalRound_TenantId",
                schema: "legacy",
                table: "REMSApprovalRound",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSApprovalRound_TenantId_REMSEngagementId_RoundNumber",
                schema: "legacy",
                table: "REMSApprovalRound",
                columns: new[] { "TenantId", "REMSEngagementId", "RoundNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_REMSApprovalTask_ApproverId",
                schema: "legacy",
                table: "REMSApprovalTask",
                column: "ApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSApprovalTask_REMSApprovalRoundId",
                schema: "legacy",
                table: "REMSApprovalTask",
                column: "REMSApprovalRoundId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSApprovalTask_TenantId",
                schema: "legacy",
                table: "REMSApprovalTask",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSApprovalTask_TenantId_ApproverId_Status",
                schema: "legacy",
                table: "REMSApprovalTask",
                columns: new[] { "TenantId", "ApproverId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_REMSApprovalTask_TenantId_REMSApprovalRoundId_ApproverId_ApproverRole",
                schema: "legacy",
                table: "REMSApprovalTask",
                columns: new[] { "TenantId", "REMSApprovalRoundId", "ApproverId", "ApproverRole" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSClient_ReferralSourceId",
                schema: "legacy",
                table: "REMSClient",
                column: "ReferralSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSClient_REMSId",
                schema: "legacy",
                table: "REMSClient",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSClient_SourceFormSubmissionId",
                schema: "legacy",
                table: "REMSClient",
                column: "SourceFormSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSClient_TenantId",
                schema: "legacy",
                table: "REMSClient",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSClient_TenantId_REMSId",
                schema: "legacy",
                table: "REMSClient",
                columns: new[] { "TenantId", "REMSId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagement_BillingManagerId",
                schema: "legacy",
                table: "REMSEngagement",
                column: "BillingManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagement_BillingPeriodId",
                schema: "legacy",
                table: "REMSEngagement",
                column: "BillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagement_DepartmentDirectorId",
                schema: "legacy",
                table: "REMSEngagement",
                column: "DepartmentDirectorId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagement_DepartmentId",
                schema: "legacy",
                table: "REMSEngagement",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagement_EngagementExecutiveId",
                schema: "legacy",
                table: "REMSEngagement",
                column: "EngagementExecutiveId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagement_IndustryId",
                schema: "legacy",
                table: "REMSEngagement",
                column: "IndustryId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagement_JobTemplateId",
                schema: "legacy",
                table: "REMSEngagement",
                column: "JobTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagement_REMSId",
                schema: "legacy",
                table: "REMSEngagement",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagement_ServiceLineId",
                schema: "legacy",
                table: "REMSEngagement",
                column: "ServiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagement_TenantId",
                schema: "legacy",
                table: "REMSEngagement",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagement_TenantId_REMSId",
                schema: "legacy",
                table: "REMSEngagement",
                columns: new[] { "TenantId", "REMSId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagement_TenantId_Status",
                schema: "legacy",
                table: "REMSEngagement",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementApprover_REMSEngagementId",
                schema: "legacy",
                table: "REMSEngagementApprover",
                column: "REMSEngagementId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementApprover_TenantId",
                schema: "legacy",
                table: "REMSEngagementApprover",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementApprover_TenantId_REMSEngagementId_UserId",
                schema: "legacy",
                table: "REMSEngagementApprover",
                columns: new[] { "TenantId", "REMSEngagementId", "UserId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementApprover_UserId",
                schema: "legacy",
                table: "REMSEngagementApprover",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementAuditDetail_ClientAcceptanceFormMediaId",
                schema: "legacy",
                table: "REMSEngagementAuditDetail",
                column: "ClientAcceptanceFormMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementAuditDetail_REMSEngagementId",
                schema: "legacy",
                table: "REMSEngagementAuditDetail",
                column: "REMSEngagementId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementAuditDetail_TenantId",
                schema: "legacy",
                table: "REMSEngagementAuditDetail",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementAuditDetail_TenantId_REMSEngagementId",
                schema: "legacy",
                table: "REMSEngagementAuditDetail",
                columns: new[] { "TenantId", "REMSEngagementId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementCommissionSplit_EmployeeId",
                schema: "legacy",
                table: "REMSEngagementCommissionSplit",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementCommissionSplit_REMSEngagementId",
                schema: "legacy",
                table: "REMSEngagementCommissionSplit",
                column: "REMSEngagementId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementCommissionSplit_TenantId",
                schema: "legacy",
                table: "REMSEngagementCommissionSplit",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementCommissionSplit_TenantId_REMSEngagementId_EmployeeId",
                schema: "legacy",
                table: "REMSEngagementCommissionSplit",
                columns: new[] { "TenantId", "REMSEngagementId", "EmployeeId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementGovernmentDetail_PurchaseOrderMediaId",
                schema: "legacy",
                table: "REMSEngagementGovernmentDetail",
                column: "PurchaseOrderMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementGovernmentDetail_REMSEngagementId",
                schema: "legacy",
                table: "REMSEngagementGovernmentDetail",
                column: "REMSEngagementId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementGovernmentDetail_TenantId",
                schema: "legacy",
                table: "REMSEngagementGovernmentDetail",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementGovernmentDetail_TenantId_REMSEngagementId",
                schema: "legacy",
                table: "REMSEngagementGovernmentDetail",
                columns: new[] { "TenantId", "REMSEngagementId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementMarketingMethod_MarketingMethodId",
                schema: "legacy",
                table: "REMSEngagementMarketingMethod",
                column: "MarketingMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementMarketingMethod_REMSEngagementId",
                schema: "legacy",
                table: "REMSEngagementMarketingMethod",
                column: "REMSEngagementId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementMarketingMethod_TenantId",
                schema: "legacy",
                table: "REMSEngagementMarketingMethod",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementMarketingMethod_TenantId_REMSEngagementId_MarketingMethodId",
                schema: "legacy",
                table: "REMSEngagementMarketingMethod",
                columns: new[] { "TenantId", "REMSEngagementId", "MarketingMethodId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementPersonnelRate_PersonnelLevelId",
                schema: "legacy",
                table: "REMSEngagementPersonnelRate",
                column: "PersonnelLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementPersonnelRate_REMSEngagementGovernmentDetailId",
                schema: "legacy",
                table: "REMSEngagementPersonnelRate",
                column: "REMSEngagementGovernmentDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementPersonnelRate_TenantId",
                schema: "legacy",
                table: "REMSEngagementPersonnelRate",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementPersonnelRate_TenantId_REMSEngagementGovernmentDetailId_PersonnelLevelId",
                schema: "legacy",
                table: "REMSEngagementPersonnelRate",
                columns: new[] { "TenantId", "REMSEngagementGovernmentDetailId", "PersonnelLevelId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementTaxDetail_REMSEngagementId",
                schema: "legacy",
                table: "REMSEngagementTaxDetail",
                column: "REMSEngagementId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementTaxDetail_TenantId",
                schema: "legacy",
                table: "REMSEngagementTaxDetail",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementTaxDetail_TenantId_REMSEngagementId",
                schema: "legacy",
                table: "REMSEngagementTaxDetail",
                columns: new[] { "TenantId", "REMSEngagementId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementTaxForm_REMSEngagementTaxDetailId",
                schema: "legacy",
                table: "REMSEngagementTaxForm",
                column: "REMSEngagementTaxDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementTaxForm_TaxFormId",
                schema: "legacy",
                table: "REMSEngagementTaxForm",
                column: "TaxFormId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementTaxForm_TenantId",
                schema: "legacy",
                table: "REMSEngagementTaxForm",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEngagementTaxForm_TenantId_REMSEngagementTaxDetailId_TaxFormId",
                schema: "legacy",
                table: "REMSEngagementTaxForm",
                columns: new[] { "TenantId", "REMSEngagementTaxDetailId", "TaxFormId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEntity_REMSClientId",
                schema: "legacy",
                table: "REMSEntity",
                column: "REMSClientId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEntity_TenantId",
                schema: "legacy",
                table: "REMSEntity",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEntity_TenantId_REMSClientId",
                schema: "legacy",
                table: "REMSEntity",
                columns: new[] { "TenantId", "REMSClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_REMSEntity_TenantId_REMSClientId_Main",
                schema: "legacy",
                table: "REMSEntity",
                columns: new[] { "TenantId", "REMSClientId" },
                unique: true,
                filter: "[IsMainEntity] = 1 AND [Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEntityAddress_AddressId",
                schema: "legacy",
                table: "REMSEntityAddress",
                column: "AddressId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEntityAddress_REMSEntityId",
                schema: "legacy",
                table: "REMSEntityAddress",
                column: "REMSEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEntityAddress_TenantId",
                schema: "legacy",
                table: "REMSEntityAddress",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEntityAddress_TenantId_REMSEntityId_AddressType",
                schema: "legacy",
                table: "REMSEntityAddress",
                columns: new[] { "TenantId", "REMSEntityId", "AddressType" },
                unique: true,
                filter: "[Deleted] = 0 AND [AddressType] <> 'Billing'");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEntityContact_PersonId",
                schema: "legacy",
                table: "REMSEntityContact",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEntityContact_REMSEntityId",
                schema: "legacy",
                table: "REMSEntityContact",
                column: "REMSEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEntityContact_TenantId",
                schema: "legacy",
                table: "REMSEntityContact",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSEntityContact_TenantId_REMSEntityId_ContactRole",
                schema: "legacy",
                table: "REMSEntityContact",
                columns: new[] { "TenantId", "REMSEntityId", "ContactRole" },
                unique: true,
                filter: "[Deleted] = 0 AND [ContactRole] <> 'BillingContact'");

            migrationBuilder.CreateIndex(
                name: "IX_REMSFiles_MediaId",
                schema: "legacy",
                table: "REMSFiles",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSFiles_REMSId",
                schema: "legacy",
                table: "REMSFiles",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSFiles_TenantId",
                schema: "legacy",
                table: "REMSFiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSFiles_TenantId_REMSId_MediaId",
                schema: "legacy",
                table: "REMSFiles",
                columns: new[] { "TenantId", "REMSId", "MediaId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSForm_CreatedByUserId",
                schema: "legacy",
                table: "REMSForm",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSForm_EntityTypeId",
                schema: "legacy",
                table: "REMSForm",
                column: "EntityTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSForm_REMSId",
                schema: "legacy",
                table: "REMSForm",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSForm_TenantId",
                schema: "legacy",
                table: "REMSForm",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSForm_TenantId_InviteCode",
                schema: "legacy",
                table: "REMSForm",
                columns: new[] { "TenantId", "InviteCode" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSForm_TenantId_REMSId",
                schema: "legacy",
                table: "REMSForm",
                columns: new[] { "TenantId", "REMSId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSForm_TenantId_REMSId_Status",
                schema: "legacy",
                table: "REMSForm",
                columns: new[] { "TenantId", "REMSId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_REMSFormDraft_REMSFormId",
                schema: "legacy",
                table: "REMSFormDraft",
                column: "REMSFormId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSFormDraft_TenantId",
                schema: "legacy",
                table: "REMSFormDraft",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSFormDraft_TenantId_REMSFormId",
                schema: "legacy",
                table: "REMSFormDraft",
                columns: new[] { "TenantId", "REMSFormId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSFormEmailEvent_REMSFormId",
                schema: "legacy",
                table: "REMSFormEmailEvent",
                column: "REMSFormId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSFormEmailEvent_TenantId",
                schema: "legacy",
                table: "REMSFormEmailEvent",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSFormEmailEvent_TenantId_ProviderMessageId_EventType",
                schema: "legacy",
                table: "REMSFormEmailEvent",
                columns: new[] { "TenantId", "ProviderMessageId", "EventType" },
                unique: true,
                filter: "[ProviderMessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_REMSFormSubmission_REMSFormId",
                schema: "legacy",
                table: "REMSFormSubmission",
                column: "REMSFormId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSFormSubmission_TenantId",
                schema: "legacy",
                table: "REMSFormSubmission",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSFormSubmission_TenantId_REMSFormId",
                schema: "legacy",
                table: "REMSFormSubmission",
                columns: new[] { "TenantId", "REMSFormId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_REMSMaconomyCustomerSync_ClientPersonId",
                schema: "legacy",
                table: "REMSMaconomyCustomerSync",
                column: "ClientPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSMaconomyCustomerSync_REMSId",
                schema: "legacy",
                table: "REMSMaconomyCustomerSync",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSMaconomyCustomerSync_TenantId",
                schema: "legacy",
                table: "REMSMaconomyCustomerSync",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSMaconomyCustomerSync_TenantId_REMSId",
                schema: "legacy",
                table: "REMSMaconomyCustomerSync",
                columns: new[] { "TenantId", "REMSId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMSMaconomyCustomerSync_TenantId_Status",
                schema: "legacy",
                table: "REMSMaconomyCustomerSync",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_REMSSendBack_REMSId",
                schema: "legacy",
                table: "REMSSendBack",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSSendBack_TenantId",
                schema: "legacy",
                table: "REMSSendBack",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMSSendBack_TenantId_REMSId",
                schema: "legacy",
                table: "REMSSendBack",
                columns: new[] { "TenantId", "REMSId" },
                unique: true,
                filter: "[Deleted] = 0 AND [ResolvedOnUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_REMSSendBack_TenantId_REMSId_CreatedOnUtc",
                schema: "legacy",
                table: "REMSSendBack",
                columns: new[] { "TenantId", "REMSId", "CreatedOnUtc" });
        }
    }
}
