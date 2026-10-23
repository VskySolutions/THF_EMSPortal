using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemsLegacySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_REMSDelegation_Tenants_TenantId",
                table: "REMSDelegation");

            migrationBuilder.DropForeignKey(
                name: "FK_REMSDelegation_Users_DelegateUserId",
                table: "REMSDelegation");

            migrationBuilder.DropForeignKey(
                name: "FK_REMSDelegation_Users_PrincipalUserId",
                table: "REMSDelegation");

            migrationBuilder.DropPrimaryKey(
                name: "PK_REMSDelegation",
                table: "REMSDelegation");

            migrationBuilder.EnsureSchema(
                name: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSSendBack",
                newName: "REMSSendBack",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSMaconomyCustomerSync",
                newName: "REMSMaconomyCustomerSync",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSFormSubmission",
                newName: "REMSFormSubmission",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSFormEmailEvent",
                newName: "REMSFormEmailEvent",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSFormDraft",
                newName: "REMSFormDraft",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSForm",
                newName: "REMSForm",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSFiles",
                newName: "REMSFiles",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSEntityContact",
                newName: "REMSEntityContact",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSEntityAddress",
                newName: "REMSEntityAddress",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSEntity",
                newName: "REMSEntity",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSEngagementTaxForm",
                newName: "REMSEngagementTaxForm",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSEngagementTaxDetail",
                newName: "REMSEngagementTaxDetail",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSEngagementPersonnelRate",
                newName: "REMSEngagementPersonnelRate",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSEngagementMarketingMethod",
                newName: "REMSEngagementMarketingMethod",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSEngagementGovernmentDetail",
                newName: "REMSEngagementGovernmentDetail",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSEngagementCommissionSplit",
                newName: "REMSEngagementCommissionSplit",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSEngagementAuditDetail",
                newName: "REMSEngagementAuditDetail",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSEngagementApprover",
                newName: "REMSEngagementApprover",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSEngagement",
                newName: "REMSEngagement",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSClient",
                newName: "REMSClient",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSApprovalTask",
                newName: "REMSApprovalTask",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSApprovalRound",
                newName: "REMSApprovalRound",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSApprovalChecklistItem",
                newName: "REMSApprovalChecklistItem",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSAdditionalIndividual",
                newName: "REMSAdditionalIndividual",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSAdditionalEntity",
                newName: "REMSAdditionalEntity",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMS",
                newName: "REMS",
                newSchema: "legacy");

            migrationBuilder.RenameTable(
                name: "REMSDelegation",
                newName: "REMS_Delegation");

            migrationBuilder.RenameColumn(
                name: "CanSend",
                table: "REMS_Delegation",
                newName: "IsSendAllowed");

            migrationBuilder.RenameColumn(
                name: "CanPrepare",
                table: "REMS_Delegation",
                newName: "IsPrepareAllowed");

            migrationBuilder.RenameIndex(
                name: "IX_REMSDelegation_TenantId_PrincipalUserId_DelegateUserId",
                table: "REMS_Delegation",
                newName: "IX_REMS_Delegation_TenantId_PrincipalUserId_DelegateUserId");

            migrationBuilder.RenameIndex(
                name: "IX_REMSDelegation_TenantId_DelegateUserId",
                table: "REMS_Delegation",
                newName: "IX_REMS_Delegation_TenantId_DelegateUserId");

            migrationBuilder.RenameIndex(
                name: "IX_REMSDelegation_TenantId",
                table: "REMS_Delegation",
                newName: "IX_REMS_Delegation_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_REMSDelegation_PrincipalUserId",
                table: "REMS_Delegation",
                newName: "IX_REMS_Delegation_PrincipalUserId");

            migrationBuilder.RenameIndex(
                name: "IX_REMSDelegation_DelegateUserId",
                table: "REMS_Delegation",
                newName: "IX_REMS_Delegation_DelegateUserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_REMS_Delegation",
                table: "REMS_Delegation",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_REMS_Delegation_Tenants_TenantId",
                table: "REMS_Delegation",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_REMS_Delegation_Users_DelegateUserId",
                table: "REMS_Delegation",
                column: "DelegateUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_REMS_Delegation_Users_PrincipalUserId",
                table: "REMS_Delegation",
                column: "PrincipalUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_REMS_Delegation_Tenants_TenantId",
                table: "REMS_Delegation");

            migrationBuilder.DropForeignKey(
                name: "FK_REMS_Delegation_Users_DelegateUserId",
                table: "REMS_Delegation");

            migrationBuilder.DropForeignKey(
                name: "FK_REMS_Delegation_Users_PrincipalUserId",
                table: "REMS_Delegation");

            migrationBuilder.DropPrimaryKey(
                name: "PK_REMS_Delegation",
                table: "REMS_Delegation");

            migrationBuilder.RenameTable(
                name: "REMSSendBack",
                schema: "legacy",
                newName: "REMSSendBack");

            migrationBuilder.RenameTable(
                name: "REMSMaconomyCustomerSync",
                schema: "legacy",
                newName: "REMSMaconomyCustomerSync");

            migrationBuilder.RenameTable(
                name: "REMSFormSubmission",
                schema: "legacy",
                newName: "REMSFormSubmission");

            migrationBuilder.RenameTable(
                name: "REMSFormEmailEvent",
                schema: "legacy",
                newName: "REMSFormEmailEvent");

            migrationBuilder.RenameTable(
                name: "REMSFormDraft",
                schema: "legacy",
                newName: "REMSFormDraft");

            migrationBuilder.RenameTable(
                name: "REMSForm",
                schema: "legacy",
                newName: "REMSForm");

            migrationBuilder.RenameTable(
                name: "REMSFiles",
                schema: "legacy",
                newName: "REMSFiles");

            migrationBuilder.RenameTable(
                name: "REMSEntityContact",
                schema: "legacy",
                newName: "REMSEntityContact");

            migrationBuilder.RenameTable(
                name: "REMSEntityAddress",
                schema: "legacy",
                newName: "REMSEntityAddress");

            migrationBuilder.RenameTable(
                name: "REMSEntity",
                schema: "legacy",
                newName: "REMSEntity");

            migrationBuilder.RenameTable(
                name: "REMSEngagementTaxForm",
                schema: "legacy",
                newName: "REMSEngagementTaxForm");

            migrationBuilder.RenameTable(
                name: "REMSEngagementTaxDetail",
                schema: "legacy",
                newName: "REMSEngagementTaxDetail");

            migrationBuilder.RenameTable(
                name: "REMSEngagementPersonnelRate",
                schema: "legacy",
                newName: "REMSEngagementPersonnelRate");

            migrationBuilder.RenameTable(
                name: "REMSEngagementMarketingMethod",
                schema: "legacy",
                newName: "REMSEngagementMarketingMethod");

            migrationBuilder.RenameTable(
                name: "REMSEngagementGovernmentDetail",
                schema: "legacy",
                newName: "REMSEngagementGovernmentDetail");

            migrationBuilder.RenameTable(
                name: "REMSEngagementCommissionSplit",
                schema: "legacy",
                newName: "REMSEngagementCommissionSplit");

            migrationBuilder.RenameTable(
                name: "REMSEngagementAuditDetail",
                schema: "legacy",
                newName: "REMSEngagementAuditDetail");

            migrationBuilder.RenameTable(
                name: "REMSEngagementApprover",
                schema: "legacy",
                newName: "REMSEngagementApprover");

            migrationBuilder.RenameTable(
                name: "REMSEngagement",
                schema: "legacy",
                newName: "REMSEngagement");

            migrationBuilder.RenameTable(
                name: "REMSClient",
                schema: "legacy",
                newName: "REMSClient");

            migrationBuilder.RenameTable(
                name: "REMSApprovalTask",
                schema: "legacy",
                newName: "REMSApprovalTask");

            migrationBuilder.RenameTable(
                name: "REMSApprovalRound",
                schema: "legacy",
                newName: "REMSApprovalRound");

            migrationBuilder.RenameTable(
                name: "REMSApprovalChecklistItem",
                schema: "legacy",
                newName: "REMSApprovalChecklistItem");

            migrationBuilder.RenameTable(
                name: "REMSAdditionalIndividual",
                schema: "legacy",
                newName: "REMSAdditionalIndividual");

            migrationBuilder.RenameTable(
                name: "REMSAdditionalEntity",
                schema: "legacy",
                newName: "REMSAdditionalEntity");

            migrationBuilder.RenameTable(
                name: "REMS",
                schema: "legacy",
                newName: "REMS");

            migrationBuilder.RenameTable(
                name: "REMS_Delegation",
                newName: "REMSDelegation");

            migrationBuilder.RenameColumn(
                name: "IsSendAllowed",
                table: "REMSDelegation",
                newName: "CanSend");

            migrationBuilder.RenameColumn(
                name: "IsPrepareAllowed",
                table: "REMSDelegation",
                newName: "CanPrepare");

            migrationBuilder.RenameIndex(
                name: "IX_REMS_Delegation_TenantId_PrincipalUserId_DelegateUserId",
                table: "REMSDelegation",
                newName: "IX_REMSDelegation_TenantId_PrincipalUserId_DelegateUserId");

            migrationBuilder.RenameIndex(
                name: "IX_REMS_Delegation_TenantId_DelegateUserId",
                table: "REMSDelegation",
                newName: "IX_REMSDelegation_TenantId_DelegateUserId");

            migrationBuilder.RenameIndex(
                name: "IX_REMS_Delegation_TenantId",
                table: "REMSDelegation",
                newName: "IX_REMSDelegation_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_REMS_Delegation_PrincipalUserId",
                table: "REMSDelegation",
                newName: "IX_REMSDelegation_PrincipalUserId");

            migrationBuilder.RenameIndex(
                name: "IX_REMS_Delegation_DelegateUserId",
                table: "REMSDelegation",
                newName: "IX_REMSDelegation_DelegateUserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_REMSDelegation",
                table: "REMSDelegation",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_REMSDelegation_Tenants_TenantId",
                table: "REMSDelegation",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_REMSDelegation_Users_DelegateUserId",
                table: "REMSDelegation",
                column: "DelegateUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_REMSDelegation_Users_PrincipalUserId",
                table: "REMSDelegation",
                column: "PrincipalUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
