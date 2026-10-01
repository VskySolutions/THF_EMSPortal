using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantBrandings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ThemeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LogoMediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LogoDarkMediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LogoMarkMediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FaviconMediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LoginBackgroundMediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBrandings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantBrandings_Media_FaviconMediaId",
                        column: x => x.FaviconMediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantBrandings_Media_LoginBackgroundMediaId",
                        column: x => x.LoginBackgroundMediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantBrandings_Media_LogoDarkMediaId",
                        column: x => x.LogoDarkMediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantBrandings_Media_LogoMarkMediaId",
                        column: x => x.LogoMarkMediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantBrandings_Media_LogoMediaId",
                        column: x => x.LogoMediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantBrandings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantBrandings_FaviconMediaId",
                table: "TenantBrandings",
                column: "FaviconMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantBrandings_LoginBackgroundMediaId",
                table: "TenantBrandings",
                column: "LoginBackgroundMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantBrandings_LogoDarkMediaId",
                table: "TenantBrandings",
                column: "LogoDarkMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantBrandings_LogoMarkMediaId",
                table: "TenantBrandings",
                column: "LogoMarkMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantBrandings_LogoMediaId",
                table: "TenantBrandings",
                column: "LogoMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantBrandings_TenantId",
                table: "TenantBrandings",
                column: "TenantId",
                unique: true,
                filter: "[Deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantBrandings");
        }
    }
}
