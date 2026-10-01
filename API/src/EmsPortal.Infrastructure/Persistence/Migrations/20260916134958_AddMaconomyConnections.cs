using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A tenant's connection to its Maconomy instance: the address, the instance code, the service user,
    /// and — encrypted — its password and the reconnect token the login produces. One live row per tenant.
    /// </summary>
    public partial class AddMaconomyConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaconomyConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    InstanceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EncryptedPassword = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EncryptedReconnectToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReconnectTokenIssuedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastLoginErrorUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ContainerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DefaultLimit = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaconomyConnections", x => x.Id);
                    table.CheckConstraint("CK_MaconomyConnections_DefaultLimit", "[DefaultLimit] >= 1");
                    table.ForeignKey(
                        name: "FK_MaconomyConnections_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaconomyConnections_TenantId",
                table: "MaconomyConnections",
                column: "TenantId",
                unique: true,
                filter: "[Deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaconomyConnections");
        }
    }
}
