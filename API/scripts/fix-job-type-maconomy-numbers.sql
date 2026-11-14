-- Clears the template job numbers ("ZZ…") the first release of AddRemsJobType left on the REMS Job Type
-- items, and puts them on the REMS Service Line items where none is set. Safe to run more than once.
-- The same change as the migration ClearRemsJobTypeTemplateNumbers, for a server fixed ahead of a deploy.
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

SELECT i.Label, i.MaconomyNumber
FROM OptionSetItems i
JOIN OptionSets s ON s.Id = i.OptionSetId AND s.[Key] = N'REMS.JobType'
WHERE i.MaconomyNumber LIKE N'ZZ%';

BEGIN TRANSACTION;

UPDATE i SET i.MaconomyNumber = NULL, i.UpdatedOnUtc = SYSUTCDATETIME()
FROM OptionSetItems i
JOIN OptionSets s ON s.Id = i.OptionSetId AND s.[Key] = N'REMS.JobType'
WHERE i.MaconomyNumber LIKE N'ZZ%';
PRINT CONCAT(@@ROWCOUNT, ' job type number(s) cleared');

UPDATE i SET i.MaconomyNumber = v.Number, i.UpdatedOnUtc = SYSUTCDATETIME()
FROM OptionSetItems i
JOIN OptionSets s ON s.Id = i.OptionSetId AND s.[Key] = N'REMS.ServiceLine'
JOIN (VALUES
    (N'tax_compliance', N'ZZTax Compliance'),
    (N'client_accounting_services', N'ZZClient Accounting Services'),
    (N'consulting', N'ZZConsulting Engagement'),
    (N'business_valuation', N'ZZBusiness Valuation'),
    (N'it_services', N'ZZInformation Technology Services'),
    (N'plan_administration', N'ZZPension Administration and Tax Compliance'),
    (N'mergers_acquisitions', N'ZZMergers and Acquisitions'),
    (N'peer_review', N'ZZPeer Review'),
    (N'soc', N'ZZSOC'),
    (N'employee_benefits', N'ZZPension Administration and Tax Compliance'),
    (N'estate_planning', N'ZZTax Compliance'),
    (N'litigation_support', N'ZZLitigation'),
    (N'forensic_accounting', N'ZZForensic Accounting')
) v (ItemValue, Number) ON i.[Value] = v.ItemValue
WHERE i.MaconomyNumber IS NULL AND i.Deleted = 0;
PRINT CONCAT(@@ROWCOUNT, ' service line number(s) set');

COMMIT TRANSACTION;
