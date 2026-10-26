/*
  REMS data model v2 — data-only migration from ANOTHER catalog: $(Source).dbo.* (v1) -> dbo.* (v2)

  The same move as rems-v2-migrate.sql, reading the old tables from the old database instead of the
  legacy schema. Run it against the NEW catalog, naming the old one:

      sqlcmd -S <server> -d <new catalog> -U <login> -P <password> -I -b -v Source=<old catalog> -i rems-v2-migrate-from.sql

  The new catalog must be a RESTORED COPY of the old database on which the API (or the release scripts)
  has applied the v2 migrations, so that Tenants, Users, Persons, Addresses, Media and the option-set
  items exist there with the same ids the REMS rows point at. A truly empty catalog cannot take this
  data: every request references those rows by id. Both catalogs must be on the same SQL Server
  instance (three-part names); the old one is only read.

  What it does
    1. Seeds the five option sets the new model references by id (platform-level rows, TenantId NULL),
       the policy columns on REMS_Settings and the checklist template. Insert-if-missing, never deleted.
    2. Rebuilds the 18 new tables from the old ones: every row of them is deleted, then recreated.
       Rows that map one-to-one keep their old id (REMS, tasks, checklist items, files, email events,
       addresses, contacts, splits, approvers, rates, methods, tax forms). Rows with no old counterpart
       get an MD5-derived id from their source, so a rerun produces the same ids. A Client's id is the
       id of the Person it was minted from, which is also the key the merge across requests uses.
    3. Parses the drafts of unsubmitted intake forms (JSON) into Client_* rows, Persons and Addresses.
       Persons it creates carry a PersonCode starting MIG-; Addresses it creates are recognised by the
       same derived ids, so both are removed and recreated on a rerun.
    4. Restarts the two number sequences above the highest number in use and prints a verification
       summary. Everything runs in one transaction; any error rolls the whole run back.

  Organisation Persons that are now Client rows stay as the v2 migration left them (soft-deleted).
  Persons minted here carry no PartyType: the v2 migration dropped that column, so the old person columns
  are read from $(Source) and the new rows are written without it.
  Step 4b brings back everything the purge procedure removes besides the REMS and Client tables: the people,
  addresses and media the old data points at, delegations, department directors, and the activity, notifications,
  messages, tags, attachments, reminders, pins, colour codes, checklists and field logs keyed to a request. All
  insert-if-missing. Users, Tenants and option sets are never copied and must already be in the target.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_ID(N'$(Source)') IS NULL THROW 50000, 'Source catalog $(Source) is not on this server: pass -v Source=<old catalog>.', 1;
IF OBJECT_ID(N'[$(Source)].dbo.REMSClient') IS NULL THROW 50000, 'The source catalog has no v1 REMS tables (dbo.REMSClient): $(Source) must be the OLD database.', 1;
IF OBJECT_ID(N'dbo.REMSClient') IS NOT NULL THROW 50000, 'This catalog still has the v1 tables: run against the NEW catalog, after the v2 migrations.', 1;
IF EXISTS (SELECT 1 FROM [$(Source)].dbo.REMS r WHERE r.Deleted = 0 AND NOT EXISTS (SELECT 1 FROM dbo.Tenants t WHERE t.Id = r.TenantId))
    THROW 50000, 'A tenant the old requests belong to is missing here: the target must be a restored copy of the old database, not an empty one.', 1;
IF EXISTS (SELECT 1 FROM [$(Source)].dbo.REMS r WHERE r.Deleted = 0 AND r.CSEId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Users u WHERE u.Id = r.CSEId))
    THROW 50000, 'Users the old requests reference (CSEs) are missing here: the target must be a restored copy of the old database. This script does not copy users.', 1;
IF OBJECT_ID('dbo.Client') IS NULL THROW 50000, 'dbo.Client is missing: apply migration RemsDataModelV2 first.', 1;
IF OBJECT_ID('dbo.OptionSets') IS NULL THROW 50000, 'dbo.OptionSets is missing.', 1;

DECLARE @now datetime2 = SYSUTCDATETIME();
DECLARE @remsEntityType int = 6; -- EntityType.Rems

BEGIN TRAN;

/* ------------------------------------------------------------------------------------------------
   1. Option sets the new model references by id. Platform-level rows (TenantId NULL), insert-if-missing.
   ------------------------------------------------------------------------------------------------ */
DECLARE @sets TABLE ([Key] nvarchar(100), Name nvarchar(200));
INSERT INTO @sets VALUES
    (N'REMS.AddressType',       N'REMS Address Type'),
    (N'REMS.ContactRole',       N'REMS Contact Role'),
    (N'REMS.RelationType',      N'REMS Relation Type'),
    (N'REMS.FilingType',        N'REMS Filing Type'),
    (N'REMS.BillingPreference', N'REMS Billing Preference');

