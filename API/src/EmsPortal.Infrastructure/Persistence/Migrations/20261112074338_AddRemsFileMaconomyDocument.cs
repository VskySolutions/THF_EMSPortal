using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRemsFileMaconomyDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MaconomyDocumentArchiveNumber",
                table: "REMS_File",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaconomyDocumentKey",
                table: "REMS_File",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaconomyDocumentName",
                table: "REMS_File",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaconomyDocumentSyncError",
                table: "REMS_File",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaconomyDocumentSyncStatus",
                table: "REMS_File",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotSynced");

            migrationBuilder.AddColumn<DateTime>(
                name: "MaconomyDocumentSyncedOnUtc",
                table: "REMS_File",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaconomyDocumentArchiveNumber",
                table: "REMS_File");

            migrationBuilder.DropColumn(
                name: "MaconomyDocumentKey",
                table: "REMS_File");

            migrationBuilder.DropColumn(
                name: "MaconomyDocumentName",
                table: "REMS_File");

            migrationBuilder.DropColumn(
                name: "MaconomyDocumentSyncError",
                table: "REMS_File");

            migrationBuilder.DropColumn(
                name: "MaconomyDocumentSyncStatus",
                table: "REMS_File");

            migrationBuilder.DropColumn(
                name: "MaconomyDocumentSyncedOnUtc",
                table: "REMS_File");
        }
    }
}
