namespace EmsPortal.Api.Models.Rems;

/// <summary>The option-set KEYS the REMS feature stores references to, in one place.</summary>
public static class RemsOptionSetKeys
{
    public const string Type = "REMS.Type";
    public const string Status = "REMS.Status";
    public const string ReferralSource = "REMS.ReferralSource";
    public const string EntityType = "REMS.EntityType";
    public const string Department = "REMS.Department";
    public const string ServiceLine = "REMS.ServiceLine";
    public const string Industry = "REMS.Industry";
    public const string BillingPeriod = "REMS.BillingPeriod";
    public const string PersonnelLevel = "REMS.PersonnelLevel";

    /// <summary>
    /// How far a client's RELATED client has got — the status on every row of the Related Entities
    /// list.
    /// </summary>
    public const string RelatedEntityStatus = "REMS.RelatedEntityStatus";

    /// <summary>Referenced by item ID already — the grouped marketing list and the tax-form checklist.</summary>
    public const string MarketingMethods = "REMSMarketing_MarketingMethods.MarketingMethodId";
    public const string TaxForm = "REMS.TaxForm";
}
