using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemsDataModelV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RemsDepartmentDirector_OptionSetItems_DepartmentId",
                table: "RemsDepartmentDirector");

            migrationBuilder.DropForeignKey(
                name: "FK_RemsDepartmentDirector_RemsSettings_RemsSettingsId",
                table: "RemsDepartmentDirector");

            migrationBuilder.DropForeignKey(
                name: "FK_RemsDepartmentDirector_Tenants_TenantId",
                table: "RemsDepartmentDirector");

            migrationBuilder.DropForeignKey(
                name: "FK_RemsDepartmentDirector_Users_DirectorUserId",
                table: "RemsDepartmentDirector");

            migrationBuilder.DropForeignKey(
                name: "FK_RemsSettings_Tenants_TenantId",
                table: "RemsSettings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RemsSettings",
                table: "RemsSettings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RemsDepartmentDirector",
                table: "RemsDepartmentDirector");

            migrationBuilder.RenameTable(
                name: "RemsSettings",
                newName: "REMS_Settings");

            migrationBuilder.RenameTable(
                name: "RemsDepartmentDirector",
                newName: "REMS_DepartmentDirector");

            migrationBuilder.RenameIndex(
                name: "IX_RemsSettings_TenantId",
                table: "REMS_Settings",
                newName: "IX_REMS_Settings_TenantId");

            migrationBuilder.RenameColumn(
                name: "RemsSettingsId",
                table: "REMS_DepartmentDirector",
                newName: "SettingsId");

            migrationBuilder.RenameIndex(
                name: "IX_RemsDepartmentDirector_TenantId_DepartmentId",
                table: "REMS_DepartmentDirector",
                newName: "IX_REMS_DepartmentDirector_TenantId_DepartmentId");

            migrationBuilder.RenameIndex(
                name: "IX_RemsDepartmentDirector_TenantId",
                table: "REMS_DepartmentDirector",
                newName: "IX_REMS_DepartmentDirector_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_RemsDepartmentDirector_RemsSettingsId",
                table: "REMS_DepartmentDirector",
                newName: "IX_REMS_DepartmentDirector_SettingsId");

            migrationBuilder.RenameIndex(
                name: "IX_RemsDepartmentDirector_DirectorUserId",
                table: "REMS_DepartmentDirector",
                newName: "IX_REMS_DepartmentDirector_DirectorUserId");

            migrationBuilder.RenameIndex(
                name: "IX_RemsDepartmentDirector_DepartmentId",
                table: "REMS_DepartmentDirector",
                newName: "IX_REMS_DepartmentDirector_DepartmentId");

            migrationBuilder.CreateSequence<int>(
                name: "seq_ClientNumber");

            migrationBuilder.CreateSequence<int>(
                name: "seq_RemsNumber");

            migrationBuilder.AddColumn<int>(
                name: "DeclineThreshold",
                table: "REMS_Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsStaticRouting",
                table: "REMS_Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxFeeCeilingWithoutShareholder",
                table: "REMS_Settings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_REMS_Settings",
                table: "REMS_Settings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_REMS_DepartmentDirector",
                table: "REMS_DepartmentDirector",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "Client",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LegalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EntityTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IndustryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EIN = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FiscalYearEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    PrimaryEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PrimaryPhone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ReferralSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReferralSourceDetail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MaconomyCustomerNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MaconomySyncStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MaconomySyncedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaconomySyncError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Client", x => x.Id);
                    table.CheckConstraint("CK_Client_Party", "[PersonId] IS NOT NULL OR [LegalName] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Client_OptionSetItems_EntityTypeId",
                        column: x => x.EntityTypeId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_OptionSetItems_IndustryId",
                        column: x => x.IndustryId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_OptionSetItems_ReferralSourceId",
                        column: x => x.ReferralSourceId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMS_ChecklistTemplate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SettingsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApproverRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS_ChecklistTemplate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMS_ChecklistTemplate_REMS_Settings_SettingsId",
                        column: x => x.SettingsId,
                        principalTable: "REMS_Settings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_ChecklistTemplate_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Client_Address",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddressId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddressTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Client_Address", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Client_Address_Addresses_AddressId",
                        column: x => x.AddressId,
                        principalTable: "Addresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Address_Client_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Address_OptionSetItems_AddressTypeId",
                        column: x => x.AddressTypeId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Address_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Client_Contact",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactRoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Client_Contact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Client_Contact_Client_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Contact_OptionSetItems_ContactRoleId",
                        column: x => x.ContactRoleId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Contact_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Contact_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Client_Contract",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ContractEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OriginalTerm = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RenewalTerms = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PurchaseOrderStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PurchaseOrderEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Client_Contract", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Client_Contract_Client_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Contract_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Client_Relationship",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelatedClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FilingTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BillingPreferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsMinor = table.Column<bool>(type: "bit", nullable: false),
                    RelatedStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Client_Relationship", x => x.Id);
                    table.CheckConstraint("CK_Client_Relationship_Distinct", "[ClientId] <> [RelatedClientId]");
                    table.ForeignKey(
                        name: "FK_Client_Relationship_Client_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Relationship_Client_RelatedClientId",
                        column: x => x.RelatedClientId,
                        principalTable: "Client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Relationship_OptionSetItems_BillingPreferenceId",
                        column: x => x.BillingPreferenceId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Relationship_OptionSetItems_FilingTypeId",
                        column: x => x.FilingTypeId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Relationship_OptionSetItems_RelatedStatusId",
                        column: x => x.RelatedStatusId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Relationship_OptionSetItems_RelationTypeId",
                        column: x => x.RelationTypeId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Client_Relationship_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMS",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RemsNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CSEId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AdminAssignedToId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OnBehalfOfUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InviteCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InviteRecipientEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    InviteSentByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InviteSentOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FormLastSavedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FormSubmittedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JobTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BillingPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DepartmentDirectorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EngagementExecutiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BillingManagerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FirstYearFeeEstimate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    EngagementFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RealizationPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    BillingProcessDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContractNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PurchaseOrderNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PurchaseOrderAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsFloridaOnePercentFeeApplicable = table.Column<bool>(type: "bit", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS", x => x.Id);
                    table.CheckConstraint("CK_REMS_PurchaseOrderAmount", "[PurchaseOrderAmount] IS NULL OR [PurchaseOrderAmount] >= 0");
                    table.CheckConstraint("CK_REMS_Realization", "[RealizationPercentage] IS NULL OR ([RealizationPercentage] >= 0 AND [RealizationPercentage] <= 100)");
                    table.ForeignKey(
                        name: "FK_REMS_Client_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_Client_Contract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Client_Contract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_OptionSetItems_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_OptionSetItems_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_OptionSetItems_JobTemplateId",
                        column: x => x.JobTemplateId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_OptionSetItems_ServiceLineId",
                        column: x => x.ServiceLineId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                        name: "FK_REMS_Users_BillingManagerId",
                        column: x => x.BillingManagerId,
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
                        name: "FK_REMS_Users_DepartmentDirectorId",
                        column: x => x.DepartmentDirectorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_Users_EngagementExecutiveId",
                        column: x => x.EngagementExecutiveId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_Users_InviteSentByUserId",
                        column: x => x.InviteSentByUserId,
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
                name: "REMS_ApprovalTask",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoundNumber = table.Column<int>(type: "int", nullable: false),
                    ApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApproverRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecidedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS_ApprovalTask", x => x.Id);
                    table.CheckConstraint("CK_REMS_ApprovalTask_Round", "[RoundNumber] >= 1");
                    table.ForeignKey(
                        name: "FK_REMS_ApprovalTask_REMS_REMSId",
                        column: x => x.REMSId,
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_ApprovalTask_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_ApprovalTask_Users_ApproverId",
                        column: x => x.ApproverId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMS_Approver",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS_Approver", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMS_Approver_REMS_REMSId",
                        column: x => x.REMSId,
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_Approver_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_Approver_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMS_CommissionSplit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommissionPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS_CommissionSplit", x => x.Id);
                    table.CheckConstraint("CK_REMS_CommissionSplit_Pct", "[CommissionPercentage] > 0 AND [CommissionPercentage] <= 100");
                    table.ForeignKey(
                        name: "FK_REMS_CommissionSplit_REMS_REMSId",
                        column: x => x.REMSId,
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_CommissionSplit_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_CommissionSplit_Users_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMS_EmailEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    OccurredOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS_EmailEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMS_EmailEvent_EmailTemplates_EmailTemplateId",
                        column: x => x.EmailTemplateId,
                        principalTable: "EmailTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_EmailEvent_REMS_REMSId",
                        column: x => x.REMSId,
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_EmailEvent_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMS_File",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS_File", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMS_File_Media_MediaId",
                        column: x => x.MediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_File_REMS_REMSId",
                        column: x => x.REMSId,
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_File_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMS_MarketingMethod",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MarketingMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS_MarketingMethod", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMS_MarketingMethod_OptionSetItems_MarketingMethodId",
                        column: x => x.MarketingMethodId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_MarketingMethod_REMS_REMSId",
                        column: x => x.REMSId,
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_MarketingMethod_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMS_PersonnelRate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonnelLevelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillRatePerHour = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS_PersonnelRate", x => x.Id);
                    table.CheckConstraint("CK_REMS_PersonnelRate_BillRatePerHour", "[BillRatePerHour] >= 0");
                    table.ForeignKey(
                        name: "FK_REMS_PersonnelRate_OptionSetItems_PersonnelLevelId",
                        column: x => x.PersonnelLevelId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_PersonnelRate_REMS_REMSId",
                        column: x => x.REMSId,
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_PersonnelRate_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMS_StatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReturnedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS_StatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMS_StatusHistory_OptionSetItems_FromStatusId",
                        column: x => x.FromStatusId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_StatusHistory_OptionSetItems_ToStatusId",
                        column: x => x.ToStatusId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_StatusHistory_REMS_REMSId",
                        column: x => x.REMSId,
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_StatusHistory_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_StatusHistory_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_StatusHistory_Users_ReturnedToUserId",
                        column: x => x.ReturnedToUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMS_TaxDueDate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    IsOverridden = table.Column<bool>(type: "bit", nullable: false),
                    OverriddenByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS_TaxDueDate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMS_TaxDueDate_REMS_REMSId",
                        column: x => x.REMSId,
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_TaxDueDate_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_TaxDueDate_Users_OverriddenByUserId",
                        column: x => x.OverriddenByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMS_TaxForm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    REMSId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxFormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS_TaxForm", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMS_TaxForm_OptionSetItems_TaxFormId",
                        column: x => x.TaxFormId,
                        principalTable: "OptionSetItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_TaxForm_REMS_REMSId",
                        column: x => x.REMSId,
                        principalTable: "REMS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_TaxForm_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REMS_ApprovalChecklistItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    CompletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REMS_ApprovalChecklistItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_REMS_ApprovalChecklistItem_REMS_ApprovalTask_TaskId",
                        column: x => x.TaskId,
                        principalTable: "REMS_ApprovalTask",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REMS_ApprovalChecklistItem_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Client_EntityTypeId",
                table: "Client",
                column: "EntityTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_IndustryId",
                table: "Client",
                column: "IndustryId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_PersonId",
                table: "Client",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_ReferralSourceId",
                table: "Client",
                column: "ReferralSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_TenantId",
                table: "Client",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_TenantId_ClientNumber",
                table: "Client",
                columns: new[] { "TenantId", "ClientNumber" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Client_TenantId_MaconomyCustomerNumber",
                table: "Client",
                columns: new[] { "TenantId", "MaconomyCustomerNumber" },
                filter: "[MaconomyCustomerNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Client_TenantId_PersonId",
                table: "Client",
                columns: new[] { "TenantId", "PersonId" },
                unique: true,
                filter: "[Deleted] = 0 AND [PersonId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Client_TenantId_PrimaryEmail",
                table: "Client",
                columns: new[] { "TenantId", "PrimaryEmail" },
                filter: "[PrimaryEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Client_TenantId_Status",
                table: "Client",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Client_Address_AddressId",
                table: "Client_Address",
                column: "AddressId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Address_AddressTypeId",
                table: "Client_Address",
                column: "AddressTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Address_ClientId",
                table: "Client_Address",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Address_TenantId",
                table: "Client_Address",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Address_TenantId_ClientId_AddressTypeId",
                table: "Client_Address",
                columns: new[] { "TenantId", "ClientId", "AddressTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Client_Contact_ClientId",
                table: "Client_Contact",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Contact_ContactRoleId",
                table: "Client_Contact",
                column: "ContactRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Contact_PersonId",
                table: "Client_Contact",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Contact_TenantId",
                table: "Client_Contact",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Contact_TenantId_ClientId_PersonId_ContactRoleId",
                table: "Client_Contact",
                columns: new[] { "TenantId", "ClientId", "PersonId", "ContactRoleId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Contract_ClientId",
                table: "Client_Contract",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Contract_Current",
                table: "Client_Contract",
                columns: new[] { "TenantId", "ClientId" },
                unique: true,
                filter: "[Deleted] = 0 AND [IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Contract_TenantId",
                table: "Client_Contract",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Contract_TenantId_ClientId",
                table: "Client_Contract",
                columns: new[] { "TenantId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_Client_Relationship_BillingPreferenceId",
                table: "Client_Relationship",
                column: "BillingPreferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Relationship_ClientId",
                table: "Client_Relationship",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Relationship_FilingTypeId",
                table: "Client_Relationship",
                column: "FilingTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Relationship_RelatedClientId",
                table: "Client_Relationship",
                column: "RelatedClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Relationship_RelatedStatusId",
                table: "Client_Relationship",
                column: "RelatedStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Relationship_RelationTypeId",
                table: "Client_Relationship",
                column: "RelationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Relationship_TenantId",
                table: "Client_Relationship",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Relationship_TenantId_ClientId_RelatedClientId",
                table: "Client_Relationship",
                columns: new[] { "TenantId", "ClientId", "RelatedClientId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Client_Relationship_TenantId_RelatedClientId",
                table: "Client_Relationship",
                columns: new[] { "TenantId", "RelatedClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_AdminAssignedToId",
                table: "REMS",
                column: "AdminAssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_BillingManagerId",
                table: "REMS",
                column: "BillingManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_BillingPeriodId",
                table: "REMS",
                column: "BillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ClientId",
                table: "REMS",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ContractId",
                table: "REMS",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_CSEId",
                table: "REMS",
                column: "CSEId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_DepartmentDirectorId",
                table: "REMS",
                column: "DepartmentDirectorId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_DepartmentId",
                table: "REMS",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_EngagementExecutiveId",
                table: "REMS",
                column: "EngagementExecutiveId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_InviteSentByUserId",
                table: "REMS",
                column: "InviteSentByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_JobTemplateId",
                table: "REMS",
                column: "JobTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_OnBehalfOfUserId",
                table: "REMS",
                column: "OnBehalfOfUserId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ServiceLineId",
                table: "REMS",
                column: "ServiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_StatusId",
                table: "REMS",
                column: "StatusId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId",
                table: "REMS",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId_ClientId",
                table: "REMS",
                columns: new[] { "TenantId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId_CreatedById_StatusId",
                table: "REMS",
                columns: new[] { "TenantId", "CreatedById", "StatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId_DepartmentId",
                table: "REMS",
                columns: new[] { "TenantId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId_InviteCode",
                table: "REMS",
                columns: new[] { "TenantId", "InviteCode" },
                unique: true,
                filter: "[Deleted] = 0 AND [InviteCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId_OnBehalfOfUserId_StatusId",
                table: "REMS",
                columns: new[] { "TenantId", "OnBehalfOfUserId", "StatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId_RemsNumber",
                table: "REMS",
                columns: new[] { "TenantId", "RemsNumber" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TenantId_StatusId_AdminAssignedToId_CreatedOnUtc",
                table: "REMS",
                columns: new[] { "TenantId", "StatusId", "AdminAssignedToId", "CreatedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TypeId",
                table: "REMS",
                column: "TypeId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ApprovalChecklistItem_TaskId",
                table: "REMS_ApprovalChecklistItem",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ApprovalChecklistItem_TenantId",
                table: "REMS_ApprovalChecklistItem",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ApprovalChecklistItem_TenantId_TaskId_DisplayOrder",
                table: "REMS_ApprovalChecklistItem",
                columns: new[] { "TenantId", "TaskId", "DisplayOrder" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ApprovalTask_ApproverId",
                table: "REMS_ApprovalTask",
                column: "ApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ApprovalTask_REMSId",
                table: "REMS_ApprovalTask",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ApprovalTask_TenantId",
                table: "REMS_ApprovalTask",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ApprovalTask_TenantId_ApproverId_Status",
                table: "REMS_ApprovalTask",
                columns: new[] { "TenantId", "ApproverId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ApprovalTask_TenantId_REMSId_RoundNumber",
                table: "REMS_ApprovalTask",
                columns: new[] { "TenantId", "REMSId", "RoundNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ApprovalTask_TenantId_REMSId_RoundNumber_ApproverId_ApproverRole",
                table: "REMS_ApprovalTask",
                columns: new[] { "TenantId", "REMSId", "RoundNumber", "ApproverId", "ApproverRole" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_Approver_REMSId",
                table: "REMS_Approver",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_Approver_TenantId",
                table: "REMS_Approver",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_Approver_TenantId_REMSId_UserId",
                table: "REMS_Approver",
                columns: new[] { "TenantId", "REMSId", "UserId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_Approver_UserId",
                table: "REMS_Approver",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ChecklistTemplate_SettingsId",
                table: "REMS_ChecklistTemplate",
                column: "SettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ChecklistTemplate_TenantId",
                table: "REMS_ChecklistTemplate",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_ChecklistTemplate_TenantId_SettingsId_ApproverRole_DisplayOrder",
                table: "REMS_ChecklistTemplate",
                columns: new[] { "TenantId", "SettingsId", "ApproverRole", "DisplayOrder" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_CommissionSplit_EmployeeId",
                table: "REMS_CommissionSplit",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_CommissionSplit_REMSId",
                table: "REMS_CommissionSplit",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_CommissionSplit_TenantId",
                table: "REMS_CommissionSplit",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_CommissionSplit_TenantId_REMSId_EmployeeId",
                table: "REMS_CommissionSplit",
                columns: new[] { "TenantId", "REMSId", "EmployeeId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_EmailEvent_EmailTemplateId",
                table: "REMS_EmailEvent",
                column: "EmailTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_EmailEvent_REMSId",
                table: "REMS_EmailEvent",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_EmailEvent_TenantId",
                table: "REMS_EmailEvent",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_EmailEvent_TenantId_ProviderMessageId_EventType",
                table: "REMS_EmailEvent",
                columns: new[] { "TenantId", "ProviderMessageId", "EventType" },
                unique: true,
                filter: "[ProviderMessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_EmailEvent_TenantId_REMSId_OccurredOnUtc",
                table: "REMS_EmailEvent",
                columns: new[] { "TenantId", "REMSId", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_File_MediaId",
                table: "REMS_File",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_File_REMSId",
                table: "REMS_File",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_File_TenantId",
                table: "REMS_File",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_File_TenantId_REMSId_Kind",
                table: "REMS_File",
                columns: new[] { "TenantId", "REMSId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_File_TenantId_REMSId_MediaId",
                table: "REMS_File",
                columns: new[] { "TenantId", "REMSId", "MediaId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_MarketingMethod_MarketingMethodId",
                table: "REMS_MarketingMethod",
                column: "MarketingMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_MarketingMethod_REMSId",
                table: "REMS_MarketingMethod",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_MarketingMethod_TenantId",
                table: "REMS_MarketingMethod",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_MarketingMethod_TenantId_REMSId_MarketingMethodId",
                table: "REMS_MarketingMethod",
                columns: new[] { "TenantId", "REMSId", "MarketingMethodId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_PersonnelRate_PersonnelLevelId",
                table: "REMS_PersonnelRate",
                column: "PersonnelLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_PersonnelRate_REMSId",
                table: "REMS_PersonnelRate",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_PersonnelRate_TenantId",
                table: "REMS_PersonnelRate",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_PersonnelRate_TenantId_REMSId_PersonnelLevelId",
                table: "REMS_PersonnelRate",
                columns: new[] { "TenantId", "REMSId", "PersonnelLevelId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_StatusHistory_ChangedByUserId",
                table: "REMS_StatusHistory",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_StatusHistory_FromStatusId",
                table: "REMS_StatusHistory",
                column: "FromStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_StatusHistory_REMSId",
                table: "REMS_StatusHistory",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_StatusHistory_ReturnedToUserId",
                table: "REMS_StatusHistory",
                column: "ReturnedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_StatusHistory_TenantId",
                table: "REMS_StatusHistory",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_StatusHistory_TenantId_REMSId_ChangedOnUtc",
                table: "REMS_StatusHistory",
                columns: new[] { "TenantId", "REMSId", "ChangedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_REMS_StatusHistory_ToStatusId",
                table: "REMS_StatusHistory",
                column: "ToStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TaxDueDate_OverriddenByUserId",
                table: "REMS_TaxDueDate",
                column: "OverriddenByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TaxDueDate_REMSId",
                table: "REMS_TaxDueDate",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TaxDueDate_TenantId",
                table: "REMS_TaxDueDate",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TaxDueDate_TenantId_REMSId_Kind",
                table: "REMS_TaxDueDate",
                columns: new[] { "TenantId", "REMSId", "Kind" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TaxForm_REMSId",
                table: "REMS_TaxForm",
                column: "REMSId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TaxForm_TaxFormId",
                table: "REMS_TaxForm",
                column: "TaxFormId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TaxForm_TenantId",
                table: "REMS_TaxForm",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_REMS_TaxForm_TenantId_REMSId_TaxFormId",
                table: "REMS_TaxForm",
                columns: new[] { "TenantId", "REMSId", "TaxFormId" },
                unique: true,
                filter: "[Deleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_REMS_DepartmentDirector_OptionSetItems_DepartmentId",
                table: "REMS_DepartmentDirector",
                column: "DepartmentId",
                principalTable: "OptionSetItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_REMS_DepartmentDirector_REMS_Settings_SettingsId",
                table: "REMS_DepartmentDirector",
                column: "SettingsId",
                principalTable: "REMS_Settings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_REMS_DepartmentDirector_Tenants_TenantId",
                table: "REMS_DepartmentDirector",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_REMS_DepartmentDirector_Users_DirectorUserId",
                table: "REMS_DepartmentDirector",
                column: "DirectorUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_REMS_Settings_Tenants_TenantId",
                table: "REMS_Settings",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_REMS_DepartmentDirector_OptionSetItems_DepartmentId",
                table: "REMS_DepartmentDirector");

            migrationBuilder.DropForeignKey(
                name: "FK_REMS_DepartmentDirector_REMS_Settings_SettingsId",
                table: "REMS_DepartmentDirector");

            migrationBuilder.DropForeignKey(
                name: "FK_REMS_DepartmentDirector_Tenants_TenantId",
                table: "REMS_DepartmentDirector");

            migrationBuilder.DropForeignKey(
                name: "FK_REMS_DepartmentDirector_Users_DirectorUserId",
                table: "REMS_DepartmentDirector");

            migrationBuilder.DropForeignKey(
                name: "FK_REMS_Settings_Tenants_TenantId",
                table: "REMS_Settings");

            migrationBuilder.DropTable(
                name: "Client_Address");

            migrationBuilder.DropTable(
                name: "Client_Contact");

            migrationBuilder.DropTable(
                name: "Client_Relationship");

            migrationBuilder.DropTable(
                name: "REMS_ApprovalChecklistItem");

            migrationBuilder.DropTable(
                name: "REMS_Approver");

            migrationBuilder.DropTable(
                name: "REMS_ChecklistTemplate");

            migrationBuilder.DropTable(
                name: "REMS_CommissionSplit");

            migrationBuilder.DropTable(
                name: "REMS_EmailEvent");

            migrationBuilder.DropTable(
                name: "REMS_File");

            migrationBuilder.DropTable(
                name: "REMS_MarketingMethod");

            migrationBuilder.DropTable(
                name: "REMS_PersonnelRate");

            migrationBuilder.DropTable(
                name: "REMS_StatusHistory");

            migrationBuilder.DropTable(
                name: "REMS_TaxDueDate");

            migrationBuilder.DropTable(
                name: "REMS_TaxForm");

            migrationBuilder.DropTable(
                name: "REMS_ApprovalTask");

            migrationBuilder.DropTable(
                name: "REMS");

            migrationBuilder.DropTable(
                name: "Client_Contract");

            migrationBuilder.DropTable(
                name: "Client");

            migrationBuilder.DropPrimaryKey(
                name: "PK_REMS_Settings",
                table: "REMS_Settings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_REMS_DepartmentDirector",
                table: "REMS_DepartmentDirector");

            migrationBuilder.DropColumn(
                name: "DeclineThreshold",
                table: "REMS_Settings");

            migrationBuilder.DropColumn(
                name: "IsStaticRouting",
                table: "REMS_Settings");

            migrationBuilder.DropColumn(
                name: "TaxFeeCeilingWithoutShareholder",
                table: "REMS_Settings");

            migrationBuilder.DropSequence(
                name: "seq_ClientNumber");

            migrationBuilder.DropSequence(
                name: "seq_RemsNumber");

            migrationBuilder.RenameTable(
                name: "REMS_Settings",
                newName: "RemsSettings");

            migrationBuilder.RenameTable(
                name: "REMS_DepartmentDirector",
                newName: "RemsDepartmentDirector");

            migrationBuilder.RenameIndex(
                name: "IX_REMS_Settings_TenantId",
                table: "RemsSettings",
                newName: "IX_RemsSettings_TenantId");

            migrationBuilder.RenameColumn(
                name: "SettingsId",
                table: "RemsDepartmentDirector",
                newName: "RemsSettingsId");

            migrationBuilder.RenameIndex(
                name: "IX_REMS_DepartmentDirector_TenantId_DepartmentId",
                table: "RemsDepartmentDirector",
                newName: "IX_RemsDepartmentDirector_TenantId_DepartmentId");

            migrationBuilder.RenameIndex(
                name: "IX_REMS_DepartmentDirector_TenantId",
                table: "RemsDepartmentDirector",
                newName: "IX_RemsDepartmentDirector_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_REMS_DepartmentDirector_SettingsId",
                table: "RemsDepartmentDirector",
                newName: "IX_RemsDepartmentDirector_RemsSettingsId");

            migrationBuilder.RenameIndex(
                name: "IX_REMS_DepartmentDirector_DirectorUserId",
                table: "RemsDepartmentDirector",
                newName: "IX_RemsDepartmentDirector_DirectorUserId");

            migrationBuilder.RenameIndex(
                name: "IX_REMS_DepartmentDirector_DepartmentId",
                table: "RemsDepartmentDirector",
                newName: "IX_RemsDepartmentDirector_DepartmentId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RemsSettings",
                table: "RemsSettings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RemsDepartmentDirector",
                table: "RemsDepartmentDirector",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RemsDepartmentDirector_OptionSetItems_DepartmentId",
                table: "RemsDepartmentDirector",
                column: "DepartmentId",
                principalTable: "OptionSetItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RemsDepartmentDirector_RemsSettings_RemsSettingsId",
                table: "RemsDepartmentDirector",
                column: "RemsSettingsId",
                principalTable: "RemsSettings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RemsDepartmentDirector_Tenants_TenantId",
                table: "RemsDepartmentDirector",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RemsDepartmentDirector_Users_DirectorUserId",
                table: "RemsDepartmentDirector",
                column: "DirectorUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RemsSettings_Tenants_TenantId",
                table: "RemsSettings",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
