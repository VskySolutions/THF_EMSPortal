/* A procedure captures these at CREATE time and keeps them for every later execution, so they have
   to be set HERE rather than left to whoever runs the file. SSMS connects with both ON and the
   script worked by luck; sqlcmd connects with QUOTED_IDENTIFIER OFF, which compiles a procedure
   whose every DELETE then fails with Msg 1934 — most of these tables carry a filtered index
   (WHERE [Deleted] = 0), and SQL Server refuses to touch one under the wrong SET options. */
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* ===============================================================================================
   usp_RemsPurgeAllData — empties REMS and everything hanging off it, on the v2 data model: the
   REMS request and its REMS_* children, and the Client master with its Client_* rows.

   Intended for resetting a DEVELOPMENT or TEST database back to "no referrals have ever been
   raised". It hard-deletes; the Deleted flag those tables carry is a soft-delete for the
   application, and a purge that respected it would leave every row exactly where it was.

   SAFE BY DEFAULT. @WhatIf = 1 unless you say otherwise, and a real run additionally needs
   @Confirm = 'DELETE REMS DATA'. Both guards exist because the first argument most people reach
   for is the tenant id, and getting that wrong on a shared database is not recoverable from here.

     -- see what would go, tenant by tenant
     EXEC dbo.usp_RemsPurgeAllData @TenantId = '00000000-0000-0000-0000-000000000000';

     -- actually clear that tenant
     EXEC dbo.usp_RemsPurgeAllData
          @TenantId = '00000000-0000-0000-0000-000000000000',
          @WhatIf   = 0,
          @Confirm  = 'DELETE REMS DATA';

   @TenantId  NULL clears EVERY tenant. Pass one to keep the blast radius to a single firm.
   @WhatIf    1 (default) reports the row counts and changes nothing.
   @Confirm   must be 'DELETE REMS DATA' when @WhatIf = 0.

   What goes with the requests, and the switches:
   @IncludeClients       (default ON)  The Client master: every client in scope, with its addresses,
                                       contacts, contracts and relationships. On v2 a client row
                                       exists only because a request raised it — at intake, by a
                                       Maconomy pick, or as a related party — so it is request data.
                                       Off keeps the clients and their rows; the requests still go.
   @IncludeDelegations   (default ON)  REMS_Delegation — who may act for whom. Request-scoped
                                       working state, so it goes with the requests by default.
   @IncludeSettings      REMS_Settings + REMS_DepartmentDirector + REMS_ChecklistTemplate. This is
                         CONFIGURATION — the department→director map, the approvers' checklists —
                         not request data. Off, because clearing it means re-entering the firm's
                         setup by hand.
   @IncludeClientPersons Persons REMS minted: an individual client's own person record and the people
                         behind client contacts (SourceEntityType Rems or Client). Off, because a
                         person is a record the platform holds in its own right and other modules may
                         point at. Only ever touches persons with no user account and no client row
                         left pointing at them.
   @IncludeMedia         The Media rows the deleted REMS_File rows pointed at — request attachments,
                         signed client acceptance forms, purchase orders. Off: it removes the DB rows
                         only — the stored files are not this script's to delete, and orphaned Media
                         is harmless.
   @ResetNumbering       (default ON)  REMS-n and CL-n come from sequences (seq_RemsNumber,
                                       seq_ClientNumber), which do not move when rows go. When the
                                       WHOLE database is purged (@TenantId NULL) they restart at 1,
                                       the client one only when the clients went too. For one tenant
                                       they are left alone: the sequences are global, and the other
                                       tenants' numbers are still in use.
   =============================================================================================== */
