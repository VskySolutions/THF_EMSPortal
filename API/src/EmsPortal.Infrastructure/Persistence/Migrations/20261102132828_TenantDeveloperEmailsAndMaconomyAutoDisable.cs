using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantDeveloperEmailsAndMaconomyAutoDisable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeveloperEmails",
                table: "Tenants",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AutoDisabledOnUtc",
                table: "MaconomyConnections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailureCount",
                table: "MaconomyConnections",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeveloperEmails",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AutoDisabledOnUtc",
                table: "MaconomyConnections");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailureCount",
                table: "MaconomyConnections");
        }
    }
}
