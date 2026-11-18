/* Set here, not left to the caller: a procedure keeps these from CREATE time, and sqlcmd connects with
   QUOTED_IDENTIFIER OFF, under which every DELETE on a table with a filtered index fails (Msg 1934). */
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* ===============================================================================================
   usp_PurgeTenantAllData — empties ONE tenant of its working data, keeping its admins, for resetting a
   DEVELOPMENT or TEST database. One call, one transaction, in the order the records depend on:

     1. usp_RemsPurgeAllData   requests, clients, delegations, REMS settings, the people and media
                               they minted, the client-form email log
     2. usp_PurgeTenantUsers   every user of the tenant except its admins
     3. what is left of the tenant's role, department and group assignments (users who also belong
        to another tenant keep their account and lose only this tenant)
     4. the tenant's people records that belong to no user and no client (left by users removed
        earlier, or added on the People page)

     5. the sign-in tokens of the tenant's remaining users, who simply sign in again

   @IncludeSharedLogs (default OFF) also empties what is NOT kept per tenant, so it reaches every
   tenant: the application log (dbo.Logs), every sign-in token (everyone is signed out), and
   Hangfire's job history and queue. The recurring job definitions stay.

   Both procedures above must be installed. A tenant is REQUIRED: there is no "every tenant" here.

   KEPT: anyone holding SuperAdmin or TenantAdmin, with their roles, and the tenant's configuration —
   the tenant row, option sets, roles, user groups, branding, email templates, SMTP and Maconomy
   connections.

   SAFE BY DEFAULT: @WhatIf = 1 previews both steps and changes nothing; a real run needs
   @Confirm = 'DELETE TENANT DATA'.

     EXEC dbo.usp_PurgeTenantAllData @TenantId = '00000000-0000-0000-0000-000000000000';

     EXEC dbo.usp_PurgeTenantAllData
          @TenantId = '00000000-0000-0000-0000-000000000000',
          @WhatIf   = 0,
          @Confirm  = 'DELETE TENANT DATA';
   =============================================================================================== */
CREATE OR ALTER PROCEDURE dbo.usp_PurgeTenantAllData
    @TenantId uniqueidentifier,
    @WhatIf   bit          = 1,
    @Confirm  nvarchar(50) = NULL,
    @IncludeSharedLogs bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;   -- any error aborts the batch, so a half-purge cannot commit

    IF @TenantId IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.Tenants t WHERE t.Id = @TenantId)
    BEGIN
        RAISERROR(N'Refusing to run: @TenantId must name an existing tenant.', 16, 1);
        RETURN;
    END;
    IF @WhatIf = 0 AND (@Confirm IS NULL OR @Confirm <> N'DELETE TENANT DATA')
    BEGIN
        RAISERROR(N'Refusing to delete: pass @Confirm = ''DELETE TENANT DATA'' to run for real, or leave @WhatIf = 1 to preview.', 16, 1);
        RETURN;
    END;

    IF @WhatIf = 1
    BEGIN
        EXEC dbo.usp_RemsPurgeAllData
             @TenantId = @TenantId, @IncludeSettings = 1, @IncludeClientPersons = 1, @IncludeMedia = 1;
        -- Counted before the requests go, so this is the number the real run removes.
        EXEC dbo.usp_PurgeTenantUsers @TenantId = @TenantId, @KeepAdmins = 1;
        IF @IncludeSharedLogs = 1
            SELECT (SELECT COUNT(*) FROM dbo.Logs) AS Logs,
                   (SELECT COUNT(*) FROM dbo.RefreshTokens) AS RefreshTokens,
                   CASE WHEN OBJECT_ID(N'HangFire.Job') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM HangFire.Job) END AS HangfireJobs,
                   'WHAT-IF (shared, every tenant)' AS Result;
        RETURN;
    END;

    BEGIN TRAN;

    EXEC dbo.usp_RemsPurgeAllData
         @TenantId = @TenantId, @WhatIf = 0, @Confirm = N'DELETE REMS DATA',
         @IncludeSettings = 1, @IncludeClientPersons = 1, @IncludeMedia = 1;

    EXEC dbo.usp_PurgeTenantUsers
         @TenantId = @TenantId, @WhatIf = 0, @Confirm = N'DELETE USERS', @KeepAdmins = 1;

    -- Users kept because they belong to another tenant too: only this tenant is taken from them.
    -- The admins keep everything they hold here.
    DECLARE @Super TABLE (UserId uniqueidentifier PRIMARY KEY);
    INSERT @Super
    SELECT DISTINCT a.UserId
    FROM dbo.UserTenantRoles a JOIN dbo.Roles r ON r.Id = a.RoleId
    WHERE r.Name IN (N'SuperAdmin', N'TenantAdmin');

    DELETE g FROM dbo.UserGroupMembers g
    WHERE g.TenantId = @TenantId AND g.UserId NOT IN (SELECT UserId FROM @Super);
    DELETE d FROM dbo.UserDepartments d
    WHERE d.TenantId = @TenantId AND d.UserId NOT IN (SELECT UserId FROM @Super);
    DELETE a FROM dbo.UserTenantRoles a
    WHERE a.TenantId = @TenantId AND a.UserId NOT IN (SELECT UserId FROM @Super);

    -- People with no account and no client: nothing in the tenant points at them any more.
    DELETE p FROM dbo.Persons p
    WHERE p.TenantId = @TenantId
      AND NOT EXISTS (SELECT 1 FROM dbo.Users u WHERE u.PersonId = p.Id OR u.Id = p.UserId)
      AND NOT EXISTS (SELECT 1 FROM dbo.Client c WHERE c.PersonId = p.Id)
      AND NOT EXISTS (SELECT 1 FROM dbo.Client_Contact c WHERE c.PersonId = p.Id);

    -- The users who stay sign in again.
    DELETE x FROM dbo.RefreshTokens x
    WHERE EXISTS (SELECT 1 FROM dbo.UserTenantRoles a WHERE a.UserId = x.UserId AND a.TenantId = @TenantId);

    -- Not kept per tenant: asked for by its own switch, because it reaches everybody.
    IF @IncludeSharedLogs = 1
    BEGIN
        DELETE FROM dbo.Logs;
        DELETE FROM dbo.RefreshTokens;
        IF OBJECT_ID(N'HangFire.Job') IS NOT NULL
        BEGIN
            DELETE FROM HangFire.JobQueue;
            DELETE FROM HangFire.Job;              -- State and JobParameter go with it (cascade)
            DELETE FROM HangFire.List;
            DELETE FROM HangFire.Counter;
            DELETE FROM HangFire.AggregatedCounter;
            -- Hash and Set hold the recurring job definitions; Server and Schema are Hangfire's own.
        END;
    END;

    -- Either step can refuse without throwing; nothing is kept unless both did their work.
    IF EXISTS (SELECT 1 FROM dbo.REMS r WHERE r.TenantId = @TenantId)
       OR EXISTS (SELECT 1 FROM dbo.UserTenantRoles a
                  WHERE a.TenantId = @TenantId AND a.UserId NOT IN (SELECT UserId FROM @Super))
    BEGIN
        ROLLBACK;
        RAISERROR(N'The tenant was not emptied; nothing was deleted. Read the messages above.', 16, 1);
        RETURN;
    END;

    COMMIT;

    SELECT (SELECT COUNT(*) FROM dbo.REMS r WHERE r.TenantId = @TenantId)   AS RequestsLeft,
           (SELECT COUNT(DISTINCT a.UserId) FROM dbo.UserTenantRoles a WHERE a.TenantId = @TenantId) AS UsersLeft,
           'DELETED' AS Result;
END;
GO
