using EmsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The application log says which tenant each entry belongs to. The table is the logger's own, not in
    /// the model, so this is plain SQL; it creates the table where the logger has not yet.
    /// </summary>
    [DbContext(typeof(EmsPortalDbContext))]
    [Migration("20261124090000_AddLogsTenantId")]
    public partial class AddLogsTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Logs', N'U') IS NULL
    CREATE TABLE dbo.Logs (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Logs PRIMARY KEY CLUSTERED,
        Message nvarchar(max) NULL,
        MessageTemplate nvarchar(max) NULL,
        [Level] nvarchar(max) NULL,
        [TimeStamp] datetime NULL,
        Exception nvarchar(max) NULL,
        Properties nvarchar(max) NULL,
        TenantId uniqueidentifier NULL);
ELSE IF COL_LENGTH(N'dbo.Logs', N'TenantId') IS NULL
    ALTER TABLE dbo.Logs ADD TenantId uniqueidentifier NULL;");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Logs_TenantId_TimeStamp' AND object_id = OBJECT_ID(N'dbo.Logs'))
    CREATE INDEX IX_Logs_TenantId_TimeStamp ON dbo.Logs (TenantId, [TimeStamp] DESC);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Logs_TenantId_TimeStamp' AND object_id = OBJECT_ID(N'dbo.Logs'))
    DROP INDEX IX_Logs_TenantId_TimeStamp ON dbo.Logs;
IF COL_LENGTH(N'dbo.Logs', N'TenantId') IS NOT NULL
    ALTER TABLE dbo.Logs DROP COLUMN TenantId;");
        }
    }
}