INSERT INTO dbo.OptionSets (Id, TenantId, EntityType, [Key], Name, ParentSetId, ItemSortMode, IsSystem, IsClosed, IsActive, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT NEWID(), NULL, @remsEntityType, s.[Key], s.Name, NULL, N'Custom', 1, 0, 1, @now, @now, 0
FROM @sets s
WHERE NOT EXISTS (SELECT 1 FROM dbo.OptionSets o WHERE o.TenantId IS NULL AND o.[Key] = s.[Key] AND o.Deleted = 0);

DECLARE @items TABLE ([Key] nvarchar(100), Value nvarchar(100), Label nvarchar(200), SortOrder int);
INSERT INTO @items VALUES
    (N'REMS.AddressType', N'physical', N'Physical', 1),
    (N'REMS.AddressType', N'mailing',  N'Mailing',  2),
    (N'REMS.AddressType', N'billing',  N'Billing',  3),
    (N'REMS.ContactRole', N'self',                   N'Self',                    1),
    (N'REMS.ContactRole', N'spouse',                 N'Spouse',                  2),
    (N'REMS.ContactRole', N'primary_client_contact', N'Primary Client Contact',  3),
    (N'REMS.ContactRole', N'financial_contact',      N'Financial Contact',       4),
    (N'REMS.ContactRole', N'billing_contact',        N'Billing Contact',         5),
    (N'REMS.ContactRole', N'other_contact',          N'Other Contact',           6),
    (N'REMS.ContactRole', N'finance_director',       N'Finance Director',        7),
    (N'REMS.ContactRole', N'trust_estate_contact',   N'Trust and Estate Contact', 8),
    (N'REMS.ContactRole', N'banker',                 N'Banker',                  9),
    (N'REMS.ContactRole', N'lawyer',                 N'Lawyer',                 10),
    (N'REMS.RelationType', N'spouse',         N'Spouse',           1),
    (N'REMS.RelationType', N'child',          N'Child',            2),
    (N'REMS.RelationType', N'other',          N'Other Individual', 3),
    (N'REMS.RelationType', N'related_entity', N'Related Entity',   4),
    (N'REMS.FilingType', N'joint',      N'Joint',      1),
    (N'REMS.FilingType', N'individual', N'Individual', 2),
    (N'REMS.BillingPreference', N'primary',  N'Bill the primary client', 1),
    (N'REMS.BillingPreference', N'separate', N'Bill separately',         2);

INSERT INTO dbo.OptionSetItems (Id, OptionSetId, TenantId, ParentItemId, Value, Label, SortOrder, IsDefault, IsActive, IsSystem, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT NEWID(), o.Id, NULL, NULL, i.Value, i.Label, i.SortOrder, 0, 1, 1, @now, @now, 0
FROM @items i
JOIN dbo.OptionSets o ON o.TenantId IS NULL AND o.[Key] = i.[Key] AND o.Deleted = 0
WHERE NOT EXISTS (SELECT 1 FROM dbo.OptionSetItems x WHERE x.OptionSetId = o.Id AND x.Value = i.Value AND x.Deleted = 0);

-- Lookup: (tenant, key, value) -> item id, tenant-owned copy first, platform row otherwise.
IF OBJECT_ID('tempdb..#opt') IS NOT NULL DROP TABLE #opt;
SELECT o.TenantId, o.[Key], i.Value, i.Id
INTO #opt
FROM dbo.OptionSetItems i
JOIN dbo.OptionSets o ON o.Id = i.OptionSetId
WHERE o.Deleted = 0 AND i.Deleted = 0 AND o.[Key] LIKE N'REMS.%';

/* ------------------------------------------------------------------------------------------------
   2. Policy: one settings row per tenant that has requests; values from the current appsettings.
   ------------------------------------------------------------------------------------------------ */
INSERT INTO dbo.REMS_Settings (Id, TenantId, DeclineThreshold, IsStaticRouting, TaxFeeCeilingWithoutShareholder, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT NEWID(), t.TenantId, 1, 1, 10000, @now, @now, 0
FROM (SELECT TenantId FROM [$(Source)].dbo.REMS WHERE Deleted = 0
      UNION SELECT TenantId FROM [$(Source)].dbo.RemsSettings WHERE Deleted = 0) t
WHERE NOT EXISTS (SELECT 1 FROM dbo.REMS_Settings s WHERE s.TenantId = t.TenantId AND s.Deleted = 0);

UPDATE dbo.REMS_Settings SET DeclineThreshold = 1 WHERE DeclineThreshold = 0;
UPDATE dbo.REMS_Settings SET IsStaticRouting = 1 WHERE IsStaticRouting = 0;
UPDATE dbo.REMS_Settings SET TaxFeeCeilingWithoutShareholder = 10000 WHERE TaxFeeCeilingWithoutShareholder IS NULL;

-- 3. Checklist template, from the static catalog in RemsApprovalModels. Shareholders use the Approver list.
DECLARE @tpl TABLE (Role nvarchar(20), DisplayOrder int, Label nvarchar(200));
INSERT INTO @tpl VALUES
    (N'CSE', 1, N'Client information reviewed and accurate'),
    (N'CSE', 2, N'Engagement scope and service line confirmed'),
    (N'DepartmentDirector', 1, N'First-year fee estimate reviewed'),
    (N'DepartmentDirector', 2, N'Realization percentage acceptable'),
    (N'DepartmentDirector', 3, N'Engagement team and department placement appropriate'),
    (N'CommissionRecipient', 1, N'Commission split reviewed'),
    (N'CommissionRecipient', 2, N'Commission allocation accepted'),
    (N'Shareholder', 1, N'Engagement details reviewed'),
    (N'Shareholder', 2, N'Engagement accepted'),
    (N'Approver', 1, N'Engagement details reviewed'),
    (N'Approver', 2, N'Engagement accepted');

INSERT INTO dbo.REMS_ChecklistTemplate (Id, TenantId, SettingsId, ApproverRole, DisplayOrder, Label, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT NEWID(), s.TenantId, s.Id, t.Role, t.DisplayOrder, t.Label, @now, @now, 0
FROM dbo.REMS_Settings s
CROSS JOIN @tpl t
WHERE s.Deleted = 0
  AND NOT EXISTS (SELECT 1 FROM dbo.REMS_ChecklistTemplate x WHERE x.SettingsId = s.Id AND x.ApproverRole = t.Role AND x.DisplayOrder = t.DisplayOrder AND x.Deleted = 0);

/* ------------------------------------------------------------------------------------------------
   4. Source views used more than once.
   ------------------------------------------------------------------------------------------------ */
-- Live requests, with the Person the client was minted from (the merge key) and the request's rank per client.
IF OBJECT_ID('tempdb..#req') IS NOT NULL DROP TABLE #req;
SELECT r.Id, r.TenantId, r.ClientPersonId AS ClientId, r.CreatedOnUtc,
       ROW_NUMBER() OVER (PARTITION BY r.ClientPersonId ORDER BY r.CreatedOnUtc DESC, r.Id) AS RankDesc,
       f.Id AS FormId, f.EntityTypeId AS FormEntityTypeId, f.Status AS FormStatus,
       e.Id AS EngagementId
INTO #req
FROM [$(Source)].dbo.REMS r
LEFT JOIN [$(Source)].dbo.REMSForm f ON f.REMSId = r.Id AND f.Deleted = 0
LEFT JOIN [$(Source)].dbo.REMSEngagement e ON e.REMSId = r.Id AND e.Deleted = 0
WHERE r.Deleted = 0 AND r.ClientPersonId IS NOT NULL;

IF EXISTS (SELECT 1 FROM [$(Source)].dbo.REMS WHERE Deleted = 0 AND ClientPersonId IS NULL)
    THROW 50001, 'A live request has no ClientPersonId; the migration cannot mint its client.', 1;

-- Unsubmitted drafts, parsed later. Read before the delete phase so the delete can use the same derived ids.
IF OBJECT_ID('tempdb..#draft') IS NOT NULL DROP TABLE #draft;
SELECT q.Id AS REMSId, q.TenantId, q.ClientId, q.FormId, d.DraftPayload AS j, d.LastSavedOnUtc
INTO #draft
FROM #req q
JOIN [$(Source)].dbo.REMSFormDraft d ON d.REMSFormId = q.FormId AND d.Deleted = 0
WHERE q.FormStatus NOT IN (N'Submitted', N'Cancelled') AND ISJSON(d.DraftPayload) = 1;

IF OBJECT_ID('tempdb..#draftAddr') IS NOT NULL DROP TABLE #draftAddr;
SELECT dr.REMSId, dr.TenantId, dr.ClientId, dr.FormId, a.Kind, a.Idx,
       CONVERT(uniqueidentifier, HASHBYTES('MD5', N'addr:' + CONVERT(nvarchar(36), dr.FormId) + N':' + a.Kind + N':' + CONVERT(nvarchar(10), a.Idx))) AS AddressId,
       CONVERT(uniqueidentifier, HASHBYTES('MD5', N'caddr:' + CONVERT(nvarchar(36), dr.FormId) + N':' + a.Kind + N':' + CONVERT(nvarchar(10), a.Idx))) AS ClientAddressId,
       a.Street, a.AddressLine2, a.City, a.StateName, a.StateCode, a.Zip, a.CountryCode, a.CountryName, a.FirstName, a.LastName, a.Suffix, a.Email, a.Phone
INTO #draftAddr
FROM #draft dr
CROSS APPLY (
    SELECT N'physical' AS Kind, 0 AS Idx, * FROM OPENJSON(dr.j, '$.physicalAddress') WITH (
        Street nvarchar(200) '$.street', AddressLine2 nvarchar(200) '$.addressLine2', City nvarchar(100) '$.city',
        StateName nvarchar(100) '$.state', StateCode nvarchar(20) '$.stateCode', Zip nvarchar(20) '$.zip',
        CountryCode nvarchar(10) '$.countryCode', CountryName nvarchar(100) '$.countryName',
        FirstName nvarchar(100) '$.firstName', LastName nvarchar(100) '$.lastName', Suffix nvarchar(20) '$.suffix',
        Email nvarchar(256) '$.email', Phone nvarchar(32) '$.phone')
    UNION ALL
    SELECT N'mailing', 0, * FROM OPENJSON(dr.j, '$.mailingAddress') WITH (
        Street nvarchar(200) '$.street', AddressLine2 nvarchar(200) '$.addressLine2', City nvarchar(100) '$.city',
        StateName nvarchar(100) '$.state', StateCode nvarchar(20) '$.stateCode', Zip nvarchar(20) '$.zip',
        CountryCode nvarchar(10) '$.countryCode', CountryName nvarchar(100) '$.countryName',
        FirstName nvarchar(100) '$.firstName', LastName nvarchar(100) '$.lastName', Suffix nvarchar(20) '$.suffix',
        Email nvarchar(256) '$.email', Phone nvarchar(32) '$.phone')
    UNION ALL
    SELECT N'billing', CONVERT(int, b.[key]), x.* FROM OPENJSON(dr.j, '$.billingAddresses') b
    CROSS APPLY OPENJSON(b.value) WITH (
        Street nvarchar(200) '$.street', AddressLine2 nvarchar(200) '$.addressLine2', City nvarchar(100) '$.city',
        StateName nvarchar(100) '$.state', StateCode nvarchar(20) '$.stateCode', Zip nvarchar(20) '$.zip',
        CountryCode nvarchar(10) '$.countryCode', CountryName nvarchar(100) '$.countryName',
        FirstName nvarchar(100) '$.firstName', LastName nvarchar(100) '$.lastName', Suffix nvarchar(20) '$.suffix',
        Email nvarchar(256) '$.email', Phone nvarchar(32) '$.phone') x
) a
WHERE NULLIF(LTRIM(RTRIM(a.Street)), N'') IS NOT NULL;

/* ------------------------------------------------------------------------------------------------
   4b. Everything else the purge procedure removes, brought back from the source where the target
       lacks it: the Media behind request files; the Persons behind clients, contacts and additional
       individuals, with their Addresses; the Addresses behind entity addresses; delegations and
       department directors; and the platform rows keyed to a request — activity, notifications,
       conversation messages and mentions, tags, attachments, reminders, pins, colour codes,
       checklists and field logs. All insert-if-missing by id, so a restored copy that still holds
       them is untouched. Users, Tenants, option sets and the staff behind user accounts are never
       touched: the checks above require users and tenants to be here already.
   ------------------------------------------------------------------------------------------------ */
IF OBJECT_ID('tempdb..#needRems') IS NOT NULL DROP TABLE #needRems;
SELECT r.Id INTO #needRems FROM [$(Source)].dbo.REMS r WHERE r.Deleted = 0;

IF OBJECT_ID('tempdb..#needPerson') IS NOT NULL DROP TABLE #needPerson;
SELECT DISTINCT p.Id
INTO #needPerson
FROM [$(Source)].dbo.Persons p
WHERE p.Id IN (SELECT r.ClientPersonId FROM [$(Source)].dbo.REMS r WHERE r.Deleted = 0 AND r.ClientPersonId IS NOT NULL)
   OR p.Id IN (SELECT ec.PersonId FROM [$(Source)].dbo.REMSEntityContact ec WHERE ec.Deleted = 0)
   OR p.Id IN (SELECT ai.PersonId FROM [$(Source)].dbo.REMSAdditionalIndividual ai WHERE ai.Deleted = 0)
   -- and every other person REMS or a client minted that has no user account: the purge removes those too.
   OR (p.SourceEntityType IN (@remsEntityType, 16) AND p.UserId IS NULL AND NOT EXISTS (SELECT 1 FROM [$(Source)].dbo.Users u WHERE u.PersonId = p.Id));

IF OBJECT_ID('tempdb..#needAddress') IS NOT NULL DROP TABLE #needAddress;
SELECT DISTINCT a.Id
INTO #needAddress
FROM [$(Source)].dbo.Addresses a
WHERE a.Id IN (SELECT p.AddressId FROM [$(Source)].dbo.Persons p JOIN #needPerson n ON n.Id = p.Id WHERE p.AddressId IS NOT NULL)
   OR a.Id IN (SELECT ea.AddressId FROM [$(Source)].dbo.REMSEntityAddress ea WHERE ea.Deleted = 0 AND ea.AddressId IS NOT NULL);

IF OBJECT_ID('tempdb..#needMedia') IS NOT NULL DROP TABLE #needMedia;
SELECT DISTINCT m.Id
INTO #needMedia
FROM [$(Source)].dbo.Media m
WHERE m.Id IN (SELECT f.MediaId FROM [$(Source)].dbo.REMSFiles f WHERE f.Deleted = 0 AND f.MediaId IS NOT NULL)
   OR m.Id IN (SELECT a.ClientAcceptanceFormMediaId FROM [$(Source)].dbo.REMSEngagementAuditDetail a WHERE a.Deleted = 0 AND a.ClientAcceptanceFormMediaId IS NOT NULL)
   OR m.Id IN (SELECT g.PurchaseOrderMediaId FROM [$(Source)].dbo.REMSEngagementGovernmentDetail g WHERE g.Deleted = 0 AND g.PurchaseOrderMediaId IS NOT NULL)
   OR m.Id IN (SELECT p.ProfileMediaId FROM [$(Source)].dbo.Persons p JOIN #needPerson n ON n.Id = p.Id WHERE p.ProfileMediaId IS NOT NULL);

INSERT INTO dbo.Media (Id, MediaType, MediaCategory, OriginalFileName, StoredFileName, FileExtension, MimeType, FileSize, StorageProvider, RelativePath, PublicUrl, Width, Height, IsPublic, IsProcessed, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc)
SELECT m.Id, m.MediaType, m.MediaCategory, m.OriginalFileName, m.StoredFileName, m.FileExtension, m.MimeType, m.FileSize, m.StorageProvider, m.RelativePath, m.PublicUrl, m.Width, m.Height, m.IsPublic, m.IsProcessed, m.CreatedById, m.CreatedOnUtc, m.UpdatedById, m.UpdatedOnUtc, m.Deleted, m.DeletedOnUtc
FROM [$(Source)].dbo.Media m
JOIN #needMedia n ON n.Id = m.Id
WHERE NOT EXISTS (SELECT 1 FROM dbo.Media x WHERE x.Id = m.Id);

INSERT INTO dbo.Addresses (Id, AddressType, AddressLine1, AddressLine2, Landmark, BuildingName, FloorNumber, UnitNumber, CountryCode, CountryName, StateCode, StateName, CityName, PostalCode, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc, Email, FirstName, LastName, PhoneNumber, Suffix)
SELECT a.Id, a.AddressType, a.AddressLine1, a.AddressLine2, a.Landmark, a.BuildingName, a.FloorNumber, a.UnitNumber, a.CountryCode, a.CountryName, a.StateCode, a.StateName, a.CityName, a.PostalCode, a.CreatedById, a.CreatedOnUtc, a.UpdatedById, a.UpdatedOnUtc, a.Deleted, a.DeletedOnUtc, a.Email, a.FirstName, a.LastName, a.PhoneNumber, a.Suffix
FROM [$(Source)].dbo.Addresses a
JOIN #needAddress n ON n.Id = a.Id
WHERE NOT EXISTS (SELECT 1 FROM dbo.Addresses x WHERE x.Id = a.Id);

-- Organisation persons come back the way the v2 migration leaves them: soft-deleted, their party now a Client row.
INSERT INTO dbo.Persons (Id, PersonCode, UserId, ProfileMediaId, AddressId, FirstName, MiddleName, LastName, DisplayName, PreferredName, Gender, DateOfBirth, MaritalStatus, Nationality, PrimaryEmail, SecondaryEmail, MobileNumber, CountryCode, AlternateMobileNumber, EmergencyContactName, EmergencyContactRelationship, EmergencyContactNumber, EmployeeCode, ProfileCompletionPercentage, IsProfileVerified, LastProfileUpdatedOn, Notes, IsActive, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc, TenantId, SourceEntityId, SourceEntityType, Suffix)
SELECT p.Id, p.PersonCode, p.UserId, p.ProfileMediaId, p.AddressId, p.FirstName, p.MiddleName, p.LastName, p.DisplayName, p.PreferredName, p.Gender, p.DateOfBirth, p.MaritalStatus, p.Nationality, p.PrimaryEmail, p.SecondaryEmail, p.MobileNumber, p.CountryCode, p.AlternateMobileNumber, p.EmergencyContactName, p.EmergencyContactRelationship, p.EmergencyContactNumber, p.EmployeeCode, p.ProfileCompletionPercentage, p.IsProfileVerified, p.LastProfileUpdatedOn, p.Notes, p.IsActive, p.CreatedById, p.CreatedOnUtc, p.UpdatedById, p.UpdatedOnUtc,
       CASE WHEN p.PartyType = 1 THEN 1 ELSE p.Deleted END,
       CASE WHEN p.PartyType = 1 AND p.Deleted = 0 THEN @now ELSE p.DeletedOnUtc END,
       p.TenantId, p.SourceEntityId, p.SourceEntityType, p.Suffix
FROM [$(Source)].dbo.Persons p
JOIN #needPerson n ON n.Id = p.Id
WHERE NOT EXISTS (SELECT 1 FROM dbo.Persons x WHERE x.Id = p.Id);

-- Who may act for whom. The two flags were renamed by the v2 migration.
INSERT INTO dbo.REMS_Delegation (Id, TenantId, PrincipalUserId, DelegateUserId, IsPrepareAllowed, IsSendAllowed, StartsOn, EndsOn, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc)
SELECT d.Id, d.TenantId, d.PrincipalUserId, d.DelegateUserId, d.CanPrepare, d.CanSend, d.StartsOn, d.EndsOn, d.CreatedById, d.CreatedOnUtc, d.UpdatedById, d.UpdatedOnUtc, d.Deleted, d.DeletedOnUtc
FROM [$(Source)].dbo.REMSDelegation d
WHERE NOT EXISTS (SELECT 1 FROM dbo.REMS_Delegation x WHERE x.Id = d.Id);

-- Department directors hang off the tenant's settings row, seeded in step 2 for every tenant that had one.
INSERT INTO dbo.REMS_DepartmentDirector (Id, TenantId, SettingsId, DirectorUserId, DepartmentId, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc)
SELECT d.Id, d.TenantId, s.Id, d.DirectorUserId, d.DepartmentId, d.CreatedById, d.CreatedOnUtc, d.UpdatedById, d.UpdatedOnUtc, d.Deleted, d.DeletedOnUtc
FROM [$(Source)].dbo.RemsDepartmentDirector d
JOIN dbo.REMS_Settings s ON s.TenantId = d.TenantId AND s.Deleted = 0
WHERE NOT EXISTS (SELECT 1 FROM dbo.REMS_DepartmentDirector x WHERE x.Id = d.Id)
  -- a live row for the same department already there wins; retired rows come back as they were
  AND NOT (d.Deleted = 0 AND EXISTS (SELECT 1 FROM dbo.REMS_DepartmentDirector x WHERE x.SettingsId = s.Id AND x.DepartmentId = d.DepartmentId AND x.Deleted = 0));

-- The platform rows keyed to a live request, by the shared (EntityType, EntityId) key.
INSERT INTO dbo.ActivityEvents (Id, TenantId, EntityType, EntityId, EventType, ActorId, OldValue, NewValue, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc)
SELECT x.Id, x.TenantId, x.EntityType, x.EntityId, x.EventType, x.ActorId, x.OldValue, x.NewValue, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, x.Deleted, x.DeletedOnUtc
FROM [$(Source)].dbo.ActivityEvents x JOIN #needRems r ON r.Id = x.EntityId
WHERE x.EntityType = @remsEntityType AND NOT EXISTS (SELECT 1 FROM dbo.ActivityEvents y WHERE y.Id = x.Id);

INSERT INTO dbo.Notifications (Id, TenantId, UserId, Type, Title, Body, EntityType, EntityId, IsRead, IsGrouped, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc)
SELECT x.Id, x.TenantId, x.UserId, x.Type, x.Title, x.Body, x.EntityType, x.EntityId, x.IsRead, x.IsGrouped, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, x.Deleted, x.DeletedOnUtc
FROM [$(Source)].dbo.Notifications x JOIN #needRems r ON r.Id = x.EntityId
WHERE x.EntityType = @remsEntityType AND NOT EXISTS (SELECT 1 FROM dbo.Notifications y WHERE y.Id = x.Id);

INSERT INTO dbo.ConversationMessages (Id, TenantId, EntityType, EntityId, Body, IsEdited, EditedOnUtc, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc)
SELECT x.Id, x.TenantId, x.EntityType, x.EntityId, x.Body, x.IsEdited, x.EditedOnUtc, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, x.Deleted, x.DeletedOnUtc
FROM [$(Source)].dbo.ConversationMessages x JOIN #needRems r ON r.Id = x.EntityId
WHERE x.EntityType = @remsEntityType AND NOT EXISTS (SELECT 1 FROM dbo.ConversationMessages y WHERE y.Id = x.Id);

INSERT INTO dbo.ConversationMessageMentions (Id, TenantId, ConversationMessageId, MentionedUserId, IsRead, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc)
SELECT x.Id, x.TenantId, x.ConversationMessageId, x.MentionedUserId, x.IsRead, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, x.Deleted, x.DeletedOnUtc
FROM [$(Source)].dbo.ConversationMessageMentions x
JOIN [$(Source)].dbo.ConversationMessages m ON m.Id = x.ConversationMessageId
JOIN #needRems r ON r.Id = m.EntityId
WHERE m.EntityType = @remsEntityType AND NOT EXISTS (SELECT 1 FROM dbo.ConversationMessageMentions y WHERE y.Id = x.Id);

INSERT INTO dbo.EntityTags (Id, TenantId, EntityType, EntityId, TagId, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc)
SELECT x.Id, x.TenantId, x.EntityType, x.EntityId, x.TagId, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, x.Deleted, x.DeletedOnUtc
FROM [$(Source)].dbo.EntityTags x JOIN #needRems r ON r.Id = x.EntityId
WHERE x.EntityType = @remsEntityType AND EXISTS (SELECT 1 FROM dbo.Tags t WHERE t.Id = x.TagId)
  AND NOT EXISTS (SELECT 1 FROM dbo.EntityTags y WHERE y.Id = x.Id);

INSERT INTO dbo.Attachments (Id, TenantId, EntityType, EntityId, FileName, StoredPath, MimeType, FileSize, FileExtension, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc)
SELECT x.Id, x.TenantId, x.EntityType, x.EntityId, x.FileName, x.StoredPath, x.MimeType, x.FileSize, x.FileExtension, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, x.Deleted, x.DeletedOnUtc
FROM [$(Source)].dbo.Attachments x JOIN #needRems r ON r.Id = x.EntityId
WHERE x.EntityType = @remsEntityType AND NOT EXISTS (SELECT 1 FROM dbo.Attachments y WHERE y.Id = x.Id);

INSERT INTO dbo.Reminders (Id, TenantId, EntityType, EntityId, UserId, DueAtUtc, Note, IsDispatched, IsOverdue, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc)
SELECT x.Id, x.TenantId, x.EntityType, x.EntityId, x.UserId, x.DueAtUtc, x.Note, x.IsDispatched, x.IsOverdue, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, x.Deleted, x.DeletedOnUtc
FROM [$(Source)].dbo.Reminders x JOIN #needRems r ON r.Id = x.EntityId
WHERE x.EntityType = @remsEntityType AND NOT EXISTS (SELECT 1 FROM dbo.Reminders y WHERE y.Id = x.Id);

INSERT INTO dbo.Pins (Id, TenantId, UserId, EntityType, EntityId, PinnedOnUtc, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc)
SELECT x.Id, x.TenantId, x.UserId, x.EntityType, x.EntityId, x.PinnedOnUtc, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, x.Deleted, x.DeletedOnUtc
FROM [$(Source)].dbo.Pins x JOIN #needRems r ON r.Id = x.EntityId
WHERE x.EntityType = @remsEntityType AND NOT EXISTS (SELECT 1 FROM dbo.Pins y WHERE y.Id = x.Id);

INSERT INTO dbo.ColourCodes (Id, TenantId, UserId, EntityType, EntityId, Colour, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc)
SELECT x.Id, x.TenantId, x.UserId, x.EntityType, x.EntityId, x.Colour, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, x.Deleted, x.DeletedOnUtc
FROM [$(Source)].dbo.ColourCodes x JOIN #needRems r ON r.Id = x.EntityId
WHERE x.EntityType = @remsEntityType AND NOT EXISTS (SELECT 1 FROM dbo.ColourCodes y WHERE y.Id = x.Id);

INSERT INTO dbo.Checklists (Id, TenantId, EntityType, EntityId, Title, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc)
SELECT x.Id, x.TenantId, x.EntityType, x.EntityId, x.Title, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, x.Deleted, x.DeletedOnUtc
FROM [$(Source)].dbo.Checklists x JOIN #needRems r ON r.Id = x.EntityId
WHERE x.EntityType = @remsEntityType AND NOT EXISTS (SELECT 1 FROM dbo.Checklists y WHERE y.Id = x.Id);

INSERT INTO dbo.ChecklistItems (Id, TenantId, ChecklistId, Text, IsCompleted, CompletedById, CompletedOnUtc, SortOrder, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted, DeletedOnUtc)
SELECT x.Id, x.TenantId, x.ChecklistId, x.Text, x.IsCompleted, x.CompletedById, x.CompletedOnUtc, x.SortOrder, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, x.Deleted, x.DeletedOnUtc
FROM [$(Source)].dbo.ChecklistItems x
JOIN [$(Source)].dbo.Checklists c ON c.Id = x.ChecklistId
JOIN #needRems r ON r.Id = c.EntityId
WHERE c.EntityType = @remsEntityType AND NOT EXISTS (SELECT 1 FROM dbo.ChecklistItems y WHERE y.Id = x.Id);

INSERT INTO dbo.FieldModifiedLogs (Id, TenantId, EntityType, EntityId, FieldName, OldValue, NewValue, ChangedById, ChangedOnUtc)
SELECT x.Id, x.TenantId, x.EntityType, x.EntityId, x.FieldName, x.OldValue, x.NewValue, x.ChangedById, x.ChangedOnUtc
FROM [$(Source)].dbo.FieldModifiedLogs x JOIN #needRems r ON r.Id = x.EntityId
WHERE x.EntityType = @remsEntityType AND NOT EXISTS (SELECT 1 FROM dbo.FieldModifiedLogs y WHERE y.Id = x.Id);

/* ------------------------------------------------------------------------------------------------
   5. Rebuild: clear every row of the new tables and what the script created in the shared tables.
   ------------------------------------------------------------------------------------------------ */
DELETE FROM dbo.REMS_ApprovalChecklistItem;
DELETE FROM dbo.REMS_ApprovalTask;
DELETE FROM dbo.REMS_Approver;
DELETE FROM dbo.REMS_CommissionSplit;
DELETE FROM dbo.REMS_PersonnelRate;
DELETE FROM dbo.REMS_MarketingMethod;
DELETE FROM dbo.REMS_TaxForm;
DELETE FROM dbo.REMS_TaxDueDate;
DELETE FROM dbo.REMS_File;
DELETE FROM dbo.REMS_EmailEvent;
DELETE FROM dbo.REMS_StatusHistory;
DELETE FROM dbo.REMS;
DELETE FROM dbo.Client_Relationship;
DELETE FROM dbo.Client_Contact;
DELETE FROM dbo.Client_Address;
DELETE FROM dbo.Client_Contract;
DELETE FROM dbo.Client;
DELETE FROM dbo.Persons WHERE PersonCode LIKE N'MIG-%';
DELETE a FROM dbo.Addresses a WHERE a.Id IN (SELECT AddressId FROM #draftAddr);

/* ------------------------------------------------------------------------------------------------
   6. Client: one per Person a live request names as its client. Latest request wins on every field.
   ------------------------------------------------------------------------------------------------ */
IF OBJECT_ID('tempdb..#client') IS NOT NULL DROP TABLE #client;
SELECT q.ClientId, q.TenantId,
       MIN(q.CreatedOnUtc) AS FirstOnUtc,
       MAX(q.CreatedOnUtc) AS LastOnUtc
INTO #client
FROM #req q
GROUP BY q.ClientId, q.TenantId;

INSERT INTO dbo.Client (Id, TenantId, ClientNumber, Status, PersonId, LegalName, EntityTypeId, IndustryId, EIN, FiscalYearEnd,
                        PrimaryEmail, PrimaryPhone, ReferralSourceId, ReferralSourceDetail,
                        MaconomyCustomerNumber, MaconomySyncStatus, MaconomySyncedOnUtc, MaconomySyncError,
                        CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT c.ClientId, c.TenantId,
       N'CL-' + CONVERT(nvarchar(20), ROW_NUMBER() OVER (ORDER BY c.FirstOnUtc, c.ClientId)),
       CASE WHEN EXISTS (SELECT 1 FROM [$(Source)].dbo.REMS r JOIN #opt o ON o.Id = r.StatusId AND o.[Key] = N'REMS.Status' AND o.Value = N'approved'
                         WHERE r.ClientPersonId = c.ClientId AND r.Deleted = 0) THEN N'Active' ELSE N'Prospect' END,
       CASE WHEN p.PartyType = 0 THEN p.Id END,
       CASE WHEN p.PartyType <> 0 THEN COALESCE(NULLIF(p.CorporateName, N''), NULLIF(lc.Name, N''), p.DisplayName) END,
       COALESCE(lq.FormEntityTypeId, CASE WHEN p.PartyType = 0 THEN ind.Id END),
       le.IndustryId,
       NULLIF(len_.EIN, N''),
       COALESCE(ltx.FiscalYearEnd, lau.ClientFiscalYearEnd),
       COALESCE(NULLIF(lc.Email, N''), NULLIF(p.PrimaryEmail, N'')),
       COALESCE(NULLIF(lc.MobileNumber, N''), NULLIF(p.MobileNumber, N'')),
       lc.ReferralSourceId, NULLIF(lc.ReferralSourceDetail, N''),
       COALESCE(NULLIF(ms.CustomerNumber, N''), NULLIF(p.MaconomyCustomerNumber, N'')),
       COALESCE(ms.Status, N'NotSynced'),
       ms.SyncedOnUtc,
       NULLIF(ms.FailureReason, N''),
       fr.CreatedById, c.FirstOnUtc, fr.UpdatedById, @now, 0
FROM #client c
JOIN [$(Source)].dbo.Persons p ON p.Id = c.ClientId   -- the old catalog still has PartyType, CorporateName and the Maconomy number
JOIN #req lq ON lq.ClientId = c.ClientId AND lq.RankDesc = 1
JOIN [$(Source)].dbo.REMS fr ON fr.Id = lq.Id
LEFT JOIN [$(Source)].dbo.REMSEngagement le ON le.Id = lq.EngagementId
OUTER APPLY (SELECT TOP 1 x.* FROM [$(Source)].dbo.REMSClient x JOIN #req q2 ON q2.Id = x.REMSId WHERE q2.ClientId = c.ClientId AND x.Deleted = 0 ORDER BY q2.RankDesc) lc
OUTER APPLY (SELECT TOP 1 x.EIN FROM [$(Source)].dbo.REMSEntity x JOIN [$(Source)].dbo.REMSClient x2 ON x2.Id = x.REMSClientId JOIN #req q2 ON q2.Id = x2.REMSId
             WHERE q2.ClientId = c.ClientId AND x.Deleted = 0 AND NULLIF(x.EIN, N'') IS NOT NULL ORDER BY q2.RankDesc) len_
OUTER APPLY (SELECT TOP 1 x.FiscalYearEnd FROM [$(Source)].dbo.REMSEngagementTaxDetail x JOIN #req q2 ON q2.EngagementId = x.REMSEngagementId
             WHERE q2.ClientId = c.ClientId AND x.Deleted = 0 AND x.FiscalYearEnd IS NOT NULL ORDER BY q2.RankDesc) ltx
OUTER APPLY (SELECT TOP 1 x.ClientFiscalYearEnd FROM [$(Source)].dbo.REMSEngagementAuditDetail x JOIN #req q2 ON q2.EngagementId = x.REMSEngagementId
             WHERE q2.ClientId = c.ClientId AND x.Deleted = 0 AND x.ClientFiscalYearEnd IS NOT NULL ORDER BY q2.RankDesc) lau
OUTER APPLY (SELECT TOP 1 x.* FROM [$(Source)].dbo.REMSMaconomyCustomerSync x JOIN #req q2 ON q2.Id = x.REMSId WHERE q2.ClientId = c.ClientId AND x.Deleted = 0 ORDER BY q2.RankDesc) ms
OUTER APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.EntityType' AND o.Value = N'individual' ORDER BY CASE WHEN o.TenantId = c.TenantId THEN 0 ELSE 1 END) ind;

/* ------------------------------------------------------------------------------------------------
   7. Client_Address and Client_Contact from the materialised entity rows (ids kept).
   ------------------------------------------------------------------------------------------------ */
INSERT INTO dbo.Client_Address (Id, TenantId, ClientId, AddressId, AddressTypeId, IsCurrent, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT ea.Id, ea.TenantId, q.ClientId, ea.AddressId, o.Id, CASE WHEN q.RankDesc = 1 THEN 1 ELSE 0 END,
       ea.CreatedById, ea.CreatedOnUtc, ea.UpdatedById, ea.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSEntityAddress ea
JOIN [$(Source)].dbo.REMSEntity en ON en.Id = ea.REMSEntityId
JOIN [$(Source)].dbo.REMSClient lc ON lc.Id = en.REMSClientId
JOIN #req q ON q.Id = lc.REMSId
CROSS APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.AddressType' AND o.Value = LOWER(ea.AddressType)
             ORDER BY CASE WHEN o.TenantId = ea.TenantId THEN 0 ELSE 1 END) o
WHERE ea.Deleted = 0 AND en.Deleted = 0 AND lc.Deleted = 0;

-- Contact roles: the legacy enum name becomes the option code.
IF OBJECT_ID('tempdb..#role') IS NOT NULL DROP TABLE #role;
CREATE TABLE #role (Legacy nvarchar(40), JsonKey nvarchar(40), Code nvarchar(40));
INSERT INTO #role VALUES
    (N'Self', N'self', N'self'), (N'Spouse', N'spouse', N'spouse'),
    (N'PrimaryClientContact', N'primaryContact', N'primary_client_contact'),
    (N'FinancialContact', N'financialContact', N'financial_contact'),
    (N'BillingContact', N'billingContact', N'billing_contact'),
    (N'OtherContact', N'otherContact', N'other_contact'),
    (N'FinanceDirector', N'financeDirector', N'finance_director'),
    (N'TrustEstateContact', N'trustEstateContact', N'trust_estate_contact'),
    (N'Banker', N'banker', N'banker'), (N'Lawyer', N'lawyer', N'lawyer'),
    (N'Ceo', N'ceo', N'other_contact'), (N'Cfo', N'cfo', N'other_contact'), (N'AccountsPayable', N'accountsPayable', N'other_contact');

INSERT INTO dbo.Client_Contact (Id, TenantId, ClientId, PersonId, ContactRoleId, IsPrimary, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT ec.Id, ec.TenantId, q.ClientId, ec.PersonId, o.Id, CASE WHEN ec.ContactRole = N'PrimaryClientContact' THEN 1 ELSE 0 END,
       ec.CreatedById, ec.CreatedOnUtc, ec.UpdatedById, ec.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSEntityContact ec
JOIN [$(Source)].dbo.REMSEntity en ON en.Id = ec.REMSEntityId
JOIN [$(Source)].dbo.REMSClient lc ON lc.Id = en.REMSClientId
JOIN #req q ON q.Id = lc.REMSId
JOIN #role rl ON rl.Legacy = ec.ContactRole
CROSS APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.ContactRole' AND o.Value = rl.Code
             ORDER BY CASE WHEN o.TenantId = ec.TenantId THEN 0 ELSE 1 END) o
WHERE ec.Deleted = 0 AND en.Deleted = 0 AND lc.Deleted = 0
  AND NOT EXISTS (SELECT 1 FROM dbo.Client_Contact x WHERE x.ClientId = q.ClientId AND x.PersonId = ec.PersonId AND x.ContactRoleId = o.Id);

-- The retired billing columns on REMSClient become a billing contact where no BillingContact row exists.
INSERT INTO dbo.Persons (Id, PersonCode, FirstName, LastName, DisplayName, PrimaryEmail, ProfileCompletionPercentage, IsProfileVerified, IsActive,
                         TenantId, SourceEntityType, SourceEntityId, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT CONVERT(uniqueidentifier, HASHBYTES('MD5', N'cbill:' + CONVERT(nvarchar(36), lc.Id))),
       N'MIG-' + LEFT(CONVERT(nvarchar(36), lc.Id), 12),
       LEFT(lc.BillingContactName, CHARINDEX(N' ', lc.BillingContactName + N' ') - 1),
       NULLIF(LTRIM(SUBSTRING(lc.BillingContactName, CHARINDEX(N' ', lc.BillingContactName + N' '), 200)), N''),
       lc.BillingContactName, NULLIF(lc.BillingEmail, N''), 0, 0, 1,
       lc.TenantId, @remsEntityType, lc.REMSId, lc.CreatedById, lc.CreatedOnUtc, lc.UpdatedById, @now, 0
FROM [$(Source)].dbo.REMSClient lc
JOIN #req q ON q.Id = lc.REMSId
WHERE lc.Deleted = 0 AND NULLIF(lc.BillingContactName, N'') IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.Client_Contact cc JOIN #opt o ON o.Id = cc.ContactRoleId AND o.Value = N'billing_contact' WHERE cc.ClientId = q.ClientId);

INSERT INTO dbo.Client_Contact (Id, TenantId, ClientId, PersonId, ContactRoleId, IsPrimary, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT CONVERT(uniqueidentifier, HASHBYTES('MD5', N'cbillc:' + CONVERT(nvarchar(36), lc.Id))), lc.TenantId, q.ClientId, p.Id, o.Id, 0,
       lc.CreatedById, lc.CreatedOnUtc, lc.UpdatedById, @now, 0
FROM [$(Source)].dbo.REMSClient lc
JOIN #req q ON q.Id = lc.REMSId
JOIN dbo.Persons p ON p.Id = CONVERT(uniqueidentifier, HASHBYTES('MD5', N'cbill:' + CONVERT(nvarchar(36), lc.Id)))
CROSS APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.ContactRole' AND o.Value = N'billing_contact' ORDER BY CASE WHEN o.TenantId = lc.TenantId THEN 0 ELSE 1 END) o;

/* ------------------------------------------------------------------------------------------------
   8. Related parties become Client rows plus Client_Relationship.
   ------------------------------------------------------------------------------------------------ */
-- Individuals: the Client's id is the individual's Person id.
INSERT INTO dbo.Client (Id, TenantId, ClientNumber, Status, PersonId, LegalName, EntityTypeId, PrimaryEmail, PrimaryPhone,
                        MaconomySyncStatus, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT ai.PersonId, ai.TenantId, N'TMP-' + CONVERT(nvarchar(36), ai.PersonId), N'Prospect', ai.PersonId, NULL, ind.Id,
       NULLIF(ai.Email, N''), NULLIF(ai.PhoneNumber, N''), N'NotSynced',
       ai.CreatedById, ai.CreatedOnUtc, ai.UpdatedById, @now, 0
FROM (SELECT x.*, ROW_NUMBER() OVER (PARTITION BY x.PersonId ORDER BY x.CreatedOnUtc DESC) AS rn FROM [$(Source)].dbo.REMSAdditionalIndividual x WHERE x.Deleted = 0) ai
JOIN #req q ON q.Id = ai.REMSId
OUTER APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.EntityType' AND o.Value = N'individual' ORDER BY CASE WHEN o.TenantId = ai.TenantId THEN 0 ELSE 1 END) ind
WHERE ai.rn = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.Client c WHERE c.Id = ai.PersonId);

INSERT INTO dbo.Client_Relationship (Id, TenantId, ClientId, RelatedClientId, RelationTypeId, FilingTypeId, BillingPreferenceId, IsMinor, RelatedStatusId,
                                     CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT ai.Id, ai.TenantId, q.ClientId, ai.PersonId, rt.Id, ft.Id, bp.Id, ISNULL(ai.IsMinor, 0), ai.RelatedStatusId,
       ai.CreatedById, ai.CreatedOnUtc, ai.UpdatedById, ai.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSAdditionalIndividual ai
JOIN #req q ON q.Id = ai.REMSId
CROSS APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.RelationType' AND o.Value = LOWER(ai.RelationType) ORDER BY CASE WHEN o.TenantId = ai.TenantId THEN 0 ELSE 1 END) rt
OUTER APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.FilingType' AND o.Value = LOWER(ai.FilingType) ORDER BY CASE WHEN o.TenantId = ai.TenantId THEN 0 ELSE 1 END) ft
OUTER APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.BillingPreference' AND o.Value = LOWER(ai.BillingPreference) ORDER BY CASE WHEN o.TenantId = ai.TenantId THEN 0 ELSE 1 END) bp
WHERE ai.Deleted = 0 AND q.ClientId <> ai.PersonId
  AND NOT EXISTS (SELECT 1 FROM dbo.Client_Relationship x WHERE x.ClientId = q.ClientId AND x.RelatedClientId = ai.PersonId);

-- The person billed for an individual, where one was named, is a billing contact on that individual's client.
INSERT INTO dbo.Persons (Id, PersonCode, FirstName, LastName, DisplayName, ProfileCompletionPercentage, IsProfileVerified, IsActive,
                         TenantId, SourceEntityType, SourceEntityId, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT CONVERT(uniqueidentifier, HASHBYTES('MD5', N'indbill:' + CONVERT(nvarchar(36), ai.Id))),
       N'MIG-' + LEFT(CONVERT(nvarchar(36), ai.Id), 12),
       ISNULL(ai.BillingFirstName, N''), ISNULL(ai.BillingLastName, N''),
       LTRIM(RTRIM(ISNULL(ai.BillingFirstName, N'') + N' ' + ISNULL(ai.BillingLastName, N''))), 0, 0, 1,
       ai.TenantId, @remsEntityType, ai.REMSId, ai.CreatedById, ai.CreatedOnUtc, ai.UpdatedById, @now, 0
FROM [$(Source)].dbo.REMSAdditionalIndividual ai
WHERE ai.Deleted = 0 AND (NULLIF(ai.BillingFirstName, N'') IS NOT NULL OR NULLIF(ai.BillingLastName, N'') IS NOT NULL);

INSERT INTO dbo.Client_Contact (Id, TenantId, ClientId, PersonId, ContactRoleId, IsPrimary, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT CONVERT(uniqueidentifier, HASHBYTES('MD5', N'indbillc:' + CONVERT(nvarchar(36), ai.Id))), ai.TenantId, ai.PersonId,
       CONVERT(uniqueidentifier, HASHBYTES('MD5', N'indbill:' + CONVERT(nvarchar(36), ai.Id))), o.Id, 0,
       ai.CreatedById, ai.CreatedOnUtc, ai.UpdatedById, @now, 0
FROM [$(Source)].dbo.REMSAdditionalIndividual ai
JOIN dbo.Client c ON c.Id = ai.PersonId
CROSS APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.ContactRole' AND o.Value = N'billing_contact' ORDER BY CASE WHEN o.TenantId = ai.TenantId THEN 0 ELSE 1 END) o
WHERE ai.Deleted = 0 AND (NULLIF(ai.BillingFirstName, N'') IS NOT NULL OR NULLIF(ai.BillingLastName, N'') IS NOT NULL);

-- Entities: where a request was already raised for one, it is that request's client; otherwise a derived id.
IF OBJECT_ID('tempdb..#relEnt') IS NOT NULL DROP TABLE #relEnt;
SELECT ae.*, q.ClientId AS PrimaryClientId,
       COALESCE(cr.ClientPersonId, CONVERT(uniqueidentifier, HASHBYTES('MD5', N'rent:' + CONVERT(nvarchar(36), ae.Id)))) AS RelatedClientId
INTO #relEnt
FROM [$(Source)].dbo.REMSAdditionalEntity ae
JOIN #req q ON q.Id = ae.REMSId
LEFT JOIN [$(Source)].dbo.REMS cr ON cr.Id = ae.CreatedREMSId AND cr.Deleted = 0
WHERE ae.Deleted = 0;

INSERT INTO dbo.Client (Id, TenantId, ClientNumber, Status, PersonId, LegalName, PrimaryEmail, PrimaryPhone, MaconomySyncStatus,
                        CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT re.RelatedClientId, re.TenantId, N'TMP-' + CONVERT(nvarchar(36), re.RelatedClientId), N'Prospect', NULL, re.FullName, NULLIF(re.EmailAddress, N''), NULLIF(re.PhoneNumber, N''), N'NotSynced',
       re.CreatedById, re.CreatedOnUtc, re.UpdatedById, @now, 0
FROM (SELECT x.*, ROW_NUMBER() OVER (PARTITION BY x.RelatedClientId ORDER BY x.CreatedOnUtc DESC) AS rn FROM #relEnt x) re
WHERE re.rn = 1 AND NOT EXISTS (SELECT 1 FROM dbo.Client c WHERE c.Id = re.RelatedClientId);

INSERT INTO dbo.Client_Relationship (Id, TenantId, ClientId, RelatedClientId, RelationTypeId, IsMinor, RelatedStatusId,
                                     CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT re.Id, re.TenantId, re.PrimaryClientId, re.RelatedClientId, rt.Id, 0, re.RelatedStatusId,
       re.CreatedById, re.CreatedOnUtc, re.UpdatedById, re.UpdatedOnUtc, 0
FROM #relEnt re
CROSS APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.RelationType' AND o.Value = N'related_entity' ORDER BY CASE WHEN o.TenantId = re.TenantId THEN 0 ELSE 1 END) rt
WHERE re.PrimaryClientId <> re.RelatedClientId
  AND NOT EXISTS (SELECT 1 FROM dbo.Client_Relationship x WHERE x.ClientId = re.PrimaryClientId AND x.RelatedClientId = re.RelatedClientId);

/* ------------------------------------------------------------------------------------------------
   9. Client_Contract from the government details (id kept); the latest request's contract is current.
   ------------------------------------------------------------------------------------------------ */
INSERT INTO dbo.Client_Contract (Id, TenantId, ClientId, ContractStartDate, ContractEndDate, OriginalTerm, RenewalTerms, PurchaseOrderStartDate, PurchaseOrderEndDate, IsCurrent,
                                 CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT g.Id, g.TenantId, q.ClientId, g.ContractStartDate, g.ContractEndDate, NULLIF(g.OriginalTerm, N''), NULLIF(g.RenewalTerms, N''),
       g.PurchaseOrderStartDate, g.PurchaseOrderEndDate,
       CASE WHEN ROW_NUMBER() OVER (PARTITION BY q.ClientId ORDER BY q.RankDesc) = 1 THEN 1 ELSE 0 END,
       g.CreatedById, g.CreatedOnUtc, g.UpdatedById, g.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSEngagementGovernmentDetail g
JOIN #req q ON q.EngagementId = g.REMSEngagementId
WHERE g.Deleted = 0
  AND (g.ContractStartDate IS NOT NULL OR g.ContractEndDate IS NOT NULL OR NULLIF(g.OriginalTerm, N'') IS NOT NULL
       OR NULLIF(g.RenewalTerms, N'') IS NOT NULL OR g.PurchaseOrderStartDate IS NOT NULL OR g.PurchaseOrderEndDate IS NOT NULL);

/* ------------------------------------------------------------------------------------------------
   10. The request as one row (id kept).
   ------------------------------------------------------------------------------------------------ */
INSERT INTO dbo.REMS (Id, TenantId, RemsNumber, TypeId, StatusId, ClientId, CSEId, AdminAssignedToId, OnBehalfOfUserId,
                      InviteCode, InviteRecipientEmail, InviteSentByUserId, InviteSentOnUtc, FormLastSavedOnUtc, FormSubmittedOnUtc,
                      DepartmentId, ServiceLineId, JobTemplateId, BillingPeriodId, DepartmentDirectorId, EngagementExecutiveId, BillingManagerId,
                      FirstYearFeeEstimate, EngagementFee, RealizationPercentage, BillingProcessDescription,
                      ContractId, ContractNumber, PurchaseOrderNumber, PurchaseOrderAmount, IsFloridaOnePercentFeeApplicable,
                      CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT r.Id, r.TenantId, r.REMSNumber, r.TypeId, r.StatusId, q.ClientId, r.CSEId, r.AdminAssignedToId, r.OnBehalfOfUserId,
       CASE WHEN f.Status IN (N'Sent', N'Submitted', N'Saved') THEN f.InviteCode END,
       se.RecipientEmail,
       CASE WHEN f.SentOnUtc IS NOT NULL THEN f.CreatedByUserId END,
       f.SentOnUtc, d.LastSavedOnUtc, f.SubmittedOnUtc,
       e.DepartmentId, e.ServiceLineId, e.JobTemplateId, e.BillingPeriodId, e.DepartmentDirectorId, e.EngagementExecutiveId, e.BillingManagerId,
       e.FirstYearFeeEstimate, e.EngagementFee, e.RealizationPercentage, NULLIF(e.BillingProcessDescription, N''),
       cc.Id, NULLIF(g.ContractNumber, N''), NULLIF(g.PurchaseOrderNumber, N''), g.PurchaseOrderAmount, g.FloridaOnePercentStateFeeApplies,
       r.CreatedById, r.CreatedOnUtc, r.UpdatedById, r.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMS r
JOIN #req q ON q.Id = r.Id
LEFT JOIN [$(Source)].dbo.REMSForm f ON f.Id = q.FormId
LEFT JOIN [$(Source)].dbo.REMSFormDraft d ON d.REMSFormId = f.Id AND d.Deleted = 0
LEFT JOIN [$(Source)].dbo.REMSEngagement e ON e.Id = q.EngagementId
LEFT JOIN [$(Source)].dbo.REMSEngagementGovernmentDetail g ON g.REMSEngagementId = e.Id AND g.Deleted = 0
LEFT JOIN dbo.Client_Contract cc ON cc.Id = g.Id
OUTER APPLY (SELECT TOP 1 x.RecipientEmail FROM [$(Source)].dbo.REMSFormEmailEvent x WHERE x.REMSFormId = f.Id AND x.EventType = N'Sent' ORDER BY x.OccurredOnUtc) se;

/* ------------------------------------------------------------------------------------------------
   11. Child rows with kept ids.
   ------------------------------------------------------------------------------------------------ */
INSERT INTO dbo.REMS_File (Id, TenantId, REMSId, MediaId, Kind, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT x.Id, x.TenantId, x.REMSId, x.MediaId, N'Attachment', x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSFiles x JOIN #req q ON q.Id = x.REMSId WHERE x.Deleted = 0;

INSERT INTO dbo.REMS_File (Id, TenantId, REMSId, MediaId, Kind, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT CONVERT(uniqueidentifier, HASHBYTES('MD5', N'acf:' + CONVERT(nvarchar(36), a.Id))), a.TenantId, q.Id, a.ClientAcceptanceFormMediaId, N'ClientAcceptanceForm',
       a.CreatedById, a.CreatedOnUtc, a.UpdatedById, a.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSEngagementAuditDetail a JOIN #req q ON q.EngagementId = a.REMSEngagementId
WHERE a.Deleted = 0 AND a.ClientAcceptanceFormMediaId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.REMS_File x WHERE x.REMSId = q.Id AND x.MediaId = a.ClientAcceptanceFormMediaId);

INSERT INTO dbo.REMS_File (Id, TenantId, REMSId, MediaId, Kind, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT CONVERT(uniqueidentifier, HASHBYTES('MD5', N'po:' + CONVERT(nvarchar(36), g.Id))), g.TenantId, q.Id, g.PurchaseOrderMediaId, N'PurchaseOrder',
       g.CreatedById, g.CreatedOnUtc, g.UpdatedById, g.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSEngagementGovernmentDetail g JOIN #req q ON q.EngagementId = g.REMSEngagementId
WHERE g.Deleted = 0 AND g.PurchaseOrderMediaId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.REMS_File x WHERE x.REMSId = q.Id AND x.MediaId = g.PurchaseOrderMediaId);

INSERT INTO dbo.REMS_EmailEvent (Id, TenantId, REMSId, EmailTemplateId, EventType, RecipientEmail, Subject, Body, ProviderMessageId, OccurredOnUtc, FailureReason,
                                 CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT x.Id, x.TenantId, q.Id, NULL, x.EventType, x.RecipientEmail, x.Subject, x.Body, x.ProviderMessageId, x.OccurredOnUtc, NULL,
       x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSFormEmailEvent x JOIN #req q ON q.FormId = x.REMSFormId;

INSERT INTO dbo.REMS_Approver (Id, TenantId, REMSId, UserId, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT x.Id, x.TenantId, q.Id, x.UserId, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSEngagementApprover x JOIN #req q ON q.EngagementId = x.REMSEngagementId WHERE x.Deleted = 0;

INSERT INTO dbo.REMS_CommissionSplit (Id, TenantId, REMSId, EmployeeId, CommissionPercentage, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT x.Id, x.TenantId, q.Id, x.EmployeeId, x.CommissionPercentage, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSEngagementCommissionSplit x JOIN #req q ON q.EngagementId = x.REMSEngagementId WHERE x.Deleted = 0;

INSERT INTO dbo.REMS_ApprovalTask (Id, TenantId, REMSId, RoundNumber, ApproverId, ApproverRole, Status, DecidedOnUtc, RejectionReason,
                                   CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT t.Id, t.TenantId, q.Id, rd.RoundNumber, t.ApproverId, t.ApproverRole, t.Status, t.DecidedOnUtc, t.RejectionReason,
       t.CreatedById, t.CreatedOnUtc, t.UpdatedById, t.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSApprovalTask t
JOIN [$(Source)].dbo.REMSApprovalRound rd ON rd.Id = t.REMSApprovalRoundId
JOIN #req q ON q.EngagementId = rd.REMSEngagementId
WHERE t.Deleted = 0;

INSERT INTO dbo.REMS_ApprovalChecklistItem (Id, TenantId, TaskId, DisplayOrder, Label, IsCompleted, CompletedOnUtc, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT i.Id, i.TenantId, i.REMSApprovalTaskId, i.DisplayOrder, i.Label, i.IsCompleted, i.CompletedOnUtc, i.CreatedById, i.CreatedOnUtc, i.UpdatedById, i.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSApprovalChecklistItem i JOIN dbo.REMS_ApprovalTask t ON t.Id = i.REMSApprovalTaskId WHERE i.Deleted = 0;

INSERT INTO dbo.REMS_PersonnelRate (Id, TenantId, REMSId, PersonnelLevelId, BillRatePerHour, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT x.Id, x.TenantId, q.Id, x.PersonnelLevelId, x.BillRatePerHour, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSEngagementPersonnelRate x
JOIN [$(Source)].dbo.REMSEngagementGovernmentDetail g ON g.Id = x.REMSEngagementGovernmentDetailId
JOIN #req q ON q.EngagementId = g.REMSEngagementId
WHERE x.Deleted = 0;

INSERT INTO dbo.REMS_MarketingMethod (Id, TenantId, REMSId, MarketingMethodId, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT x.Id, x.TenantId, q.Id, x.MarketingMethodId, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSEngagementMarketingMethod x JOIN #req q ON q.EngagementId = x.REMSEngagementId WHERE x.Deleted = 0;

INSERT INTO dbo.REMS_TaxForm (Id, TenantId, REMSId, TaxFormId, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT x.Id, x.TenantId, q.Id, x.TaxFormId, x.CreatedById, x.CreatedOnUtc, x.UpdatedById, x.UpdatedOnUtc, 0
FROM [$(Source)].dbo.REMSEngagementTaxForm x
JOIN [$(Source)].dbo.REMSEngagementTaxDetail td ON td.Id = x.REMSEngagementTaxDetailId
JOIN #req q ON q.EngagementId = td.REMSEngagementId
WHERE x.Deleted = 0;

-- Tax due dates: one row per kind. The stored snapshot already carries any override, so an override is
-- detected by comparing the editable column with the date the rule would give.
;WITH t AS (
    SELECT d.Id, d.TenantId, q.Id AS REMSId, d.UpdatedById, d.CreatedById, d.CreatedOnUtc, d.UpdatedOnUtc,
           COALESCE(d.FiscalYearEnd, TRY_CONVERT(date, JSON_VALUE(d.CalculatedDueDates, '$.fiscalYearEnd'))) AS Fye,
           d.OriginalDueDate, d.FirstExtensionDueDate,
           TRY_CONVERT(date, JSON_VALUE(d.CalculatedDueDates, '$.originalDueDate')) AS JsonOriginal,
           TRY_CONVERT(date, JSON_VALUE(d.CalculatedDueDates, '$.extendedDueDate')) AS JsonExtended
    FROM [$(Source)].dbo.REMSEngagementTaxDetail d JOIN #req q ON q.EngagementId = d.REMSEngagementId
    WHERE d.Deleted = 0
), calc AS (
    SELECT t.*,
           DATEFROMPARTS(YEAR(DATEADD(month, 4, DATEFROMPARTS(YEAR(Fye), MONTH(Fye), 1))), MONTH(DATEADD(month, 4, DATEFROMPARTS(YEAR(Fye), MONTH(Fye), 1))), 15) AS RuleOriginal
    FROM t WHERE Fye IS NOT NULL
), eff AS (
    SELECT c.*, COALESCE(c.OriginalDueDate, c.JsonOriginal, c.RuleOriginal) AS EffOriginal FROM calc c
), rows_ AS (
    SELECT Id, TenantId, REMSId, N'Original' AS Kind, EffOriginal AS DueOn,
           CASE WHEN OriginalDueDate IS NOT NULL AND OriginalDueDate <> RuleOriginal THEN 1 ELSE 0 END AS IsOverridden,
           UpdatedById, CreatedById, CreatedOnUtc, UpdatedOnUtc
    FROM eff
    UNION ALL
    SELECT Id, TenantId, REMSId, N'FirstExtension', COALESCE(FirstExtensionDueDate, JsonExtended, DATEADD(month, 6, EffOriginal)),
           CASE WHEN FirstExtensionDueDate IS NOT NULL AND FirstExtensionDueDate <> DATEADD(month, 6, EffOriginal) THEN 1 ELSE 0 END,
           UpdatedById, CreatedById, CreatedOnUtc, UpdatedOnUtc
    FROM eff
)
INSERT INTO dbo.REMS_TaxDueDate (Id, TenantId, REMSId, Kind, DueOn, IsOverridden, OverriddenByUserId, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT CONVERT(uniqueidentifier, HASHBYTES('MD5', N'due:' + CONVERT(nvarchar(36), Id) + N':' + Kind)), TenantId, REMSId, Kind, DueOn, IsOverridden,
       CASE WHEN IsOverridden = 1 THEN UpdatedById END, CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, 0
FROM rows_ WHERE DueOn IS NOT NULL;

/* ------------------------------------------------------------------------------------------------
   12. Status history: the activity feed already records every transition; send-backs add who and why.
   ------------------------------------------------------------------------------------------------ */
;WITH ev AS (
    SELECT a.Id, a.TenantId, a.EntityId AS REMSId, a.ActorId, a.CreatedOnUtc,
           CASE a.EventType
               WHEN N'RemsCreated' THEN N'draft'
               WHEN N'RemsFormSent' THEN N'awaiting_customer'
               WHEN N'RemsFormSubmitted' THEN N'customer_submitted'
               WHEN N'RemsSentBack' THEN N'returned_to_initiator'
               WHEN N'RemsReturnedToAdmin' THEN N'awaiting_admin_confirmation'
               WHEN N'RemsApprovalSent' THEN N'pending_approval'
               WHEN N'RemsApprovalResubmitted' THEN N'pending_approval'
               WHEN N'RemsRejected' THEN N'changes_requested'
               WHEN N'RemsFullyApproved' THEN N'approved'
           END AS Code
    FROM dbo.ActivityEvents a
    WHERE a.EntityType = @remsEntityType AND a.Deleted = 0 AND a.EntityId IN (SELECT Id FROM #req)
), mapped AS (
    SELECT ev.*, o.Id AS ToStatusId
    FROM ev
    CROSS APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.Status' AND o.Value = ev.Code ORDER BY CASE WHEN o.TenantId = ev.TenantId THEN 0 ELSE 1 END) o
    WHERE ev.Code IS NOT NULL
), seq AS (
    SELECT m.*, LAG(m.ToStatusId) OVER (PARTITION BY m.REMSId ORDER BY m.CreatedOnUtc, m.Id) AS FromStatusId FROM mapped m
)
INSERT INTO dbo.REMS_StatusHistory (Id, TenantId, REMSId, FromStatusId, ToStatusId, ChangedByUserId, ReturnedToUserId, ChangedOnUtc, Reason,
                                    CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT CONVERT(uniqueidentifier, HASHBYTES('MD5', N'hist:' + CONVERT(nvarchar(36), s.Id))), s.TenantId, s.REMSId, s.FromStatusId, s.ToStatusId,
       u.Id, sb.ReturnedToUserId, s.CreatedOnUtc, sb.Reason, u.Id, s.CreatedOnUtc, u.Id, s.CreatedOnUtc, 0
FROM seq s
LEFT JOIN dbo.Users u ON u.Id = s.ActorId
OUTER APPLY (SELECT TOP 1 x.ReturnedToUserId, x.Reason FROM [$(Source)].dbo.REMSSendBack x
             WHERE s.Code = N'returned_to_initiator' AND x.REMSId = s.REMSId AND ABS(DATEDIFF(second, x.CreatedOnUtc, s.CreatedOnUtc)) <= 120
             ORDER BY ABS(DATEDIFF(second, x.CreatedOnUtc, s.CreatedOnUtc))) sb;

-- Where the feed and the request disagree on the current status, one closing row says what was found.
INSERT INTO dbo.REMS_StatusHistory (Id, TenantId, REMSId, FromStatusId, ToStatusId, ChangedByUserId, ReturnedToUserId, ChangedOnUtc, Reason,
                                    CreatedById, CreatedOnUtc, UpdatedById, UpdatedOnUtc, Deleted)
SELECT CONVERT(uniqueidentifier, HASHBYTES('MD5', N'histfix:' + CONVERT(nvarchar(36), r.Id))), r.TenantId, r.Id, last_.ToStatusId, r.StatusId,
       r.UpdatedById, NULL, r.UpdatedOnUtc, N'Migration: status as found on the request',
       r.UpdatedById, r.UpdatedOnUtc, r.UpdatedById, r.UpdatedOnUtc, 0
FROM dbo.REMS r
OUTER APPLY (SELECT TOP 1 h.ToStatusId FROM dbo.REMS_StatusHistory h WHERE h.REMSId = r.Id ORDER BY h.ChangedOnUtc DESC, h.Id DESC) last_
WHERE last_.ToStatusId IS NULL OR last_.ToStatusId <> r.StatusId;

/* ------------------------------------------------------------------------------------------------
   13. Drafts of unsubmitted forms: what the client has typed so far lands on the master directly.
   ------------------------------------------------------------------------------------------------ */
-- Client fields, only where the master has nothing yet.
UPDATE c SET
    c.PrimaryEmail = COALESCE(c.PrimaryEmail, NULLIF(JSON_VALUE(d.j, '$.email'), N'')),
    c.PrimaryPhone = COALESCE(c.PrimaryPhone, NULLIF(JSON_VALUE(d.j, '$.mobileNumber'), N'')),
    c.EIN = COALESCE(c.EIN, NULLIF(JSON_VALUE(d.j, '$.ein'), N'')),
    c.ReferralSourceId = COALESCE(c.ReferralSourceId, rs.Id),
    c.ReferralSourceDetail = COALESCE(c.ReferralSourceDetail, NULLIF(JSON_VALUE(d.j, '$.referralSourceDetail'), N'')),
    c.LegalName = CASE WHEN c.PersonId IS NULL THEN COALESCE(c.LegalName, NULLIF(JSON_VALUE(d.j, '$.clientName'), N'')) ELSE c.LegalName END,
    c.UpdatedOnUtc = @now
FROM dbo.Client c
JOIN #draft d ON d.ClientId = c.Id
OUTER APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.ReferralSource' AND o.Value = NULLIF(JSON_VALUE(d.j, '$.referralSource'), N'') COLLATE DATABASE_DEFAULT
             ORDER BY CASE WHEN o.TenantId = d.TenantId THEN 0 ELSE 1 END) rs;

-- Addresses.
INSERT INTO dbo.Addresses (Id, AddressType, AddressLine1, AddressLine2, CountryCode, CountryName, StateCode, StateName, CityName, PostalCode,
                           Email, FirstName, LastName, PhoneNumber, Suffix, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT a.AddressId, CASE a.Kind WHEN N'physical' THEN N'Office' WHEN N'mailing' THEN N'Other' ELSE N'Billing' END, NULLIF(a.Street, N''), NULLIF(a.AddressLine2, N''), NULLIF(a.CountryCode, N''), NULLIF(a.CountryName, N''),
       NULLIF(a.StateCode, N''), NULLIF(a.StateName, N''), NULLIF(a.City, N''), NULLIF(a.Zip, N''),
       NULLIF(a.Email, N''), NULLIF(a.FirstName, N''), NULLIF(a.LastName, N''), NULLIF(a.Phone, N''), NULLIF(a.Suffix, N''), @now, @now, 0
FROM #draftAddr a
WHERE NOT EXISTS (SELECT 1 FROM dbo.Addresses x WHERE x.Id = a.AddressId);

INSERT INTO dbo.Client_Address (Id, TenantId, ClientId, AddressId, AddressTypeId, IsCurrent, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT a.ClientAddressId, a.TenantId, a.ClientId, a.AddressId, o.Id, 1, @now, @now, 0
FROM #draftAddr a
CROSS APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.AddressType' AND o.Value = a.Kind ORDER BY CASE WHEN o.TenantId = a.TenantId THEN 0 ELSE 1 END) o
WHERE NOT EXISTS (SELECT 1 FROM dbo.Client_Address x WHERE x.Id = a.ClientAddressId);

-- Role contacts: each filled role is a Person and a contact row.
IF OBJECT_ID('tempdb..#draftRole') IS NOT NULL DROP TABLE #draftRole;
SELECT d.REMSId, d.TenantId, d.ClientId, d.FormId, r.[key] AS JsonKey, rl.Code,
       CONVERT(uniqueidentifier, HASHBYTES('MD5', N'role:' + CONVERT(nvarchar(36), d.FormId) + N':' + r.[key])) AS PersonId,
       CONVERT(uniqueidentifier, HASHBYTES('MD5', N'rolec:' + CONVERT(nvarchar(36), d.FormId) + N':' + r.[key])) AS ContactId,
       NULLIF(JSON_VALUE(r.value, '$.prefix'), N'') AS Prefix, NULLIF(JSON_VALUE(r.value, '$.suffix'), N'') AS Suffix,
       NULLIF(JSON_VALUE(r.value, '$.firstName'), N'') AS FirstName, NULLIF(JSON_VALUE(r.value, '$.lastName'), N'') AS LastName,
       NULLIF(JSON_VALUE(r.value, '$.name'), N'') AS Name, NULLIF(JSON_VALUE(r.value, '$.email'), N'') AS Email, NULLIF(JSON_VALUE(r.value, '$.phone'), N'') AS Phone
INTO #draftRole
FROM #draft d
CROSS APPLY OPENJSON(d.j, '$.roles') r
JOIN #role rl ON rl.JsonKey = r.[key] COLLATE DATABASE_DEFAULT
WHERE r.type = 5
  AND (NULLIF(JSON_VALUE(r.value, '$.firstName'), N'') IS NOT NULL OR NULLIF(JSON_VALUE(r.value, '$.lastName'), N'') IS NOT NULL
       OR NULLIF(JSON_VALUE(r.value, '$.name'), N'') IS NOT NULL OR NULLIF(JSON_VALUE(r.value, '$.email'), N'') IS NOT NULL);

INSERT INTO dbo.Persons (Id, PersonCode, FirstName, LastName, DisplayName, Suffix, PrimaryEmail, MobileNumber, ProfileCompletionPercentage, IsProfileVerified, IsActive,
                         TenantId, SourceEntityType, SourceEntityId, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT x.PersonId, N'MIG-' + LEFT(CONVERT(nvarchar(36), x.PersonId), 12),
       COALESCE(x.FirstName, LEFT(x.Name, CHARINDEX(N' ', x.Name + N' ') - 1), N''),
       COALESCE(x.LastName, NULLIF(LTRIM(SUBSTRING(x.Name, CHARINDEX(N' ', x.Name + N' '), 200)), N''), N''),
       COALESCE(x.Name, LTRIM(RTRIM(ISNULL(x.FirstName, N'') + N' ' + ISNULL(x.LastName, N''))), x.Email),
       x.Suffix, x.Email, x.Phone, 0, 0, 1, x.TenantId, @remsEntityType, x.REMSId, @now, @now, 0
FROM #draftRole x
WHERE NOT EXISTS (SELECT 1 FROM dbo.Persons p WHERE p.Id = x.PersonId);

INSERT INTO dbo.Client_Contact (Id, TenantId, ClientId, PersonId, ContactRoleId, IsPrimary, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT x.ContactId, x.TenantId, x.ClientId, x.PersonId, o.Id, CASE WHEN x.JsonKey = N'primaryContact' THEN 1 ELSE 0 END, @now, @now, 0
FROM #draftRole x
CROSS APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.ContactRole' AND o.Value = x.Code ORDER BY CASE WHEN o.TenantId = x.TenantId THEN 0 ELSE 1 END) o
WHERE NOT EXISTS (SELECT 1 FROM dbo.Client_Contact cc WHERE cc.ClientId = x.ClientId AND cc.PersonId = x.PersonId AND cc.ContactRoleId = o.Id);

-- Additional individuals: a Person, a Prospect client with that Person's id, and the relationship.
IF OBJECT_ID('tempdb..#draftInd') IS NOT NULL DROP TABLE #draftInd;
SELECT d.REMSId, d.TenantId, d.ClientId, d.FormId,
       CONVERT(uniqueidentifier, HASHBYTES('MD5', N'ind:' + CONVERT(nvarchar(36), d.FormId) + N':' + ISNULL(i.SourceKey, CONVERT(nvarchar(10), i.Idx)))) AS PersonId,
       CONVERT(uniqueidentifier, HASHBYTES('MD5', N'indr:' + CONVERT(nvarchar(36), d.FormId) + N':' + ISNULL(i.SourceKey, CONVERT(nvarchar(10), i.Idx)))) AS RelationshipId,
       CONVERT(uniqueidentifier, HASHBYTES('MD5', N'indb:' + CONVERT(nvarchar(36), d.FormId) + N':' + ISNULL(i.SourceKey, CONVERT(nvarchar(10), i.Idx)))) AS BillingPersonId,
       CONVERT(uniqueidentifier, HASHBYTES('MD5', N'indbc:' + CONVERT(nvarchar(36), d.FormId) + N':' + ISNULL(i.SourceKey, CONVERT(nvarchar(10), i.Idx)))) AS BillingContactId,
       i.*
INTO #draftInd
FROM #draft d
CROSS APPLY (
    SELECT CONVERT(int, o.[key]) AS Idx, x.*
    FROM OPENJSON(d.j, '$.additionalIndividuals') o
    CROSS APPLY OPENJSON(o.value) WITH (
        SourceKey nvarchar(64) '$.sourceKey', Type nvarchar(32) '$.type', FilingType nvarchar(32) '$.filingType',
        FirstName nvarchar(100) '$.firstName', LastName nvarchar(100) '$.lastName', Suffix nvarchar(20) '$.suffix',
        Email nvarchar(256) '$.email', Phone nvarchar(32) '$.phone', IsMinor bit '$.isMinor', BillingPreference nvarchar(32) '$.billingPreference',
        BillingFirstName nvarchar(100) '$.billingFirstName', BillingLastName nvarchar(100) '$.billingLastName') x
) i
WHERE NULLIF(i.FirstName, N'') IS NOT NULL OR NULLIF(i.LastName, N'') IS NOT NULL OR NULLIF(i.Email, N'') IS NOT NULL;

INSERT INTO dbo.Persons (Id, PersonCode, FirstName, LastName, DisplayName, Suffix, PrimaryEmail, MobileNumber, ProfileCompletionPercentage, IsProfileVerified, IsActive,
                         TenantId, SourceEntityType, SourceEntityId, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT x.PersonId, N'MIG-' + LEFT(CONVERT(nvarchar(36), x.PersonId), 12), ISNULL(x.FirstName, N''), ISNULL(x.LastName, N''),
       LTRIM(RTRIM(ISNULL(x.FirstName, N'') + N' ' + ISNULL(x.LastName, N''))), NULLIF(x.Suffix, N''), NULLIF(x.Email, N''), NULLIF(x.Phone, N''),
       0, 0, 1, x.TenantId, @remsEntityType, x.REMSId, @now, @now, 0
FROM #draftInd x
WHERE NOT EXISTS (SELECT 1 FROM dbo.Persons p WHERE p.Id = x.PersonId);

INSERT INTO dbo.Client (Id, TenantId, ClientNumber, Status, PersonId, EntityTypeId, PrimaryEmail, PrimaryPhone, MaconomySyncStatus, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT x.PersonId, x.TenantId, N'TMP-' + CONVERT(nvarchar(36), x.PersonId), N'Prospect', x.PersonId, ind.Id, NULLIF(x.Email, N''), NULLIF(x.Phone, N''), N'NotSynced', @now, @now, 0
FROM #draftInd x
OUTER APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.EntityType' AND o.Value = N'individual' ORDER BY CASE WHEN o.TenantId = x.TenantId THEN 0 ELSE 1 END) ind
WHERE NOT EXISTS (SELECT 1 FROM dbo.Client c WHERE c.Id = x.PersonId);

INSERT INTO dbo.Client_Relationship (Id, TenantId, ClientId, RelatedClientId, RelationTypeId, FilingTypeId, BillingPreferenceId, IsMinor, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT x.RelationshipId, x.TenantId, x.ClientId, x.PersonId, rt.Id, ft.Id, bp.Id, ISNULL(x.IsMinor, 0), @now, @now, 0
FROM #draftInd x
CROSS APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.RelationType' AND o.Value = ISNULL(NULLIF(LOWER(x.Type), N''), N'other') COLLATE DATABASE_DEFAULT ORDER BY CASE WHEN o.TenantId = x.TenantId THEN 0 ELSE 1 END) rt
OUTER APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.FilingType' AND o.Value = LOWER(x.FilingType) COLLATE DATABASE_DEFAULT ORDER BY CASE WHEN o.TenantId = x.TenantId THEN 0 ELSE 1 END) ft
OUTER APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.BillingPreference' AND o.Value = LOWER(x.BillingPreference) COLLATE DATABASE_DEFAULT ORDER BY CASE WHEN o.TenantId = x.TenantId THEN 0 ELSE 1 END) bp
WHERE x.ClientId <> x.PersonId
  AND NOT EXISTS (SELECT 1 FROM dbo.Client_Relationship r WHERE r.ClientId = x.ClientId AND r.RelatedClientId = x.PersonId);

INSERT INTO dbo.Persons (Id, PersonCode, FirstName, LastName, DisplayName, ProfileCompletionPercentage, IsProfileVerified, IsActive,
                         TenantId, SourceEntityType, SourceEntityId, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT x.BillingPersonId, N'MIG-' + LEFT(CONVERT(nvarchar(36), x.BillingPersonId), 12), ISNULL(x.BillingFirstName, N''), ISNULL(x.BillingLastName, N''),
       LTRIM(RTRIM(ISNULL(x.BillingFirstName, N'') + N' ' + ISNULL(x.BillingLastName, N''))), 0, 0, 1, x.TenantId, @remsEntityType, x.REMSId, @now, @now, 0
FROM #draftInd x
WHERE (NULLIF(x.BillingFirstName, N'') IS NOT NULL OR NULLIF(x.BillingLastName, N'') IS NOT NULL)
  AND NOT EXISTS (SELECT 1 FROM dbo.Persons p WHERE p.Id = x.BillingPersonId);

INSERT INTO dbo.Client_Contact (Id, TenantId, ClientId, PersonId, ContactRoleId, IsPrimary, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT x.BillingContactId, x.TenantId, x.PersonId, x.BillingPersonId, o.Id, 0, @now, @now, 0
FROM #draftInd x
CROSS APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.ContactRole' AND o.Value = N'billing_contact' ORDER BY CASE WHEN o.TenantId = x.TenantId THEN 0 ELSE 1 END) o
WHERE (NULLIF(x.BillingFirstName, N'') IS NOT NULL OR NULLIF(x.BillingLastName, N'') IS NOT NULL)
  AND NOT EXISTS (SELECT 1 FROM dbo.Client_Contact cc WHERE cc.Id = x.BillingContactId);

-- Related entities: a Prospect client and the relationship.
IF OBJECT_ID('tempdb..#draftEnt') IS NOT NULL DROP TABLE #draftEnt;
SELECT d.REMSId, d.TenantId, d.ClientId, d.FormId,
       CONVERT(uniqueidentifier, HASHBYTES('MD5', N'rente:' + CONVERT(nvarchar(36), d.FormId) + N':' + ISNULL(e.SourceKey, CONVERT(nvarchar(10), e.Idx)))) AS RelatedClientId,
       CONVERT(uniqueidentifier, HASHBYTES('MD5', N'rentr:' + CONVERT(nvarchar(36), d.FormId) + N':' + ISNULL(e.SourceKey, CONVERT(nvarchar(10), e.Idx)))) AS RelationshipId,
       e.*
INTO #draftEnt
FROM #draft d
CROSS APPLY (
    SELECT CONVERT(int, o.[key]) AS Idx, x.*
    FROM OPENJSON(d.j, '$.relatedEntities') o
    CROSS APPLY OPENJSON(o.value) WITH (SourceKey nvarchar(64) '$.sourceKey', FullName nvarchar(200) '$.fullName', EmailAddress nvarchar(256) '$.emailAddress', PhoneNumber nvarchar(32) '$.phoneNumber') x
) e
WHERE NULLIF(e.FullName, N'') IS NOT NULL;

INSERT INTO dbo.Client (Id, TenantId, ClientNumber, Status, LegalName, PrimaryEmail, PrimaryPhone, MaconomySyncStatus, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT x.RelatedClientId, x.TenantId, N'TMP-' + CONVERT(nvarchar(36), x.RelatedClientId), N'Prospect', x.FullName, NULLIF(x.EmailAddress, N''), NULLIF(x.PhoneNumber, N''), N'NotSynced', @now, @now, 0
FROM #draftEnt x
WHERE NOT EXISTS (SELECT 1 FROM dbo.Client c WHERE c.Id = x.RelatedClientId);

INSERT INTO dbo.Client_Relationship (Id, TenantId, ClientId, RelatedClientId, RelationTypeId, IsMinor, CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT x.RelationshipId, x.TenantId, x.ClientId, x.RelatedClientId, rt.Id, 0, @now, @now, 0
FROM #draftEnt x
CROSS APPLY (SELECT TOP 1 o.Id FROM #opt o WHERE o.[Key] = N'REMS.RelationType' AND o.Value = N'related_entity' ORDER BY CASE WHEN o.TenantId = x.TenantId THEN 0 ELSE 1 END) rt
WHERE NOT EXISTS (SELECT 1 FROM dbo.Client_Relationship r WHERE r.ClientId = x.ClientId AND r.RelatedClientId = x.RelatedClientId);

-- Government contract fields typed on the form.
INSERT INTO dbo.Client_Contract (Id, TenantId, ClientId, ContractStartDate, ContractEndDate, OriginalTerm, RenewalTerms, PurchaseOrderStartDate, PurchaseOrderEndDate, IsCurrent,
                                 CreatedOnUtc, UpdatedOnUtc, Deleted)
SELECT CONVERT(uniqueidentifier, HASHBYTES('MD5', N'contract:' + CONVERT(nvarchar(36), d.FormId))), d.TenantId, d.ClientId,
       TRY_CONVERT(date, JSON_VALUE(d.j, '$.contractStartDate')), TRY_CONVERT(date, JSON_VALUE(d.j, '$.contractEndDate')),
       NULLIF(JSON_VALUE(d.j, '$.originalTerm'), N''), NULLIF(JSON_VALUE(d.j, '$.renewalTerms'), N''),
       TRY_CONVERT(date, JSON_VALUE(d.j, '$.poStartDate')), TRY_CONVERT(date, JSON_VALUE(d.j, '$.poEndDate')),
       CASE WHEN EXISTS (SELECT 1 FROM dbo.Client_Contract x WHERE x.ClientId = d.ClientId AND x.IsCurrent = 1) THEN 0 ELSE 1 END,
       @now, @now, 0
FROM #draft d
WHERE (TRY_CONVERT(date, JSON_VALUE(d.j, '$.contractStartDate')) IS NOT NULL OR TRY_CONVERT(date, JSON_VALUE(d.j, '$.contractEndDate')) IS NOT NULL
       OR NULLIF(JSON_VALUE(d.j, '$.originalTerm'), N'') IS NOT NULL OR NULLIF(JSON_VALUE(d.j, '$.renewalTerms'), N'') IS NOT NULL
       OR TRY_CONVERT(date, JSON_VALUE(d.j, '$.poStartDate')) IS NOT NULL OR TRY_CONVERT(date, JSON_VALUE(d.j, '$.poEndDate')) IS NOT NULL)
  AND NOT EXISTS (SELECT 1 FROM dbo.Client_Contract x WHERE x.Id = CONVERT(uniqueidentifier, HASHBYTES('MD5', N'contract:' + CONVERT(nvarchar(36), d.FormId))));

UPDATE r SET r.ContractId = cc.Id
FROM dbo.REMS r
JOIN #draft d ON d.REMSId = r.Id
JOIN dbo.Client_Contract cc ON cc.Id = CONVERT(uniqueidentifier, HASHBYTES('MD5', N'contract:' + CONVERT(nvarchar(36), d.FormId)))
WHERE r.ContractId IS NULL;

/* ------------------------------------------------------------------------------------------------
   14. Client numbers for related parties, then the sequences restart above everything in use.
   ------------------------------------------------------------------------------------------------ */
;WITH numbered AS (
    SELECT c.Id, ROW_NUMBER() OVER (ORDER BY c.CreatedOnUtc, c.Id) + ISNULL((SELECT MAX(TRY_CONVERT(int, SUBSTRING(ClientNumber, 4, 20))) FROM dbo.Client WHERE ClientNumber LIKE N'CL-%'), 0) AS n
    FROM dbo.Client c WHERE c.ClientNumber LIKE N'TMP-%'
)
UPDATE c SET c.ClientNumber = N'CL-' + CONVERT(nvarchar(20), n.n)
FROM dbo.Client c JOIN numbered n ON n.Id = c.Id;

DECLARE @maxRems int = ISNULL((SELECT MAX(TRY_CONVERT(int, SUBSTRING(RemsNumber, 6, 20))) FROM dbo.REMS WHERE RemsNumber LIKE N'REMS-%'), 0);
DECLARE @maxClient int = ISNULL((SELECT MAX(TRY_CONVERT(int, SUBSTRING(ClientNumber, 4, 20))) FROM dbo.Client WHERE ClientNumber LIKE N'CL-%'), 0);
DECLARE @sql nvarchar(400);
SET @sql = N'ALTER SEQUENCE dbo.seq_RemsNumber RESTART WITH ' + CONVERT(nvarchar(20), @maxRems + 1) + N';';
EXEC sp_executesql @sql;
SET @sql = N'ALTER SEQUENCE dbo.seq_ClientNumber RESTART WITH ' + CONVERT(nvarchar(20), @maxClient + 1) + N';';
EXEC sp_executesql @sql;

/* ------------------------------------------------------------------------------------------------
   15. Verification. Any failed check throws, which rolls everything back.
   ------------------------------------------------------------------------------------------------ */
DECLARE @errors nvarchar(max) = N'';
IF EXISTS (SELECT 1 FROM [$(Source)].dbo.REMS r WHERE r.Deleted = 0 AND NOT EXISTS (SELECT 1 FROM dbo.REMS x WHERE x.Id = r.Id))
    SET @errors += N'A live request did not make it into dbo.REMS. ';
IF EXISTS (SELECT 1 FROM dbo.REMS r WHERE NOT EXISTS (SELECT 1 FROM dbo.Client c WHERE c.Id = r.ClientId))
    SET @errors += N'A request points at a client that does not exist. ';
IF (SELECT COUNT(*) FROM [$(Source)].dbo.REMSApprovalTask WHERE Deleted = 0) <> (SELECT COUNT(*) FROM dbo.REMS_ApprovalTask)
    SET @errors += N'Approval task count differs. ';
IF (SELECT COUNT(*) FROM [$(Source)].dbo.REMSApprovalChecklistItem WHERE Deleted = 0) <> (SELECT COUNT(*) FROM dbo.REMS_ApprovalChecklistItem)
    SET @errors += N'Checklist item count differs. ';
IF (SELECT COUNT(*) FROM [$(Source)].dbo.REMSFormEmailEvent) <> (SELECT COUNT(*) FROM dbo.REMS_EmailEvent)
    SET @errors += N'Email event count differs. ';
IF EXISTS (SELECT 1 FROM dbo.REMS r OUTER APPLY (SELECT TOP 1 h.ToStatusId FROM dbo.REMS_StatusHistory h WHERE h.REMSId = r.Id ORDER BY h.ChangedOnUtc DESC, h.Id DESC) l WHERE l.ToStatusId <> r.StatusId OR l.ToStatusId IS NULL)
    SET @errors += N'A request''s latest history row does not match its status. ';
IF EXISTS (SELECT 1 FROM dbo.Client WHERE ClientNumber = N'' OR ClientNumber IS NULL)
    SET @errors += N'A client has no number. ';
IF EXISTS (SELECT 1 FROM dbo.Client GROUP BY TenantId, ClientNumber HAVING COUNT(*) > 1)
    SET @errors += N'Duplicate client numbers. ';
IF @errors <> N'' THROW 50002, @errors, 1;

SELECT 'Client' AS [Table], COUNT(*) AS [Rows] FROM dbo.Client
UNION ALL SELECT 'Client_Address', COUNT(*) FROM dbo.Client_Address
UNION ALL SELECT 'Client_Contact', COUNT(*) FROM dbo.Client_Contact
UNION ALL SELECT 'Client_Relationship', COUNT(*) FROM dbo.Client_Relationship
UNION ALL SELECT 'Client_Contract', COUNT(*) FROM dbo.Client_Contract
UNION ALL SELECT 'REMS', COUNT(*) FROM dbo.REMS
UNION ALL SELECT 'REMS_File', COUNT(*) FROM dbo.REMS_File
UNION ALL SELECT 'REMS_EmailEvent', COUNT(*) FROM dbo.REMS_EmailEvent
UNION ALL SELECT 'REMS_StatusHistory', COUNT(*) FROM dbo.REMS_StatusHistory
UNION ALL SELECT 'REMS_Approver', COUNT(*) FROM dbo.REMS_Approver
UNION ALL SELECT 'REMS_CommissionSplit', COUNT(*) FROM dbo.REMS_CommissionSplit
UNION ALL SELECT 'REMS_ApprovalTask', COUNT(*) FROM dbo.REMS_ApprovalTask
UNION ALL SELECT 'REMS_ApprovalChecklistItem', COUNT(*) FROM dbo.REMS_ApprovalChecklistItem
UNION ALL SELECT 'REMS_PersonnelRate', COUNT(*) FROM dbo.REMS_PersonnelRate
UNION ALL SELECT 'REMS_MarketingMethod', COUNT(*) FROM dbo.REMS_MarketingMethod
UNION ALL SELECT 'REMS_TaxForm', COUNT(*) FROM dbo.REMS_TaxForm
UNION ALL SELECT 'REMS_TaxDueDate', COUNT(*) FROM dbo.REMS_TaxDueDate
UNION ALL SELECT 'REMS_ChecklistTemplate', COUNT(*) FROM dbo.REMS_ChecklistTemplate
UNION ALL SELECT 'Persons created (MIG-)', COUNT(*) FROM dbo.Persons WHERE PersonCode LIKE N'MIG-%'
UNION ALL SELECT 'Addresses created', COUNT(*) FROM dbo.Addresses WHERE Id IN (SELECT AddressId FROM #draftAddr)
UNION ALL SELECT 'Persons carried (referenced by REMS)', COUNT(*) FROM dbo.Persons p JOIN #needPerson n ON n.Id = p.Id
UNION ALL SELECT 'Addresses carried', COUNT(*) FROM dbo.Addresses a JOIN #needAddress n ON n.Id = a.Id
UNION ALL SELECT 'Media carried', COUNT(*) FROM dbo.Media m JOIN #needMedia n ON n.Id = m.Id
UNION ALL SELECT 'REMS_Delegation', COUNT(*) FROM dbo.REMS_Delegation
UNION ALL SELECT 'REMS_DepartmentDirector', COUNT(*) FROM dbo.REMS_DepartmentDirector
UNION ALL SELECT 'ActivityEvents (requests)', COUNT(*) FROM dbo.ActivityEvents x JOIN #needRems r ON r.Id = x.EntityId WHERE x.EntityType = @remsEntityType
UNION ALL SELECT 'Notifications (requests)', COUNT(*) FROM dbo.Notifications x JOIN #needRems r ON r.Id = x.EntityId WHERE x.EntityType = @remsEntityType
UNION ALL SELECT 'ConversationMessages (requests)', COUNT(*) FROM dbo.ConversationMessages x JOIN #needRems r ON r.Id = x.EntityId WHERE x.EntityType = @remsEntityType
UNION ALL SELECT 'seq_RemsNumber next', @maxRems + 1
UNION ALL SELECT 'seq_ClientNumber next', @maxClient + 1;

COMMIT;
