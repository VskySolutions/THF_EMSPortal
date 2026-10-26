-- Generated with: dotnet ef migrations script 20261023103102_RemsDataModelV2 20261023155724_RemsLegacyModelRemoved --idempotent
-- One statement rewritten by hand: EF Core 9.0.0 emits the ClientDisplayName computed column as EXEC(CONCAT(...)),
-- which SQL Server rejects; the plain ALTER TABLE below is the same statement.
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    UPDATE [Persons] SET [Deleted] = 1, [DeletedOnUtc] = SYSUTCDATETIME() WHERE [PartyType] = 1 AND [Deleted] = 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    ALTER TABLE [REMS] DROP CONSTRAINT [FK_REMS_OptionSetItems_StatusId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    ALTER TABLE [REMS] DROP CONSTRAINT [FK_REMS_OptionSetItems_TypeId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    ALTER TABLE [REMS] DROP CONSTRAINT [FK_REMS_Tenants_TenantId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    ALTER TABLE [REMS] DROP CONSTRAINT [FK_REMS_Users_AdminAssignedToId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    ALTER TABLE [REMS] DROP CONSTRAINT [FK_REMS_Users_CSEId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    ALTER TABLE [REMS] DROP CONSTRAINT [FK_REMS_Users_OnBehalfOfUserId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSAdditionalEntity];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSAdditionalIndividual];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSApprovalChecklistItem];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSEngagementApprover];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSEngagementAuditDetail];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSEngagementCommissionSplit];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSEngagementMarketingMethod];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSEngagementPersonnelRate];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSEngagementTaxForm];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSEntityAddress];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSEntityContact];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSFiles];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSFormDraft];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSFormEmailEvent];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSMaconomyCustomerSync];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSSendBack];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSApprovalTask];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSEngagementGovernmentDetail];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSEngagementTaxDetail];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSEntity];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSApprovalRound];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSClient];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSEngagement];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSFormSubmission];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMSForm];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP TABLE [legacy].[REMS];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP INDEX [IX_Persons_TenantId_MaconomyCustomerNumber] ON [Persons];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DROP INDEX [IX_Persons_TenantId_PartyType_LastName] ON [Persons];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Persons]') AND [c].[name] = N'ClientDisplayName');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Persons] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [Persons] DROP COLUMN [ClientDisplayName];
    ALTER TABLE [Persons] ADD [ClientDisplayName] AS COALESCE(NULLIF(LTRIM(RTRIM(
        ISNULL(NULLIF(LTRIM(RTRIM([LastName])), N''), N'')
            + CASE WHEN NULLIF(LTRIM(RTRIM([FirstName])), N'') IS NULL THEN N''
                   ELSE N' ' + LTRIM(RTRIM([FirstName])) END
            + CASE WHEN NULLIF(LTRIM(RTRIM([Suffix])), N'') IS NULL THEN N''
                   ELSE N' ' + LTRIM(RTRIM([Suffix])) END)), N''), [DisplayName]) PERSISTED;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Persons]') AND [c].[name] = N'CorporateName');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Persons] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [Persons] DROP COLUMN [CorporateName];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Persons]') AND [c].[name] = N'MaconomyCustomerNumber');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Persons] DROP CONSTRAINT [' + @var2 + '];');
    ALTER TABLE [Persons] DROP COLUMN [MaconomyCustomerNumber];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    DECLARE @var3 sysname;
    SELECT @var3 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Persons]') AND [c].[name] = N'PartyType');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [Persons] DROP CONSTRAINT [' + @var3 + '];');
    ALTER TABLE [Persons] DROP COLUMN [PartyType];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    CREATE INDEX [IX_Persons_TenantId_LastName] ON [Persons] ([TenantId], [LastName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    IF SCHEMA_ID(N'legacy') IS NOT NULL EXEC(N'DROP SCHEMA [legacy]');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023155724_RemsLegacyModelRemoved'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023155724_RemsLegacyModelRemoved', N'9.0.0');
END;

COMMIT;
GO

