using EmsPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmsPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data only. Filing a new client became a right of its own (rems.clients.create) and deleting your own
    /// draft needs rems.requests.delete (THF, 21 Nov 2026). Every custom role, permission group and group
    /// template that could already create requests gets both, so nobody loses what they could do. The
    /// system roles are not touched here: the seeder rewrites their sets at start.
    /// </summary>
    [DbContext(typeof(EmsPortalDbContext))]
    [Migration("20261121100000_GrantNewClientAndDraftDeleteToRequestCreators")]
    public partial class GrantNewClientAndDraftDeleteToRequestCreators : Migration
    {
        private static readonly string[] Keys = { "rems.clients.create", "rems.requests.delete" };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var key in Keys)
            {
                // The lists are JSON arrays of strings; appended as text so no JSON function is needed.
                migrationBuilder.Sql($"""
                    UPDATE [Roles]
                    SET [Permissions] = LEFT(RTRIM([Permissions]), LEN(RTRIM([Permissions])) - 1) + N',"{key}"]',
                        [UpdatedOnUtc] = SYSUTCDATETIME()
                    WHERE [IsSystem] = 0
                      AND RTRIM([Permissions]) LIKE N'%]'
                      AND [Permissions] LIKE N'%"rems.requests.create"%'
                      AND [Permissions] NOT LIKE N'%"{key}"%';

                    UPDATE [Roles]
                    SET [EffectivePermissionsJson] = LEFT(RTRIM([EffectivePermissionsJson]), LEN(RTRIM([EffectivePermissionsJson])) - 1) + N',"{key}"]',
                        [UpdatedOnUtc] = SYSUTCDATETIME()
                    WHERE RTRIM([EffectivePermissionsJson]) LIKE N'%]'
                      AND [EffectivePermissionsJson] LIKE N'%"rems.requests.create"%'
                      AND [EffectivePermissionsJson] NOT LIKE N'%"{key}"%';

                    UPDATE [PermissionGroupTemplates]
                    SET [PermissionKeysJson] = LEFT(RTRIM([PermissionKeysJson]), LEN(RTRIM([PermissionKeysJson])) - 1) + N',"{key}"]',
                        [UpdatedOnUtc] = SYSUTCDATETIME()
                    WHERE RTRIM([PermissionKeysJson]) LIKE N'%]'
                      AND [PermissionKeysJson] LIKE N'%"rems.requests.create"%'
                      AND [PermissionKeysJson] NOT LIKE N'%"{key}"%';

                    INSERT INTO [PermissionGroupPermissions] ([Id], [PermissionGroupId], [PermissionKey])
                    SELECT NEWID(), g.[PermissionGroupId], N'{key}'
                    FROM (SELECT DISTINCT [PermissionGroupId] FROM [PermissionGroupPermissions] WHERE [PermissionKey] = N'rems.requests.create') g
                    WHERE NOT EXISTS (
                        SELECT 1 FROM [PermissionGroupPermissions] x
                        WHERE x.[PermissionGroupId] = g.[PermissionGroupId] AND x.[PermissionKey] = N'{key}');
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the key that did not exist before is taken back; who held delete before is not recorded.
            migrationBuilder.Sql("""
                UPDATE [Roles] SET [Permissions] = REPLACE([Permissions], N',"rems.clients.create"', N'')
                WHERE [Permissions] LIKE N'%,"rems.clients.create"%';

                UPDATE [Roles] SET [EffectivePermissionsJson] = REPLACE([EffectivePermissionsJson], N',"rems.clients.create"', N'')
                WHERE [EffectivePermissionsJson] LIKE N'%,"rems.clients.create"%';

                UPDATE [PermissionGroupTemplates] SET [PermissionKeysJson] = REPLACE([PermissionKeysJson], N',"rems.clients.create"', N'')
                WHERE [PermissionKeysJson] LIKE N'%,"rems.clients.create"%';

                DELETE FROM [PermissionGroupPermissions] WHERE [PermissionKey] = N'rems.clients.create';
                """);
        }
    }
}
