/*
  Rebrand: EMS Portal -> COP Portal in the data admins typed under the old name.

  The code carries the new product name on its own. This renames what lives in the database: tenant names,
  SMTP sender and account names, email template text, and a branding theme's application name. Idempotent:
  a second run finds nothing to change. Run once per catalog, before or after the publish.

      sqlcmd -S "VSky-MT\SQLEXPRESS" -d THF_EMSPortal_New -E -I -i rebrand-cop-portal.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

UPDATE Tenants
SET Name = REPLACE(Name, 'EMS Portal', 'COP Portal'), UpdatedOnUtc = SYSUTCDATETIME()
WHERE Name LIKE '%EMS Portal%';
PRINT CONCAT('Tenants renamed: ', @@ROWCOUNT);

UPDATE SmtpAccounts
SET FromName = REPLACE(FromName, 'EMS Portal', 'COP Portal'),
    AccountName = REPLACE(AccountName, 'EMS Portal', 'COP Portal'),
    UpdatedOnUtc = SYSUTCDATETIME()
WHERE FromName LIKE '%EMS Portal%' OR AccountName LIKE '%EMS Portal%';
PRINT CONCAT('SMTP accounts renamed: ', @@ROWCOUNT);

UPDATE EmailTemplates
SET Subject = REPLACE(Subject, 'EMS Portal', 'COP Portal'),
    Body = REPLACE(Body, 'EMS Portal', 'COP Portal'),
    UpdatedOnUtc = SYSUTCDATETIME()
WHERE Subject LIKE '%EMS Portal%' OR Body LIKE '%EMS Portal%';
PRINT CONCAT('Email templates reworded: ', @@ROWCOUNT);

UPDATE TenantBrandings
SET ThemeJson = JSON_MODIFY(ThemeJson, '$.identity.applicationName',
        REPLACE(JSON_VALUE(ThemeJson, '$.identity.applicationName'), 'EMS Portal', 'COP Portal')),
    UpdatedOnUtc = SYSUTCDATETIME()
WHERE JSON_VALUE(ThemeJson, '$.identity.applicationName') LIKE '%EMS Portal%';
PRINT CONCAT('Branding application names renamed: ', @@ROWCOUNT);

COMMIT TRANSACTION;
