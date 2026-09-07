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
   usp_RemsPurgeAllData — empties REMS and everything hanging off it.

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

   Opt-in extras, all off unless stated:
   @IncludeDelegations   (default ON)  REMSDelegation — who may act for whom. Request-scoped
                                       working state, so it goes with the requests by default.
   @IncludeSettings      RemsSettings + RemsDepartmentDirector. This is CONFIGURATION — the
                         department→director map — not request data. Off, because clearing it means
                         re-entering the firm's setup by hand.
   @IncludeClientPersons Persons REMS minted: the clients it captured at intake, the role contacts
                         from submitted forms, and the other people declared on an individual's
                         return (spouse, children — REMSAdditionalIndividual mints one each). Off,
                         because a client entered once is a record the platform holds in its own
                         right and other modules may point at it. Only ever touches persons with no
                         user account and nothing else referring to them.
   @IncludeMedia         The Media rows the deleted files pointed at — request attachments, signed
                         client acceptance forms, and a government engagement's purchase order. Off:
                         it removes the DB rows only — the stored files are not this script's to
                         delete, and orphaned Media is harmless.

   The REMS number (REMS-1, REMS-2, …) is derived from a live count, not a stored counter, so
   numbering restarts on its own once the rows are gone. Nothing else needs resetting.
   =============================================================================================== */