CREATE OR ALTER PROCEDURE dbo.usp_RemsPurgeAllData
    @TenantId             uniqueidentifier = NULL,
    @WhatIf               bit              = 1,
    @Confirm              nvarchar(50)     = NULL,
    @IncludeClients       bit              = 1,
    @IncludeDelegations   bit              = 1,
    @IncludeSettings      bit              = 0,
    @IncludeClientPersons bit              = 0,
    @IncludeMedia         bit              = 0,
    @ResetNumbering       bit              = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;   -- any error aborts the batch, so a half-purge cannot commit

    IF @WhatIf = 0 AND (@Confirm IS NULL OR @Confirm <> N'DELETE REMS DATA')
    BEGIN
        RAISERROR(N'Refusing to delete: pass @Confirm = ''DELETE REMS DATA'' to run for real, or leave @WhatIf = 1 to preview.', 16, 1);
        RETURN;
    END;

    /* -- The Universal Features keys. Conversation messages, tags, attachments, activity, reminders,
          notifications, pins, colour codes, checklists and field logs all attach through
          (EntityType, EntityId), so they are found by number rather than by foreign key: Rems for a
          request, Client for a client. Persons carry the same numbers in SourceEntityType to say
          what minted them. */
    DECLARE @EntityTypeRems   int = 6;
    DECLARE @EntityTypeClient int = 16;

    -- ---------------------------------------------------------------------------------------
    -- 1. Collect the ids in scope. Every later statement joins one of these rather than
    --    re-deriving the scope, so the tenant filter is applied exactly once and cannot drift
    --    between statements as rows disappear underneath them.
    -- ---------------------------------------------------------------------------------------
    CREATE TABLE #Rems       (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Task       (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Client     (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Address    (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Media      (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Person     (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #PersonAddr (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Entity     (EntityType int NOT NULL, EntityId uniqueidentifier NOT NULL, PRIMARY KEY (EntityType, EntityId));
    CREATE TABLE #Message    (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Checklist  (Id uniqueidentifier PRIMARY KEY);

    INSERT INTO #Rems (Id)
    SELECT r.Id FROM dbo.REMS r
    WHERE @TenantId IS NULL OR r.TenantId = @TenantId;

    INSERT INTO #Task (Id)
    SELECT t.Id FROM dbo.REMS_ApprovalTask t JOIN #Rems r ON r.Id = t.REMSId;

    IF @IncludeClients = 1
    BEGIN
        INSERT INTO #Client (Id)
        SELECT c.Id FROM dbo.Client c
        WHERE @TenantId IS NULL OR c.TenantId = @TenantId;

        -- A client address owns its Address row; nothing else points at it, so it goes when it does.
        INSERT INTO #Address (Id)
        SELECT DISTINCT a.AddressId FROM dbo.Client_Address a
        JOIN #Client c ON c.Id = a.ClientId
        WHERE a.AddressId IS NOT NULL;
    END;

    IF @IncludeMedia = 1
    BEGIN
        -- Every kind of file a request holds: attachment, client acceptance form, purchase order.
        INSERT INTO #Media (Id)
        SELECT DISTINCT f.MediaId FROM dbo.REMS_File f JOIN #Rems r ON r.Id = f.REMSId
        WHERE f.MediaId IS NOT NULL;
    END;

    IF @IncludeClientPersons = 1
    BEGIN
        /* The person behind an individual client and the people behind client contacts — but never
           somebody who has since become a user, and never one a client outside the scope still points
           at. A person REMS created is REMS's to remove; a person who has grown into anything else is
           not. */
        INSERT INTO #Person (Id)
        SELECT p.Id
        FROM dbo.Persons p
        WHERE (@TenantId IS NULL OR p.TenantId = @TenantId)
          AND p.SourceEntityType IN (@EntityTypeRems, @EntityTypeClient)
          AND p.UserId IS NULL
          AND NOT EXISTS (SELECT 1 FROM dbo.Users u WHERE u.PersonId = p.Id)
          AND NOT EXISTS (SELECT 1 FROM dbo.Client c2
                          WHERE c2.PersonId = p.Id AND c2.Id NOT IN (SELECT Id FROM #Client))
          AND NOT EXISTS (SELECT 1 FROM dbo.Client_Contact k2
                          WHERE k2.PersonId = p.Id AND k2.ClientId NOT IN (SELECT Id FROM #Client));

        /* Their addresses, noted NOW: Persons is the child of Addresses, so the address cannot go
           until the person does — and by then there is nothing left to find it by. */
        INSERT INTO #PersonAddr (Id)
        SELECT DISTINCT p.AddressId FROM dbo.Persons p
        JOIN #Person t ON t.Id = p.Id
        WHERE p.AddressId IS NOT NULL;
    END;

    -- Universal Features attached to a request or a client, found through the shared key.
    INSERT INTO #Entity (EntityType, EntityId) SELECT @EntityTypeRems,   Id FROM #Rems;
    INSERT INTO #Entity (EntityType, EntityId) SELECT @EntityTypeClient, Id FROM #Client;

    INSERT INTO #Message (Id)
    SELECT n.Id FROM dbo.ConversationMessages n JOIN #Entity e ON e.EntityType = n.EntityType AND e.EntityId = n.EntityId;

    INSERT INTO #Checklist (Id)
    SELECT c.Id FROM dbo.Checklists c JOIN #Entity e ON e.EntityType = c.EntityType AND e.EntityId = c.EntityId;

    -- ---------------------------------------------------------------------------------------
    -- 2. Count everything BEFORE deleting, so the preview and the receipt are the same report.
    -- ---------------------------------------------------------------------------------------
    DECLARE @Report TABLE (Seq int IDENTITY(1, 1), TableName sysname, Rows int);

    INSERT INTO @Report (TableName, Rows) VALUES
        ('REMS',                            (SELECT COUNT(*) FROM #Rems)),
        ('REMS_ApprovalTask',               (SELECT COUNT(*) FROM #Task)),
        ('REMS_ApprovalChecklistItem',      (SELECT COUNT(*) FROM dbo.REMS_ApprovalChecklistItem i JOIN #Task t ON t.Id = i.TaskId)),
        ('REMS_Approver',                   (SELECT COUNT(*) FROM dbo.REMS_Approver        x JOIN #Rems r ON r.Id = x.REMSId)),
        ('REMS_CommissionSplit',            (SELECT COUNT(*) FROM dbo.REMS_CommissionSplit x JOIN #Rems r ON r.Id = x.REMSId)),
        ('REMS_EmailEvent',                 (SELECT COUNT(*) FROM dbo.REMS_EmailEvent      x JOIN #Rems r ON r.Id = x.REMSId)),
        ('REMS_File',                       (SELECT COUNT(*) FROM dbo.REMS_File            x JOIN #Rems r ON r.Id = x.REMSId)),
        ('REMS_MarketingMethod',            (SELECT COUNT(*) FROM dbo.REMS_MarketingMethod x JOIN #Rems r ON r.Id = x.REMSId)),
        ('REMS_PersonnelRate',              (SELECT COUNT(*) FROM dbo.REMS_PersonnelRate   x JOIN #Rems r ON r.Id = x.REMSId)),
        ('REMS_StatusHistory',              (SELECT COUNT(*) FROM dbo.REMS_StatusHistory   x JOIN #Rems r ON r.Id = x.REMSId)),
        ('REMS_TaxDueDate',                 (SELECT COUNT(*) FROM dbo.REMS_TaxDueDate      x JOIN #Rems r ON r.Id = x.REMSId)),
        ('REMS_TaxForm',                    (SELECT COUNT(*) FROM dbo.REMS_TaxForm         x JOIN #Rems r ON r.Id = x.REMSId)),
        ('Client',                          (SELECT COUNT(*) FROM #Client)),
        ('Client_Relationship',             (SELECT COUNT(*) FROM dbo.Client_Relationship x
                                             WHERE x.ClientId IN (SELECT Id FROM #Client) OR x.RelatedClientId IN (SELECT Id FROM #Client))),
        ('Client_Contact',                  (SELECT COUNT(*) FROM dbo.Client_Contact  x JOIN #Client c ON c.Id = x.ClientId)),
        ('Client_Address',                  (SELECT COUNT(*) FROM dbo.Client_Address  x JOIN #Client c ON c.Id = x.ClientId)),
        ('Client_Contract',                 (SELECT COUNT(*) FROM dbo.Client_Contract x JOIN #Client c ON c.Id = x.ClientId)),
        ('Addresses (client)',              (SELECT COUNT(*) FROM #Address)),
        ('REMS_Delegation',                 CASE WHEN @IncludeDelegations = 1
                                                 THEN (SELECT COUNT(*) FROM dbo.REMS_Delegation d WHERE @TenantId IS NULL OR d.TenantId = @TenantId)
                                                 ELSE 0 END),
        ('REMS_Settings',                   CASE WHEN @IncludeSettings = 1
                                                 THEN (SELECT COUNT(*) FROM dbo.REMS_Settings s WHERE @TenantId IS NULL OR s.TenantId = @TenantId)
                                                 ELSE 0 END),
        ('REMS_DepartmentDirector',         CASE WHEN @IncludeSettings = 1
                                                 THEN (SELECT COUNT(*) FROM dbo.REMS_DepartmentDirector d
                                                       JOIN dbo.REMS_Settings s ON s.Id = d.SettingsId
                                                       WHERE @TenantId IS NULL OR s.TenantId = @TenantId)
                                                 ELSE 0 END),
        ('REMS_ChecklistTemplate',          CASE WHEN @IncludeSettings = 1
                                                 THEN (SELECT COUNT(*) FROM dbo.REMS_ChecklistTemplate t
                                                       JOIN dbo.REMS_Settings s ON s.Id = t.SettingsId
                                                       WHERE @TenantId IS NULL OR s.TenantId = @TenantId)
                                                 ELSE 0 END),
        ('Persons (REMS-minted)',           (SELECT COUNT(*) FROM #Person)),
        ('Addresses (person)',              (SELECT COUNT(*) FROM #PersonAddr)),
        ('Media',                           (SELECT COUNT(*) FROM #Media)),
        ('UF: ConversationMessages',        (SELECT COUNT(*) FROM #Message)),
        ('UF: ConversationMessageMentions', (SELECT COUNT(*) FROM dbo.ConversationMessageMentions m JOIN #Message n ON n.Id = m.ConversationMessageId)),
        ('UF: EntityTags',                  (SELECT COUNT(*) FROM dbo.EntityTags        x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId)),
        ('UF: Attachments',                 (SELECT COUNT(*) FROM dbo.Attachments       x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId)),
        ('UF: ActivityEvents',              (SELECT COUNT(*) FROM dbo.ActivityEvents    x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId)),
        ('UF: Reminders',                   (SELECT COUNT(*) FROM dbo.Reminders         x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId)),
        ('UF: Notifications',               (SELECT COUNT(*) FROM dbo.Notifications     x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId)),
        ('UF: Pins',                        (SELECT COUNT(*) FROM dbo.Pins              x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId)),
        ('UF: ColourCodes',                 (SELECT COUNT(*) FROM dbo.ColourCodes       x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId)),
        ('UF: Checklists',                  (SELECT COUNT(*) FROM #Checklist)),
        ('UF: ChecklistItems',              (SELECT COUNT(*) FROM dbo.ChecklistItems i JOIN #Checklist c ON c.Id = i.ChecklistId)),
        ('UF: FieldModifiedLogs',           (SELECT COUNT(*) FROM dbo.FieldModifiedLogs x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId)),
        ('Sequences restarted',             CASE WHEN @ResetNumbering = 1 AND @TenantId IS NULL THEN 1 + @IncludeClients ELSE 0 END);

    IF @WhatIf = 1
    BEGIN
        SELECT TableName, Rows, N'WHATIF — nothing deleted' AS Outcome
        FROM @Report ORDER BY Seq;
        RETURN;
    END;

    -- ---------------------------------------------------------------------------------------
    -- 3. Delete, children before parents. One transaction: a purge that stops half way through
    --    leaves a database with clients whose requests are gone, which is worse than a purge
    --    that did not happen.
    -- ---------------------------------------------------------------------------------------
    BEGIN TRY
        BEGIN TRANSACTION;

        -- Universal Features first: they reference the records by number, not by key, so nothing
        -- forces this order — but a failure here should not have already taken the records they
        -- describe.
        DELETE m FROM dbo.ConversationMessageMentions m JOIN #Message n ON n.Id = m.ConversationMessageId;
        DELETE n FROM dbo.ConversationMessages n JOIN #Message t ON t.Id = n.Id;
        DELETE i FROM dbo.ChecklistItems i JOIN #Checklist c ON c.Id = i.ChecklistId;
        DELETE c FROM dbo.Checklists c JOIN #Checklist t ON t.Id = c.Id;
        DELETE x FROM dbo.EntityTags        x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId;
        DELETE x FROM dbo.Attachments       x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId;
        DELETE x FROM dbo.ActivityEvents    x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId;
        DELETE x FROM dbo.Reminders         x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId;
        DELETE x FROM dbo.Notifications     x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId;
        DELETE x FROM dbo.Pins              x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId;
        DELETE x FROM dbo.ColourCodes       x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId;
        DELETE x FROM dbo.FieldModifiedLogs x JOIN #Entity e ON e.EntityType = x.EntityType AND e.EntityId = x.EntityId;

        -- Approval: checklist item → task. There is no round table; a round is the tasks sharing a
        -- RoundNumber, so the tasks carry it all.
        DELETE i FROM dbo.REMS_ApprovalChecklistItem i JOIN #Task t ON t.Id = i.TaskId;
        DELETE t FROM dbo.REMS_ApprovalTask          t JOIN #Task x ON x.Id = t.Id;

        -- The request's other children, every one keyed by REMSId, then the request itself.
        DELETE x FROM dbo.REMS_Approver        x JOIN #Rems r ON r.Id = x.REMSId;
        DELETE x FROM dbo.REMS_CommissionSplit x JOIN #Rems r ON r.Id = x.REMSId;
        DELETE x FROM dbo.REMS_EmailEvent      x JOIN #Rems r ON r.Id = x.REMSId;
        DELETE x FROM dbo.REMS_File            x JOIN #Rems r ON r.Id = x.REMSId;
        DELETE x FROM dbo.REMS_MarketingMethod x JOIN #Rems r ON r.Id = x.REMSId;
        DELETE x FROM dbo.REMS_PersonnelRate   x JOIN #Rems r ON r.Id = x.REMSId;
        DELETE x FROM dbo.REMS_StatusHistory   x JOIN #Rems r ON r.Id = x.REMSId;
        DELETE x FROM dbo.REMS_TaxDueDate      x JOIN #Rems r ON r.Id = x.REMSId;
        DELETE x FROM dbo.REMS_TaxForm         x JOIN #Rems r ON r.Id = x.REMSId;
        DELETE x FROM dbo.REMS                 x JOIN #Rems r ON r.Id = x.Id;

        IF @IncludeClients = 1
        BEGIN
            -- Relationships first: they point at clients from both ends. Then the client's rows, the
            -- contracts last among them because the requests that named a contract are already gone.
            DELETE x FROM dbo.Client_Relationship x
            WHERE x.ClientId IN (SELECT Id FROM #Client) OR x.RelatedClientId IN (SELECT Id FROM #Client);
            DELETE x FROM dbo.Client_Contact  x JOIN #Client c ON c.Id = x.ClientId;
            DELETE x FROM dbo.Client_Address  x JOIN #Client c ON c.Id = x.ClientId;
            DELETE x FROM dbo.Client_Contract x JOIN #Client c ON c.Id = x.ClientId;
            DELETE x FROM dbo.Client          x JOIN #Client c ON c.Id = x.Id;

            -- Addresses the client address rows owned, now unreferenced.
            DELETE a FROM dbo.Addresses a JOIN #Address t ON t.Id = a.Id;
        END;

        IF @IncludeDelegations = 1
            DELETE FROM dbo.REMS_Delegation WHERE @TenantId IS NULL OR TenantId = @TenantId;

        IF @IncludeClientPersons = 1
        BEGIN
            -- Person before address: the person holds the reference, so the address cannot go first.
            DELETE p FROM dbo.Persons p JOIN #Person t ON t.Id = p.Id;
            DELETE a FROM dbo.Addresses a JOIN #PersonAddr t ON t.Id = a.Id;
        END;

        IF @IncludeMedia = 1
            DELETE m FROM dbo.Media m JOIN #Media t ON t.Id = m.Id;

        IF @IncludeSettings = 1
        BEGIN
            DELETE t FROM dbo.REMS_ChecklistTemplate t
            JOIN dbo.REMS_Settings s ON s.Id = t.SettingsId
            WHERE @TenantId IS NULL OR s.TenantId = @TenantId;

            DELETE d FROM dbo.REMS_DepartmentDirector d
            JOIN dbo.REMS_Settings s ON s.Id = d.SettingsId
            WHERE @TenantId IS NULL OR s.TenantId = @TenantId;

            DELETE FROM dbo.REMS_Settings WHERE @TenantId IS NULL OR TenantId = @TenantId;
        END;

        -- Only when the whole database went: the sequences are global, so after a one-tenant purge
        -- the other tenants' numbers are still in use.
        IF @ResetNumbering = 1 AND @TenantId IS NULL
        BEGIN
            ALTER SEQUENCE dbo.seq_RemsNumber RESTART WITH 1;
            IF @IncludeClients = 1
                ALTER SEQUENCE dbo.seq_ClientNumber RESTART WITH 1;
        END;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;   -- nothing was deleted; the original error says which table refused and why
    END CATCH;

    SELECT TableName, Rows, N'DELETED' AS Outcome
    FROM @Report ORDER BY Seq;
END;
GO
