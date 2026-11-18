/* Set here, not left to the caller: a procedure keeps these from CREATE time, and sqlcmd connects with
   QUOTED_IDENTIFIER OFF, under which every DELETE on a table with a filtered index fails (Msg 1934). */
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* ===============================================================================================
   usp_PurgeTenantUsers — removes a tenant's users, for resetting a DEVELOPMENT or TEST database.
   Run usp_RemsPurgeAllData for the same tenant FIRST: it refuses while requests still name the users.

   SAFE BY DEFAULT, like usp_RemsPurgeAllData: @WhatIf = 1 unless you say otherwise, and a real run
   additionally needs @Confirm = 'DELETE USERS'.

     -- see how many would go
     EXEC dbo.usp_PurgeTenantUsers @TenantId = '00000000-0000-0000-0000-000000000000';

     -- actually remove them
     EXEC dbo.usp_PurgeTenantUsers
          @TenantId = '00000000-0000-0000-0000-000000000000',
          @WhatIf   = 0,
          @Confirm  = 'DELETE USERS';

   @TenantId    NULL takes the users of EVERY tenant. With one, a user who also holds a role in
                another tenant is left alone.
   @KeepAdmins  (default ON) Anyone holding SuperAdmin or TenantAdmin stays, so the tenant can still
                be signed in to. Off removes them too — SuperAdmin holders are kept regardless.

   Hard deletes: the users, their person records (unless a client or client contact still points at
   one), roles, departments, group memberships, tokens, notifications and the tenant's import log.
   =============================================================================================== */
CREATE OR ALTER PROCEDURE dbo.usp_PurgeTenantUsers
    @TenantId   uniqueidentifier = NULL,
    @WhatIf     bit              = 1,
    @Confirm    nvarchar(50)     = NULL,
    @KeepAdmins bit              = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;   -- any error aborts the batch, so a half-purge cannot commit

    IF @WhatIf = 0 AND (@Confirm IS NULL OR @Confirm <> N'DELETE USERS')
    BEGIN
        RAISERROR(N'Refusing to delete: pass @Confirm = ''DELETE USERS'' to run for real, or leave @WhatIf = 1 to preview.', 16, 1);
        RETURN;
    END;

    CREATE TABLE #U (Id uniqueidentifier PRIMARY KEY, PersonId uniqueidentifier NULL);
    INSERT #U (Id, PersonId)
    SELECT u.Id, u.PersonId
    FROM dbo.Users u
    WHERE (@TenantId IS NULL
           OR (EXISTS (SELECT 1 FROM dbo.UserTenantRoles a WHERE a.UserId = u.Id AND a.TenantId = @TenantId)
               AND NOT EXISTS (SELECT 1 FROM dbo.UserTenantRoles a WHERE a.UserId = u.Id AND a.TenantId <> @TenantId)))
      AND NOT EXISTS (SELECT 1 FROM dbo.UserTenantRoles a JOIN dbo.Roles r ON r.Id = a.RoleId
                      WHERE a.UserId = u.Id
                        AND (r.Name = N'SuperAdmin' OR (@KeepAdmins = 1 AND r.Name = N'TenantAdmin')));

    DECLARE @InScope int = (SELECT COUNT(*) FROM #U);
    DECLARE @Kept int = (SELECT COUNT(DISTINCT a.UserId) FROM dbo.UserTenantRoles a
                         WHERE (@TenantId IS NULL OR a.TenantId = @TenantId)
                           AND a.UserId NOT IN (SELECT Id FROM #U));

    IF @WhatIf = 1
    BEGIN
        SELECT @InScope AS UsersToDelete, @Kept AS UsersKept, 'WHAT-IF (nothing deleted)' AS Result;
        RETURN;
    END;

    IF EXISTS (SELECT 1 FROM dbo.REMS r JOIN #U u
               ON u.Id IN (r.CSEId, r.CreatedById, r.EngagementExecutiveId, r.BillingManagerId, r.DepartmentDirectorId))
    BEGIN
        RAISERROR(N'Requests still name these users. Run usp_RemsPurgeAllData for the tenant first.', 16, 1);
        RETURN;
    END;

    BEGIN TRAN;

    -- What a user leaves behind with no cascade of its own.
    DELETE d FROM dbo.REMS_DepartmentDirector d JOIN #U u ON u.Id = d.DirectorUserId;
    DELETE x FROM dbo.REMS_Delegation x JOIN #U u ON u.Id IN (x.PrincipalUserId, x.DelegateUserId);
    UPDATE c SET IntakeSubmittedByUserId = NULL FROM dbo.Client c JOIN #U u ON u.Id = c.IntakeSubmittedByUserId;
    DELETE x FROM dbo.UserImportRows x WHERE @TenantId IS NULL OR x.TenantId = @TenantId;
    DELETE x FROM dbo.UserImports x    WHERE @TenantId IS NULL OR x.TenantId = @TenantId;
    DELETE x FROM dbo.RefreshTokens x           JOIN #U u ON u.Id = x.UserId;
    DELETE x FROM dbo.Notifications x           JOIN #U u ON u.Id = x.UserId;
    DELETE x FROM dbo.NotificationPreferences x JOIN #U u ON u.Id = x.UserId;
    DELETE x FROM dbo.Reminders x               JOIN #U u ON u.Id = x.UserId;
    DELETE x FROM dbo.Pins x                    JOIN #U u ON u.Id = x.UserId;
    DELETE x FROM dbo.ColourCodes x             JOIN #U u ON u.Id = x.UserId;
    DELETE x FROM dbo.DashboardLayouts x        JOIN #U u ON u.Id = x.UserId;
    DELETE x FROM dbo.StickyNoteDismissals x    JOIN #U u ON u.Id = x.UserId;
    DELETE x FROM dbo.UserStickyNoteStates x    JOIN #U u ON u.Id = x.UserId;

    -- Roles, departments, group memberships and reset tokens go with the user (cascade).
    DELETE x FROM dbo.Users x JOIN #U u ON u.Id = x.Id;
    DECLARE @Users int = @@ROWCOUNT;

    -- Their person records, unless a client or client contact still points at one.
    DELETE p FROM dbo.Persons p JOIN #U u ON u.PersonId = p.Id
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Client c WHERE c.PersonId = p.Id)
      AND NOT EXISTS (SELECT 1 FROM dbo.Client_Contact c WHERE c.PersonId = p.Id);
    DECLARE @Persons int = @@ROWCOUNT;

    COMMIT;

    SELECT @Users AS UsersDeleted, @Persons AS PersonsDeleted, @Kept AS UsersKept, 'DELETED' AS Result;
END;
GO