CREATE OR ALTER PROCEDURE dbo.usp_RemsPurgeAllData
    @TenantId             uniqueidentifier = NULL,
    @WhatIf               bit              = 1,
    @Confirm              nvarchar(50)     = NULL,
    @IncludeDelegations   bit              = 1,
    @IncludeSettings      bit              = 0,
    @IncludeClientPersons bit              = 0,
    @IncludeMedia         bit              = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;   -- any error aborts the batch, so a half-purge cannot commit

    IF @WhatIf = 0 AND (@Confirm IS NULL OR @Confirm <> N'DELETE REMS DATA')
    BEGIN
        RAISERROR(N'Refusing to delete: pass @Confirm = ''DELETE REMS DATA'' to run for real, or leave @WhatIf = 1 to preview.', 16, 1);
        RETURN;
    END;

    /* -- The Universal Features key for a REMS request. Conversation messages, tags, attachments,
          activity, reminders, pins, colour codes and checklists all attach through
          (EntityType, EntityId), so they are found by number rather than by foreign key. */
    DECLARE @EntityTypeRems int = 6;

    /* -- Person provenance: what a person IS (a client) and what created them (a REMS record).
          Both are REMS's to clean up; a colleague entered on the People screen is neither. */
    DECLARE @SourceTypeRems   int = 6;
    DECLARE @SourceTypeClient int = 16;

    -- ---------------------------------------------------------------------------------------
    -- 1. Collect the ids in scope. Every later statement joins one of these rather than
    --    re-deriving the scope, so the tenant filter is applied exactly once and cannot drift
    --    between statements as rows disappear underneath them.
    -- ---------------------------------------------------------------------------------------
    CREATE TABLE #Rems       (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Engagement (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Client     (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Entity     (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Form       (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #TaxDetail  (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Round      (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Task       (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Address    (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Media      (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Person     (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #PersonAddr (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Message    (Id uniqueidentifier PRIMARY KEY);
    CREATE TABLE #Checklist  (Id uniqueidentifier PRIMARY KEY);

    INSERT INTO #Rems (Id)
    SELECT r.Id FROM dbo.REMS r
    WHERE @TenantId IS NULL OR r.TenantId = @TenantId;

    INSERT INTO #Engagement (Id)
    SELECT e.Id FROM dbo.REMSEngagement e JOIN #Rems r ON r.Id = e.REMSId;

    INSERT INTO #Client (Id)
    SELECT c.Id FROM dbo.REMSClient c JOIN #Rems r ON r.Id = c.REMSId;

    INSERT INTO #Entity (Id)
    SELECT e.Id FROM dbo.REMSEntity e JOIN #Client c ON c.Id = e.REMSClientId;

    INSERT INTO #Form (Id)
    SELECT f.Id FROM dbo.REMSForm f JOIN #Rems r ON r.Id = f.REMSId;

    INSERT INTO #TaxDetail (Id)
    SELECT d.Id FROM dbo.REMSEngagementTaxDetail d JOIN #Engagement e ON e.Id = d.REMSEngagementId;

    INSERT INTO #Round (Id)
    SELECT a.Id FROM dbo.REMSApprovalRound a JOIN #Engagement e ON e.Id = a.REMSEngagementId;

    INSERT INTO #Task (Id)
    SELECT t.Id FROM dbo.REMSApprovalTask t JOIN #Round a ON a.Id = t.REMSApprovalRoundId;

    -- An entity address owns its Address row; nothing else points at it, so it goes when it does.
    INSERT INTO #Address (Id)
    SELECT DISTINCT a.AddressId FROM dbo.REMSEntityAddress a
    JOIN #Entity e ON e.Id = a.REMSEntityId
    WHERE a.AddressId IS NOT NULL;

    IF @IncludeMedia = 1
    BEGIN
        INSERT INTO #Media (Id)
        SELECT DISTINCT f.MediaId FROM dbo.REMSFiles f JOIN #Rems r ON r.Id = f.REMSId
        WHERE f.MediaId IS NOT NULL;

        INSERT INTO #Media (Id)
        SELECT DISTINCT d.ClientAcceptanceFormMediaId FROM dbo.REMSEngagementAuditDetail d
        JOIN #Engagement e ON e.Id = d.REMSEngagementId
        WHERE d.ClientAcceptanceFormMediaId IS NOT NULL
          AND d.ClientAcceptanceFormMediaId NOT IN (SELECT Id FROM #Media);

        -- The purchase order a government engagement was raised against.
        INSERT INTO #Media (Id)
        SELECT DISTINCT d.PurchaseOrderMediaId FROM dbo.REMSEngagementGovernmentDetail d
        JOIN #Engagement e ON e.Id = d.REMSEngagementId
        WHERE d.PurchaseOrderMediaId IS NOT NULL
          AND d.PurchaseOrderMediaId NOT IN (SELECT Id FROM #Media);
    END;

    IF @IncludeClientPersons = 1
    BEGIN
        /* Clients captured at intake, role contacts minted from submitted forms, and the other people
           declared on an individual's return — but never somebody who has since become a user, and
           never one another module still points at. A person REMS created is REMS's to remove; a
           person who has grown into anything else is not. */
        INSERT INTO #Person (Id)
        SELECT p.Id
        FROM dbo.Persons p
        WHERE (@TenantId IS NULL OR p.TenantId = @TenantId)
          AND p.SourceEntityType IN (@SourceTypeRems, @SourceTypeClient)
          AND p.UserId IS NULL
          AND NOT EXISTS (SELECT 1 FROM dbo.Users u WHERE u.PersonId = p.Id)
          AND NOT EXISTS (SELECT 1 FROM dbo.REMS r2
                          WHERE r2.ClientPersonId = p.Id AND r2.Id NOT IN (SELECT Id FROM #Rems))
          AND NOT EXISTS (SELECT 1 FROM dbo.REMSEntityContact c2
                          JOIN dbo.REMSEntity e2 ON e2.Id = c2.REMSEntityId
                          WHERE c2.PersonId = p.Id AND e2.Id NOT IN (SELECT Id FROM #Entity))
          /* The third place REMS points at a person, and a required FK — same rule as the two above. */
          AND NOT EXISTS (SELECT 1 FROM dbo.REMSAdditionalIndividual i2
                          WHERE i2.PersonId = p.Id AND i2.REMSId NOT IN (SELECT Id FROM #Rems));

        /* Their addresses, noted NOW: Persons is the child of Addresses, so the address cannot go
           until the person does — and by then there is nothing left to find it by. */
        INSERT INTO #PersonAddr (Id)
        SELECT DISTINCT p.AddressId FROM dbo.Persons p
        JOIN #Person t ON t.Id = p.Id
        WHERE p.AddressId IS NOT NULL;
    END;

    -- Universal Features attached to a REMS request, found through the shared (EntityType, EntityId) key.
    -- The request's Conversation thread. The tables were Notes/NoteMentions until the feature was
    -- renamed (RenameNotesToConversationMessages); the rows are the same ones.
    INSERT INTO #Message (Id)
    SELECT n.Id FROM dbo.ConversationMessages n JOIN #Rems r ON r.Id = n.EntityId
    WHERE n.EntityType = @EntityTypeRems;

    INSERT INTO #Checklist (Id)
    SELECT c.Id FROM dbo.Checklists c JOIN #Rems r ON r.Id = c.EntityId WHERE c.EntityType = @EntityTypeRems;

    -- ---------------------------------------------------------------------------------------
    -- 2. Count everything BEFORE deleting, so the preview and the receipt are the same report.
    -- ---------------------------------------------------------------------------------------
    DECLARE @Report TABLE (Seq int IDENTITY(1, 1), TableName sysname, Rows int);

    INSERT INTO @Report (TableName, Rows) VALUES
        ('REMS',                            (SELECT COUNT(*) FROM #Rems)),
        ('REMSEngagement',                  (SELECT COUNT(*) FROM #Engagement)),
        ('REMSClient',                      (SELECT COUNT(*) FROM #Client)),
        ('REMSEntity',                      (SELECT COUNT(*) FROM #Entity)),
        ('REMSForm',                        (SELECT COUNT(*) FROM #Form)),
        ('REMSApprovalRound',               (SELECT COUNT(*) FROM #Round)),
        ('REMSApprovalTask',                (SELECT COUNT(*) FROM #Task)),
        ('REMSApprovalChecklistItem',       (SELECT COUNT(*) FROM dbo.REMSApprovalChecklistItem i JOIN #Task t ON t.Id = i.REMSApprovalTaskId)),
        ('REMSEngagementApprover',          (SELECT COUNT(*) FROM dbo.REMSEngagementApprover x JOIN #Engagement e ON e.Id = x.REMSEngagementId)),
        ('REMSEngagementCommissionSplit',   (SELECT COUNT(*) FROM dbo.REMSEngagementCommissionSplit x JOIN #Engagement e ON e.Id = x.REMSEngagementId)),
        ('REMSEngagementMarketingMethod',   (SELECT COUNT(*) FROM dbo.REMSEngagementMarketingMethod x JOIN #Engagement e ON e.Id = x.REMSEngagementId)),
        ('REMSEngagementTaxForm',           (SELECT COUNT(*) FROM dbo.REMSEngagementTaxForm x JOIN #TaxDetail d ON d.Id = x.REMSEngagementTaxDetailId)),
        ('REMSEngagementTaxDetail',         (SELECT COUNT(*) FROM #TaxDetail)),
        ('REMSEngagementGovernmentDetail',  (SELECT COUNT(*) FROM dbo.REMSEngagementGovernmentDetail x JOIN #Engagement e ON e.Id = x.REMSEngagementId)),
        ('REMSEngagementAuditDetail',       (SELECT COUNT(*) FROM dbo.REMSEngagementAuditDetail x JOIN #Engagement e ON e.Id = x.REMSEngagementId)),
        ('REMSEntityContact',               (SELECT COUNT(*) FROM dbo.REMSEntityContact x JOIN #Entity e ON e.Id = x.REMSEntityId)),
        ('REMSEntityAddress',               (SELECT COUNT(*) FROM dbo.REMSEntityAddress x JOIN #Entity e ON e.Id = x.REMSEntityId)),
        ('REMSFormEmailEvent',              (SELECT COUNT(*) FROM dbo.REMSFormEmailEvent x JOIN #Form f ON f.Id = x.REMSFormId)),
        ('REMSFormDraft',                   (SELECT COUNT(*) FROM dbo.REMSFormDraft x JOIN #Form f ON f.Id = x.REMSFormId)),
        ('REMSFormSubmission',              (SELECT COUNT(*) FROM dbo.REMSFormSubmission x JOIN #Form f ON f.Id = x.REMSFormId)),
        ('REMSFiles',                       (SELECT COUNT(*) FROM dbo.REMSFiles x JOIN #Rems r ON r.Id = x.REMSId)),
        ('REMSSendBack',                    (SELECT COUNT(*) FROM dbo.REMSSendBack x JOIN #Rems r ON r.Id = x.REMSId)),
        ('REMSAdditionalEntity',            (SELECT COUNT(*) FROM dbo.REMSAdditionalEntity x JOIN #Rems r ON r.Id = x.REMSId)),
        ('REMSAdditionalIndividual',        (SELECT COUNT(*) FROM dbo.REMSAdditionalIndividual x JOIN #Rems r ON r.Id = x.REMSId)),
        ('REMSDelegation',                  CASE WHEN @IncludeDelegations = 1
                                                 THEN (SELECT COUNT(*) FROM dbo.REMSDelegation d WHERE @TenantId IS NULL OR d.TenantId = @TenantId)
                                                 ELSE 0 END),
        ('RemsSettings',                    CASE WHEN @IncludeSettings = 1
                                                 THEN (SELECT COUNT(*) FROM dbo.RemsSettings s WHERE @TenantId IS NULL OR s.TenantId = @TenantId)
                                                 ELSE 0 END),
        ('RemsDepartmentDirector',          CASE WHEN @IncludeSettings = 1
                                                 THEN (SELECT COUNT(*) FROM dbo.RemsDepartmentDirector d
                                                       JOIN dbo.RemsSettings s ON s.Id = d.RemsSettingsId
                                                       WHERE @TenantId IS NULL OR s.TenantId = @TenantId)
                                                 ELSE 0 END),
        ('Addresses (entity)',              (SELECT COUNT(*) FROM #Address)),
        ('Persons (REMS-minted)',           (SELECT COUNT(*) FROM #Person)),
        ('Media',                           (SELECT COUNT(*) FROM #Media)),
        ('UF: ConversationMessages',        (SELECT COUNT(*) FROM #Message)),
        ('UF: ConversationMessageMentions', (SELECT COUNT(*) FROM dbo.ConversationMessageMentions m JOIN #Message n ON n.Id = m.ConversationMessageId)),
        ('UF: EntityTags',                  (SELECT COUNT(*) FROM dbo.EntityTags x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems)),
        ('UF: Attachments',                 (SELECT COUNT(*) FROM dbo.Attachments x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems)),
        ('UF: ActivityEvents',              (SELECT COUNT(*) FROM dbo.ActivityEvents x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems)),
        ('UF: Reminders',                   (SELECT COUNT(*) FROM dbo.Reminders x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems)),
        ('UF: Notifications',               (SELECT COUNT(*) FROM dbo.Notifications x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems)),
        ('UF: Pins',                        (SELECT COUNT(*) FROM dbo.Pins x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems)),
        ('UF: ColourCodes',                 (SELECT COUNT(*) FROM dbo.ColourCodes x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems)),
        ('UF: Checklists',                  (SELECT COUNT(*) FROM #Checklist)),
        ('UF: ChecklistItems',              (SELECT COUNT(*) FROM dbo.ChecklistItems i JOIN #Checklist c ON c.Id = i.ChecklistId)),
        ('UF: FieldModifiedLogs',           (SELECT COUNT(*) FROM dbo.FieldModifiedLogs x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems));

    IF @WhatIf = 1
    BEGIN
        SELECT TableName, Rows, N'WHATIF — nothing deleted' AS Outcome
        FROM @Report ORDER BY Seq;
        RETURN;
    END;

    -- ---------------------------------------------------------------------------------------
    -- 3. Delete, children before parents. One transaction: a purge that stops half way through
    --    leaves a database with engagements whose requests are gone, which is worse than a
    --    purge that did not happen.
    -- ---------------------------------------------------------------------------------------
    BEGIN TRY
        BEGIN TRANSACTION;

        -- Universal Features first: they reference REMS by number, not by key, so nothing forces
        -- this order — but a failure here should not have already taken the records they describe.
        DELETE m FROM dbo.ConversationMessageMentions m JOIN #Message n ON n.Id = m.ConversationMessageId;
        DELETE n FROM dbo.ConversationMessages n JOIN #Message t ON t.Id = n.Id;
        DELETE i FROM dbo.ChecklistItems i JOIN #Checklist c ON c.Id = i.ChecklistId;
        DELETE c FROM dbo.Checklists c JOIN #Checklist t ON t.Id = c.Id;
        DELETE x FROM dbo.EntityTags        x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems;
        DELETE x FROM dbo.Attachments       x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems;
        DELETE x FROM dbo.ActivityEvents    x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems;
        DELETE x FROM dbo.Reminders         x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems;
        DELETE x FROM dbo.Notifications     x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems;
        DELETE x FROM dbo.Pins              x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems;
        DELETE x FROM dbo.ColourCodes       x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems;
        DELETE x FROM dbo.FieldModifiedLogs x JOIN #Rems r ON r.Id = x.EntityId WHERE x.EntityType = @EntityTypeRems;

        -- Approval: checklist item → task → round.
        DELETE i FROM dbo.REMSApprovalChecklistItem i JOIN #Task t ON t.Id = i.REMSApprovalTaskId;
        DELETE t FROM dbo.REMSApprovalTask          t JOIN #Round a ON a.Id = t.REMSApprovalRoundId;
        DELETE a FROM dbo.REMSApprovalRound         a JOIN #Round t ON t.Id = a.Id;

        -- Engagement and its parts. The tax forms hang off the tax detail, not off the engagement.
        DELETE x FROM dbo.REMSEngagementApprover         x JOIN #Engagement e ON e.Id = x.REMSEngagementId;
        DELETE x FROM dbo.REMSEngagementCommissionSplit  x JOIN #Engagement e ON e.Id = x.REMSEngagementId;
        DELETE x FROM dbo.REMSEngagementMarketingMethod  x JOIN #Engagement e ON e.Id = x.REMSEngagementId;
        DELETE x FROM dbo.REMSEngagementTaxForm          x JOIN #TaxDetail  d ON d.Id = x.REMSEngagementTaxDetailId;
        DELETE x FROM dbo.REMSEngagementTaxDetail        x JOIN #TaxDetail  d ON d.Id = x.Id;
        DELETE x FROM dbo.REMSEngagementGovernmentDetail x JOIN #Engagement e ON e.Id = x.REMSEngagementId;
        DELETE x FROM dbo.REMSEngagementAuditDetail      x JOIN #Engagement e ON e.Id = x.REMSEngagementId;
        DELETE x FROM dbo.REMSEngagement                 x JOIN #Engagement e ON e.Id = x.Id;

        -- Spouse, children, anyone else on a return. Ahead of the entity block: each row points at the
        -- request, the entity AND a Person, all three Restrict, so all three refuse to go while it stands.
        DELETE x FROM dbo.REMSAdditionalIndividual x JOIN #Rems r ON r.Id = x.REMSId;

        -- The client and its entities. REMSClient points AT a form submission, so it goes first.
        DELETE x FROM dbo.REMSEntityContact x JOIN #Entity e ON e.Id = x.REMSEntityId;
        DELETE x FROM dbo.REMSEntityAddress x JOIN #Entity e ON e.Id = x.REMSEntityId;
        DELETE x FROM dbo.REMSEntity        x JOIN #Entity e ON e.Id = x.Id;
        DELETE x FROM dbo.REMSClient        x JOIN #Client c ON c.Id = x.Id;

        -- The intake form: its events, its drafts, then the submissions and the form itself.
        DELETE x FROM dbo.REMSFormEmailEvent x JOIN #Form f ON f.Id = x.REMSFormId;
        DELETE x FROM dbo.REMSFormDraft      x JOIN #Form f ON f.Id = x.REMSFormId;
        DELETE x FROM dbo.REMSFormSubmission x JOIN #Form f ON f.Id = x.REMSFormId;
        DELETE x FROM dbo.REMSForm           x JOIN #Form f ON f.Id = x.Id;

        -- The request's own children, then the request. REMSAdditionalEntity points at REMS twice
        -- (the request that named it, and the request raised from it), so it must go first either way.
        DELETE x FROM dbo.REMSFiles            x JOIN #Rems r ON r.Id = x.REMSId;
        DELETE x FROM dbo.REMSSendBack         x JOIN #Rems r ON r.Id = x.REMSId;
        DELETE x FROM dbo.REMSAdditionalEntity x JOIN #Rems r ON r.Id = x.REMSId;
        DELETE x FROM dbo.REMS                 x JOIN #Rems r ON r.Id = x.Id;

        IF @IncludeDelegations = 1
            DELETE FROM dbo.REMSDelegation WHERE @TenantId IS NULL OR TenantId = @TenantId;

        -- Addresses the entity rows owned, now unreferenced.
        DELETE a FROM dbo.Addresses a JOIN #Address t ON t.Id = a.Id;

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
            DELETE d FROM dbo.RemsDepartmentDirector d
            JOIN dbo.RemsSettings s ON s.Id = d.RemsSettingsId
            WHERE @TenantId IS NULL OR s.TenantId = @TenantId;

            DELETE FROM dbo.RemsSettings WHERE @TenantId IS NULL OR TenantId = @TenantId;
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
