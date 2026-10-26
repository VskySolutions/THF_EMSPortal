BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022112337_AddRemsMaconomyCustomerSync'
)
BEGIN
    ALTER TABLE [MaconomyConnections] ADD [CustomerSyncChangedById] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022112337_AddRemsMaconomyCustomerSync'
)
BEGIN
    ALTER TABLE [MaconomyConnections] ADD [CustomerSyncChangedOnUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022112337_AddRemsMaconomyCustomerSync'
)
BEGIN
    ALTER TABLE [MaconomyConnections] ADD [CustomerSyncEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022112337_AddRemsMaconomyCustomerSync'
)
BEGIN
    CREATE TABLE [REMSMaconomyCustomerSync] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [REMSId] uniqueidentifier NOT NULL,
        [ClientPersonId] uniqueidentifier NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [CustomerNumber] nvarchar(64) NULL,
        [Attempts] int NOT NULL,
        [LastAttemptOnUtc] datetime2 NULL,
        [SyncedOnUtc] datetime2 NULL,
        [FailureReason] nvarchar(2000) NULL,
        [PayloadJson] nvarchar(max) NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_REMSMaconomyCustomerSync] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_REMSMaconomyCustomerSync_Persons_ClientPersonId] FOREIGN KEY ([ClientPersonId]) REFERENCES [Persons] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMSMaconomyCustomerSync_REMS_REMSId] FOREIGN KEY ([REMSId]) REFERENCES [REMS] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMSMaconomyCustomerSync_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022112337_AddRemsMaconomyCustomerSync'
)
BEGIN
    CREATE INDEX [IX_REMSMaconomyCustomerSync_ClientPersonId] ON [REMSMaconomyCustomerSync] ([ClientPersonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022112337_AddRemsMaconomyCustomerSync'
)
BEGIN
    CREATE INDEX [IX_REMSMaconomyCustomerSync_REMSId] ON [REMSMaconomyCustomerSync] ([REMSId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022112337_AddRemsMaconomyCustomerSync'
)
BEGIN
    CREATE INDEX [IX_REMSMaconomyCustomerSync_TenantId] ON [REMSMaconomyCustomerSync] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022112337_AddRemsMaconomyCustomerSync'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_REMSMaconomyCustomerSync_TenantId_REMSId] ON [REMSMaconomyCustomerSync] ([TenantId], [REMSId]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022112337_AddRemsMaconomyCustomerSync'
)
BEGIN
    CREATE INDEX [IX_REMSMaconomyCustomerSync_TenantId_Status] ON [REMSMaconomyCustomerSync] ([TenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022112337_AddRemsMaconomyCustomerSync'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261022112337_AddRemsMaconomyCustomerSync', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER TABLE [REMSDelegation] DROP CONSTRAINT [FK_REMSDelegation_Tenants_TenantId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER TABLE [REMSDelegation] DROP CONSTRAINT [FK_REMSDelegation_Users_DelegateUserId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER TABLE [REMSDelegation] DROP CONSTRAINT [FK_REMSDelegation_Users_PrincipalUserId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER TABLE [REMSDelegation] DROP CONSTRAINT [PK_REMSDelegation];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    IF SCHEMA_ID(N'legacy') IS NULL EXEC(N'CREATE SCHEMA [legacy];');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSSendBack];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSMaconomyCustomerSync];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSFormSubmission];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSFormEmailEvent];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSFormDraft];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSForm];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSFiles];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSEntityContact];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSEntityAddress];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSEntity];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSEngagementTaxForm];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSEngagementTaxDetail];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSEngagementPersonnelRate];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSEngagementMarketingMethod];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSEngagementGovernmentDetail];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSEngagementCommissionSplit];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSEngagementAuditDetail];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSEngagementApprover];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSEngagement];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSClient];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSApprovalTask];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSApprovalRound];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSApprovalChecklistItem];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSAdditionalIndividual];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMSAdditionalEntity];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER SCHEMA [legacy] TRANSFER [REMS];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    EXEC sp_rename N'[REMSDelegation]', N'REMS_Delegation', 'OBJECT';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    EXEC sp_rename N'[REMS_Delegation].[CanSend]', N'IsSendAllowed', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    EXEC sp_rename N'[REMS_Delegation].[CanPrepare]', N'IsPrepareAllowed', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    EXEC sp_rename N'[REMS_Delegation].[IX_REMSDelegation_TenantId_PrincipalUserId_DelegateUserId]', N'IX_REMS_Delegation_TenantId_PrincipalUserId_DelegateUserId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    EXEC sp_rename N'[REMS_Delegation].[IX_REMSDelegation_TenantId_DelegateUserId]', N'IX_REMS_Delegation_TenantId_DelegateUserId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    EXEC sp_rename N'[REMS_Delegation].[IX_REMSDelegation_TenantId]', N'IX_REMS_Delegation_TenantId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    EXEC sp_rename N'[REMS_Delegation].[IX_REMSDelegation_PrincipalUserId]', N'IX_REMS_Delegation_PrincipalUserId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    EXEC sp_rename N'[REMS_Delegation].[IX_REMSDelegation_DelegateUserId]', N'IX_REMS_Delegation_DelegateUserId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER TABLE [REMS_Delegation] ADD CONSTRAINT [PK_REMS_Delegation] PRIMARY KEY ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER TABLE [REMS_Delegation] ADD CONSTRAINT [FK_REMS_Delegation_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER TABLE [REMS_Delegation] ADD CONSTRAINT [FK_REMS_Delegation_Users_DelegateUserId] FOREIGN KEY ([DelegateUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    ALTER TABLE [REMS_Delegation] ADD CONSTRAINT [FK_REMS_Delegation_Users_PrincipalUserId] FOREIGN KEY ([PrincipalUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103052_RemsLegacySchema'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023103052_RemsLegacySchema', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [RemsDepartmentDirector] DROP CONSTRAINT [FK_RemsDepartmentDirector_OptionSetItems_DepartmentId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [RemsDepartmentDirector] DROP CONSTRAINT [FK_RemsDepartmentDirector_RemsSettings_RemsSettingsId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [RemsDepartmentDirector] DROP CONSTRAINT [FK_RemsDepartmentDirector_Tenants_TenantId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [RemsDepartmentDirector] DROP CONSTRAINT [FK_RemsDepartmentDirector_Users_DirectorUserId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [RemsSettings] DROP CONSTRAINT [FK_RemsSettings_Tenants_TenantId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [RemsSettings] DROP CONSTRAINT [PK_RemsSettings];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [RemsDepartmentDirector] DROP CONSTRAINT [PK_RemsDepartmentDirector];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC sp_rename N'[RemsSettings]', N'REMS_Settings', 'OBJECT';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC sp_rename N'[RemsDepartmentDirector]', N'REMS_DepartmentDirector', 'OBJECT';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC sp_rename N'[REMS_Settings].[IX_RemsSettings_TenantId]', N'IX_REMS_Settings_TenantId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC sp_rename N'[REMS_DepartmentDirector].[RemsSettingsId]', N'SettingsId', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC sp_rename N'[REMS_DepartmentDirector].[IX_RemsDepartmentDirector_TenantId_DepartmentId]', N'IX_REMS_DepartmentDirector_TenantId_DepartmentId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC sp_rename N'[REMS_DepartmentDirector].[IX_RemsDepartmentDirector_TenantId]', N'IX_REMS_DepartmentDirector_TenantId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC sp_rename N'[REMS_DepartmentDirector].[IX_RemsDepartmentDirector_RemsSettingsId]', N'IX_REMS_DepartmentDirector_SettingsId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC sp_rename N'[REMS_DepartmentDirector].[IX_RemsDepartmentDirector_DirectorUserId]', N'IX_REMS_DepartmentDirector_DirectorUserId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC sp_rename N'[REMS_DepartmentDirector].[IX_RemsDepartmentDirector_DepartmentId]', N'IX_REMS_DepartmentDirector_DepartmentId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE SEQUENCE [seq_ClientNumber] AS int START WITH 1 INCREMENT BY 1 NO CYCLE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE SEQUENCE [seq_RemsNumber] AS int START WITH 1 INCREMENT BY 1 NO CYCLE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [REMS_Settings] ADD [DeclineThreshold] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [REMS_Settings] ADD [IsStaticRouting] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [REMS_Settings] ADD [TaxFeeCeilingWithoutShareholder] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [REMS_Settings] ADD CONSTRAINT [PK_REMS_Settings] PRIMARY KEY ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [REMS_DepartmentDirector] ADD CONSTRAINT [PK_REMS_DepartmentDirector] PRIMARY KEY ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [Client] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ClientNumber] nvarchar(64) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [PersonId] uniqueidentifier NULL,
        [LegalName] nvarchar(200) NULL,
        [EntityTypeId] uniqueidentifier NULL,
        [IndustryId] uniqueidentifier NULL,
        [EIN] nvarchar(32) NULL,
        [FiscalYearEnd] date NULL,
        [PrimaryEmail] nvarchar(256) NULL,
        [PrimaryPhone] nvarchar(32) NULL,
        [ReferralSourceId] uniqueidentifier NULL,
        [ReferralSourceDetail] nvarchar(500) NULL,
        [MaconomyCustomerNumber] nvarchar(64) NULL,
        [MaconomySyncStatus] nvarchar(20) NOT NULL,
        [MaconomySyncedOnUtc] datetime2 NULL,
        [MaconomySyncError] nvarchar(1000) NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_Client] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Client_Party] CHECK ([PersonId] IS NOT NULL OR [LegalName] IS NOT NULL),
        CONSTRAINT [FK_Client_OptionSetItems_EntityTypeId] FOREIGN KEY ([EntityTypeId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_OptionSetItems_IndustryId] FOREIGN KEY ([IndustryId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_OptionSetItems_ReferralSourceId] FOREIGN KEY ([ReferralSourceId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Persons_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [Persons] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [REMS_ChecklistTemplate] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [SettingsId] uniqueidentifier NOT NULL,
        [ApproverRole] nvarchar(20) NOT NULL,
        [DisplayOrder] int NOT NULL,
        [Label] nvarchar(200) NOT NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_REMS_ChecklistTemplate] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_REMS_ChecklistTemplate_REMS_Settings_SettingsId] FOREIGN KEY ([SettingsId]) REFERENCES [REMS_Settings] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_ChecklistTemplate_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [Client_Address] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ClientId] uniqueidentifier NOT NULL,
        [AddressId] uniqueidentifier NOT NULL,
        [AddressTypeId] uniqueidentifier NOT NULL,
        [IsCurrent] bit NOT NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_Client_Address] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Client_Address_Addresses_AddressId] FOREIGN KEY ([AddressId]) REFERENCES [Addresses] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Address_Client_ClientId] FOREIGN KEY ([ClientId]) REFERENCES [Client] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Address_OptionSetItems_AddressTypeId] FOREIGN KEY ([AddressTypeId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Address_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [Client_Contact] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ClientId] uniqueidentifier NOT NULL,
        [PersonId] uniqueidentifier NOT NULL,
        [ContactRoleId] uniqueidentifier NOT NULL,
        [IsPrimary] bit NOT NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_Client_Contact] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Client_Contact_Client_ClientId] FOREIGN KEY ([ClientId]) REFERENCES [Client] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Contact_OptionSetItems_ContactRoleId] FOREIGN KEY ([ContactRoleId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Contact_Persons_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [Persons] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Contact_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [Client_Contract] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ClientId] uniqueidentifier NOT NULL,
        [ContractStartDate] date NULL,
        [ContractEndDate] date NULL,
        [OriginalTerm] nvarchar(500) NULL,
        [RenewalTerms] nvarchar(500) NULL,
        [PurchaseOrderStartDate] date NULL,
        [PurchaseOrderEndDate] date NULL,
        [IsCurrent] bit NOT NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_Client_Contract] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Client_Contract_Client_ClientId] FOREIGN KEY ([ClientId]) REFERENCES [Client] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Contract_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [Client_Relationship] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [ClientId] uniqueidentifier NOT NULL,
        [RelatedClientId] uniqueidentifier NOT NULL,
        [RelationTypeId] uniqueidentifier NOT NULL,
        [FilingTypeId] uniqueidentifier NULL,
        [BillingPreferenceId] uniqueidentifier NULL,
        [IsMinor] bit NOT NULL,
        [RelatedStatusId] uniqueidentifier NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_Client_Relationship] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Client_Relationship_Distinct] CHECK ([ClientId] <> [RelatedClientId]),
        CONSTRAINT [FK_Client_Relationship_Client_ClientId] FOREIGN KEY ([ClientId]) REFERENCES [Client] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Relationship_Client_RelatedClientId] FOREIGN KEY ([RelatedClientId]) REFERENCES [Client] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Relationship_OptionSetItems_BillingPreferenceId] FOREIGN KEY ([BillingPreferenceId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Relationship_OptionSetItems_FilingTypeId] FOREIGN KEY ([FilingTypeId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Relationship_OptionSetItems_RelatedStatusId] FOREIGN KEY ([RelatedStatusId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Relationship_OptionSetItems_RelationTypeId] FOREIGN KEY ([RelationTypeId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Client_Relationship_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [REMS] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RemsNumber] nvarchar(64) NOT NULL,
        [TypeId] uniqueidentifier NOT NULL,
        [StatusId] uniqueidentifier NOT NULL,
        [ClientId] uniqueidentifier NOT NULL,
        [CSEId] uniqueidentifier NULL,
        [AdminAssignedToId] uniqueidentifier NULL,
        [OnBehalfOfUserId] uniqueidentifier NULL,
        [InviteCode] nvarchar(64) NULL,
        [InviteRecipientEmail] nvarchar(256) NULL,
        [InviteSentByUserId] uniqueidentifier NULL,
        [InviteSentOnUtc] datetime2 NULL,
        [FormLastSavedOnUtc] datetime2 NULL,
        [FormSubmittedOnUtc] datetime2 NULL,
        [DepartmentId] uniqueidentifier NULL,
        [ServiceLineId] uniqueidentifier NULL,
        [JobTemplateId] uniqueidentifier NULL,
        [BillingPeriodId] uniqueidentifier NULL,
        [DepartmentDirectorId] uniqueidentifier NULL,
        [EngagementExecutiveId] uniqueidentifier NULL,
        [BillingManagerId] uniqueidentifier NULL,
        [FirstYearFeeEstimate] decimal(18,2) NULL,
        [EngagementFee] decimal(18,2) NULL,
        [RealizationPercentage] decimal(5,2) NULL,
        [BillingProcessDescription] nvarchar(1000) NULL,
        [ContractId] uniqueidentifier NULL,
        [ContractNumber] nvarchar(64) NULL,
        [PurchaseOrderNumber] nvarchar(64) NULL,
        [PurchaseOrderAmount] decimal(18,2) NULL,
        [IsFloridaOnePercentFeeApplicable] bit NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_REMS] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_REMS_PurchaseOrderAmount] CHECK ([PurchaseOrderAmount] IS NULL OR [PurchaseOrderAmount] >= 0),
        CONSTRAINT [CK_REMS_Realization] CHECK ([RealizationPercentage] IS NULL OR ([RealizationPercentage] >= 0 AND [RealizationPercentage] <= 100)),
        CONSTRAINT [FK_REMS_Client_ClientId] FOREIGN KEY ([ClientId]) REFERENCES [Client] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_Client_Contract_ContractId] FOREIGN KEY ([ContractId]) REFERENCES [Client_Contract] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_OptionSetItems_BillingPeriodId] FOREIGN KEY ([BillingPeriodId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_OptionSetItems_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_OptionSetItems_JobTemplateId] FOREIGN KEY ([JobTemplateId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_OptionSetItems_ServiceLineId] FOREIGN KEY ([ServiceLineId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_OptionSetItems_StatusId] FOREIGN KEY ([StatusId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_OptionSetItems_TypeId] FOREIGN KEY ([TypeId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_Users_AdminAssignedToId] FOREIGN KEY ([AdminAssignedToId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_Users_BillingManagerId] FOREIGN KEY ([BillingManagerId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_Users_CSEId] FOREIGN KEY ([CSEId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_Users_DepartmentDirectorId] FOREIGN KEY ([DepartmentDirectorId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_Users_EngagementExecutiveId] FOREIGN KEY ([EngagementExecutiveId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_Users_InviteSentByUserId] FOREIGN KEY ([InviteSentByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_Users_OnBehalfOfUserId] FOREIGN KEY ([OnBehalfOfUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [REMS_ApprovalTask] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [REMSId] uniqueidentifier NOT NULL,
        [RoundNumber] int NOT NULL,
        [ApproverId] uniqueidentifier NOT NULL,
        [ApproverRole] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [DecidedOnUtc] datetime2 NULL,
        [RejectionReason] nvarchar(500) NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_REMS_ApprovalTask] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_REMS_ApprovalTask_Round] CHECK ([RoundNumber] >= 1),
        CONSTRAINT [FK_REMS_ApprovalTask_REMS_REMSId] FOREIGN KEY ([REMSId]) REFERENCES [REMS] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_ApprovalTask_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_ApprovalTask_Users_ApproverId] FOREIGN KEY ([ApproverId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [REMS_Approver] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [REMSId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_REMS_Approver] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_REMS_Approver_REMS_REMSId] FOREIGN KEY ([REMSId]) REFERENCES [REMS] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_Approver_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_Approver_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [REMS_CommissionSplit] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [REMSId] uniqueidentifier NOT NULL,
        [EmployeeId] uniqueidentifier NOT NULL,
        [CommissionPercentage] decimal(5,2) NOT NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_REMS_CommissionSplit] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_REMS_CommissionSplit_Pct] CHECK ([CommissionPercentage] > 0 AND [CommissionPercentage] <= 100),
        CONSTRAINT [FK_REMS_CommissionSplit_REMS_REMSId] FOREIGN KEY ([REMSId]) REFERENCES [REMS] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_CommissionSplit_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_CommissionSplit_Users_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [REMS_EmailEvent] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [REMSId] uniqueidentifier NOT NULL,
        [EmailTemplateId] uniqueidentifier NULL,
        [EventType] nvarchar(20) NOT NULL,
        [RecipientEmail] nvarchar(256) NOT NULL,
        [Subject] nvarchar(512) NULL,
        [Body] nvarchar(max) NULL,
        [ProviderMessageId] nvarchar(256) NULL,
        [OccurredOnUtc] datetime2 NOT NULL,
        [FailureReason] nvarchar(1000) NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_REMS_EmailEvent] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_REMS_EmailEvent_EmailTemplates_EmailTemplateId] FOREIGN KEY ([EmailTemplateId]) REFERENCES [EmailTemplates] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_EmailEvent_REMS_REMSId] FOREIGN KEY ([REMSId]) REFERENCES [REMS] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_EmailEvent_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [REMS_File] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [REMSId] uniqueidentifier NOT NULL,
        [MediaId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(32) NOT NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_REMS_File] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_REMS_File_Media_MediaId] FOREIGN KEY ([MediaId]) REFERENCES [Media] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_File_REMS_REMSId] FOREIGN KEY ([REMSId]) REFERENCES [REMS] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_File_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [REMS_MarketingMethod] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [REMSId] uniqueidentifier NOT NULL,
        [MarketingMethodId] uniqueidentifier NOT NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_REMS_MarketingMethod] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_REMS_MarketingMethod_OptionSetItems_MarketingMethodId] FOREIGN KEY ([MarketingMethodId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_MarketingMethod_REMS_REMSId] FOREIGN KEY ([REMSId]) REFERENCES [REMS] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_MarketingMethod_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [REMS_PersonnelRate] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [REMSId] uniqueidentifier NOT NULL,
        [PersonnelLevelId] uniqueidentifier NOT NULL,
        [BillRatePerHour] decimal(18,2) NOT NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_REMS_PersonnelRate] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_REMS_PersonnelRate_BillRatePerHour] CHECK ([BillRatePerHour] >= 0),
        CONSTRAINT [FK_REMS_PersonnelRate_OptionSetItems_PersonnelLevelId] FOREIGN KEY ([PersonnelLevelId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_PersonnelRate_REMS_REMSId] FOREIGN KEY ([REMSId]) REFERENCES [REMS] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_PersonnelRate_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [REMS_StatusHistory] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [REMSId] uniqueidentifier NOT NULL,
        [FromStatusId] uniqueidentifier NULL,
        [ToStatusId] uniqueidentifier NOT NULL,
        [ChangedByUserId] uniqueidentifier NULL,
        [ReturnedToUserId] uniqueidentifier NULL,
        [ChangedOnUtc] datetime2 NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_REMS_StatusHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_REMS_StatusHistory_OptionSetItems_FromStatusId] FOREIGN KEY ([FromStatusId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_StatusHistory_OptionSetItems_ToStatusId] FOREIGN KEY ([ToStatusId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_StatusHistory_REMS_REMSId] FOREIGN KEY ([REMSId]) REFERENCES [REMS] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_StatusHistory_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_StatusHistory_Users_ChangedByUserId] FOREIGN KEY ([ChangedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_StatusHistory_Users_ReturnedToUserId] FOREIGN KEY ([ReturnedToUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [REMS_TaxDueDate] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [REMSId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(20) NOT NULL,
        [DueOn] date NOT NULL,
        [IsOverridden] bit NOT NULL,
        [OverriddenByUserId] uniqueidentifier NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_REMS_TaxDueDate] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_REMS_TaxDueDate_REMS_REMSId] FOREIGN KEY ([REMSId]) REFERENCES [REMS] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_TaxDueDate_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_TaxDueDate_Users_OverriddenByUserId] FOREIGN KEY ([OverriddenByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [REMS_TaxForm] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [REMSId] uniqueidentifier NOT NULL,
        [TaxFormId] uniqueidentifier NOT NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_REMS_TaxForm] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_REMS_TaxForm_OptionSetItems_TaxFormId] FOREIGN KEY ([TaxFormId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_TaxForm_REMS_REMSId] FOREIGN KEY ([REMSId]) REFERENCES [REMS] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_TaxForm_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE TABLE [REMS_ApprovalChecklistItem] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [TaskId] uniqueidentifier NOT NULL,
        [DisplayOrder] int NOT NULL,
        [Label] nvarchar(200) NOT NULL,
        [IsCompleted] bit NOT NULL,
        [CompletedOnUtc] datetime2 NULL,
        [CreatedById] uniqueidentifier NULL,
        [CreatedOnUtc] datetime2 NOT NULL,
        [UpdatedById] uniqueidentifier NULL,
        [UpdatedOnUtc] datetime2 NOT NULL,
        [Deleted] bit NOT NULL,
        [DeletedOnUtc] datetime2 NULL,
        CONSTRAINT [PK_REMS_ApprovalChecklistItem] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_REMS_ApprovalChecklistItem_REMS_ApprovalTask_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [REMS_ApprovalTask] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_REMS_ApprovalChecklistItem_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_EntityTypeId] ON [Client] ([EntityTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_IndustryId] ON [Client] ([IndustryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_PersonId] ON [Client] ([PersonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_ReferralSourceId] ON [Client] ([ReferralSourceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_TenantId] ON [Client] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Client_TenantId_ClientNumber] ON [Client] ([TenantId], [ClientNumber]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Client_TenantId_MaconomyCustomerNumber] ON [Client] ([TenantId], [MaconomyCustomerNumber]) WHERE [MaconomyCustomerNumber] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Client_TenantId_PersonId] ON [Client] ([TenantId], [PersonId]) WHERE [Deleted] = 0 AND [PersonId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Client_TenantId_PrimaryEmail] ON [Client] ([TenantId], [PrimaryEmail]) WHERE [PrimaryEmail] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_TenantId_Status] ON [Client] ([TenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Address_AddressId] ON [Client_Address] ([AddressId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Address_AddressTypeId] ON [Client_Address] ([AddressTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Address_ClientId] ON [Client_Address] ([ClientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Address_TenantId] ON [Client_Address] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Address_TenantId_ClientId_AddressTypeId] ON [Client_Address] ([TenantId], [ClientId], [AddressTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Contact_ClientId] ON [Client_Contact] ([ClientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Contact_ContactRoleId] ON [Client_Contact] ([ContactRoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Contact_PersonId] ON [Client_Contact] ([PersonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Contact_TenantId] ON [Client_Contact] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Client_Contact_TenantId_ClientId_PersonId_ContactRoleId] ON [Client_Contact] ([TenantId], [ClientId], [PersonId], [ContactRoleId]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Contract_ClientId] ON [Client_Contract] ([ClientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Client_Contract_Current] ON [Client_Contract] ([TenantId], [ClientId]) WHERE [Deleted] = 0 AND [IsCurrent] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Contract_TenantId] ON [Client_Contract] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Contract_TenantId_ClientId] ON [Client_Contract] ([TenantId], [ClientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Relationship_BillingPreferenceId] ON [Client_Relationship] ([BillingPreferenceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Relationship_ClientId] ON [Client_Relationship] ([ClientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Relationship_FilingTypeId] ON [Client_Relationship] ([FilingTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Relationship_RelatedClientId] ON [Client_Relationship] ([RelatedClientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Relationship_RelatedStatusId] ON [Client_Relationship] ([RelatedStatusId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Relationship_RelationTypeId] ON [Client_Relationship] ([RelationTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Relationship_TenantId] ON [Client_Relationship] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Client_Relationship_TenantId_ClientId_RelatedClientId] ON [Client_Relationship] ([TenantId], [ClientId], [RelatedClientId]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_Client_Relationship_TenantId_RelatedClientId] ON [Client_Relationship] ([TenantId], [RelatedClientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_AdminAssignedToId] ON [REMS] ([AdminAssignedToId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_BillingManagerId] ON [REMS] ([BillingManagerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_BillingPeriodId] ON [REMS] ([BillingPeriodId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_ClientId] ON [REMS] ([ClientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_ContractId] ON [REMS] ([ContractId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_CSEId] ON [REMS] ([CSEId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_DepartmentDirectorId] ON [REMS] ([DepartmentDirectorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_DepartmentId] ON [REMS] ([DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_EngagementExecutiveId] ON [REMS] ([EngagementExecutiveId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_InviteSentByUserId] ON [REMS] ([InviteSentByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_JobTemplateId] ON [REMS] ([JobTemplateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_OnBehalfOfUserId] ON [REMS] ([OnBehalfOfUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_ServiceLineId] ON [REMS] ([ServiceLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_StatusId] ON [REMS] ([StatusId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_TenantId] ON [REMS] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_TenantId_ClientId] ON [REMS] ([TenantId], [ClientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_TenantId_CreatedById_StatusId] ON [REMS] ([TenantId], [CreatedById], [StatusId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_TenantId_DepartmentId] ON [REMS] ([TenantId], [DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_REMS_TenantId_InviteCode] ON [REMS] ([TenantId], [InviteCode]) WHERE [Deleted] = 0 AND [InviteCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_TenantId_OnBehalfOfUserId_StatusId] ON [REMS] ([TenantId], [OnBehalfOfUserId], [StatusId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_REMS_TenantId_RemsNumber] ON [REMS] ([TenantId], [RemsNumber]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_TenantId_StatusId_AdminAssignedToId_CreatedOnUtc] ON [REMS] ([TenantId], [StatusId], [AdminAssignedToId], [CreatedOnUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_TypeId] ON [REMS] ([TypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_ApprovalChecklistItem_TaskId] ON [REMS_ApprovalChecklistItem] ([TaskId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_ApprovalChecklistItem_TenantId] ON [REMS_ApprovalChecklistItem] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_REMS_ApprovalChecklistItem_TenantId_TaskId_DisplayOrder] ON [REMS_ApprovalChecklistItem] ([TenantId], [TaskId], [DisplayOrder]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_ApprovalTask_ApproverId] ON [REMS_ApprovalTask] ([ApproverId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_ApprovalTask_REMSId] ON [REMS_ApprovalTask] ([REMSId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_ApprovalTask_TenantId] ON [REMS_ApprovalTask] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_ApprovalTask_TenantId_ApproverId_Status] ON [REMS_ApprovalTask] ([TenantId], [ApproverId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_ApprovalTask_TenantId_REMSId_RoundNumber] ON [REMS_ApprovalTask] ([TenantId], [REMSId], [RoundNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_REMS_ApprovalTask_TenantId_REMSId_RoundNumber_ApproverId_ApproverRole] ON [REMS_ApprovalTask] ([TenantId], [REMSId], [RoundNumber], [ApproverId], [ApproverRole]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_Approver_REMSId] ON [REMS_Approver] ([REMSId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_Approver_TenantId] ON [REMS_Approver] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_REMS_Approver_TenantId_REMSId_UserId] ON [REMS_Approver] ([TenantId], [REMSId], [UserId]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_Approver_UserId] ON [REMS_Approver] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_ChecklistTemplate_SettingsId] ON [REMS_ChecklistTemplate] ([SettingsId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_ChecklistTemplate_TenantId] ON [REMS_ChecklistTemplate] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_REMS_ChecklistTemplate_TenantId_SettingsId_ApproverRole_DisplayOrder] ON [REMS_ChecklistTemplate] ([TenantId], [SettingsId], [ApproverRole], [DisplayOrder]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_CommissionSplit_EmployeeId] ON [REMS_CommissionSplit] ([EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_CommissionSplit_REMSId] ON [REMS_CommissionSplit] ([REMSId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_CommissionSplit_TenantId] ON [REMS_CommissionSplit] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_REMS_CommissionSplit_TenantId_REMSId_EmployeeId] ON [REMS_CommissionSplit] ([TenantId], [REMSId], [EmployeeId]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_EmailEvent_EmailTemplateId] ON [REMS_EmailEvent] ([EmailTemplateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_EmailEvent_REMSId] ON [REMS_EmailEvent] ([REMSId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_EmailEvent_TenantId] ON [REMS_EmailEvent] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_REMS_EmailEvent_TenantId_ProviderMessageId_EventType] ON [REMS_EmailEvent] ([TenantId], [ProviderMessageId], [EventType]) WHERE [ProviderMessageId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_EmailEvent_TenantId_REMSId_OccurredOnUtc] ON [REMS_EmailEvent] ([TenantId], [REMSId], [OccurredOnUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_File_MediaId] ON [REMS_File] ([MediaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_File_REMSId] ON [REMS_File] ([REMSId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_File_TenantId] ON [REMS_File] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_File_TenantId_REMSId_Kind] ON [REMS_File] ([TenantId], [REMSId], [Kind]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_REMS_File_TenantId_REMSId_MediaId] ON [REMS_File] ([TenantId], [REMSId], [MediaId]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_MarketingMethod_MarketingMethodId] ON [REMS_MarketingMethod] ([MarketingMethodId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_MarketingMethod_REMSId] ON [REMS_MarketingMethod] ([REMSId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_MarketingMethod_TenantId] ON [REMS_MarketingMethod] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_REMS_MarketingMethod_TenantId_REMSId_MarketingMethodId] ON [REMS_MarketingMethod] ([TenantId], [REMSId], [MarketingMethodId]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_PersonnelRate_PersonnelLevelId] ON [REMS_PersonnelRate] ([PersonnelLevelId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_PersonnelRate_REMSId] ON [REMS_PersonnelRate] ([REMSId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_PersonnelRate_TenantId] ON [REMS_PersonnelRate] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_REMS_PersonnelRate_TenantId_REMSId_PersonnelLevelId] ON [REMS_PersonnelRate] ([TenantId], [REMSId], [PersonnelLevelId]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_StatusHistory_ChangedByUserId] ON [REMS_StatusHistory] ([ChangedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_StatusHistory_FromStatusId] ON [REMS_StatusHistory] ([FromStatusId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_StatusHistory_REMSId] ON [REMS_StatusHistory] ([REMSId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_StatusHistory_ReturnedToUserId] ON [REMS_StatusHistory] ([ReturnedToUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_StatusHistory_TenantId] ON [REMS_StatusHistory] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_StatusHistory_TenantId_REMSId_ChangedOnUtc] ON [REMS_StatusHistory] ([TenantId], [REMSId], [ChangedOnUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_StatusHistory_ToStatusId] ON [REMS_StatusHistory] ([ToStatusId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_TaxDueDate_OverriddenByUserId] ON [REMS_TaxDueDate] ([OverriddenByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_TaxDueDate_REMSId] ON [REMS_TaxDueDate] ([REMSId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_TaxDueDate_TenantId] ON [REMS_TaxDueDate] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_REMS_TaxDueDate_TenantId_REMSId_Kind] ON [REMS_TaxDueDate] ([TenantId], [REMSId], [Kind]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_TaxForm_REMSId] ON [REMS_TaxForm] ([REMSId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_TaxForm_TaxFormId] ON [REMS_TaxForm] ([TaxFormId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    CREATE INDEX [IX_REMS_TaxForm_TenantId] ON [REMS_TaxForm] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_REMS_TaxForm_TenantId_REMSId_TaxFormId] ON [REMS_TaxForm] ([TenantId], [REMSId], [TaxFormId]) WHERE [Deleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [REMS_DepartmentDirector] ADD CONSTRAINT [FK_REMS_DepartmentDirector_OptionSetItems_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [OptionSetItems] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [REMS_DepartmentDirector] ADD CONSTRAINT [FK_REMS_DepartmentDirector_REMS_Settings_SettingsId] FOREIGN KEY ([SettingsId]) REFERENCES [REMS_Settings] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [REMS_DepartmentDirector] ADD CONSTRAINT [FK_REMS_DepartmentDirector_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [REMS_DepartmentDirector] ADD CONSTRAINT [FK_REMS_DepartmentDirector_Users_DirectorUserId] FOREIGN KEY ([DirectorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    ALTER TABLE [REMS_Settings] ADD CONSTRAINT [FK_REMS_Settings_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023103102_RemsDataModelV2'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023103102_RemsDataModelV2', N'9.0.0');
END;

COMMIT;
GO

