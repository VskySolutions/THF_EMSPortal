using EmsPortal.Api.Validators.Rems;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Api.Models.Rems;

/// <summary>
/// The client layer stores contact roles and address types as option-set items (REMS.ContactRole,
/// REMS.AddressType). The API contract has always named them by their enum names, so these maps translate
/// in both directions and keep the required-contact rule that used to be a stored flag.
/// </summary>
internal static class RemsContactRoles
{
    private static readonly (string Code, string Name)[] Map =
    {
        ("self", nameof(RemsContactRole.Self)),
        ("spouse", nameof(RemsContactRole.Spouse)),
        ("primary_client_contact", nameof(RemsContactRole.PrimaryClientContact)),
        ("financial_contact", nameof(RemsContactRole.FinancialContact)),
        ("billing_contact", nameof(RemsContactRole.BillingContact)),
        ("other_contact", nameof(RemsContactRole.OtherContact)),
        ("finance_director", nameof(RemsContactRole.FinanceDirector)),
        ("trust_estate_contact", nameof(RemsContactRole.TrustEstateContact)),
        ("banker", nameof(RemsContactRole.Banker)),
        ("lawyer", nameof(RemsContactRole.Lawyer)),
    };

    public const string BillingContactCode = "billing_contact";

    /// <summary>The option code for a role given by enum name or by code; null for an unknown role.</summary>
    public static string? CodeOf(string? roleNameOrCode)
    {
        if (string.IsNullOrWhiteSpace(roleNameOrCode))
        {
            return null;
        }
        var wanted = roleNameOrCode.Trim();
        foreach (var (code, name) in Map)
        {
            if (string.Equals(code, wanted, StringComparison.OrdinalIgnoreCase) || string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }
        return null;
    }

    /// <summary>The enum name the contract hands out for an option code; the code itself when unknown.</summary>
    public static string NameOf(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }
        foreach (var (c, name) in Map)
        {
            if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }
        return code;
    }

    /// <summary>
    /// Whether a role must be filled for an entity type: the rule that used to be written to the contact row
    /// at submit time. Self for an individual; the primary contact for every business group, plus the
    /// financial contact except for a commercial client; nothing for a government client.
    /// </summary>
    public static bool IsRequired(string? entityTypeCode, string? roleNameOrCode)
    {
        var name = NameOf(CodeOf(roleNameOrCode));
        if (entityTypeCode == RemsFormPayloadValidator.Individual)
        {
            return name == nameof(RemsContactRole.Self);
        }
        if (RemsFormPayloadValidator.IsBusinessGroup(entityTypeCode))
        {
            return name == nameof(RemsContactRole.PrimaryClientContact)
                || (name == nameof(RemsContactRole.FinancialContact) && entityTypeCode != RemsFormPayloadValidator.Commercial);
        }
        return false;
    }

    /// <summary>The roles the intake form asks for, in the order the review shows them. Retired roles are kept so old rows still read.</summary>
    public static IReadOnlyList<string> RolesFor(string? entityTypeCode)
    {
        if (entityTypeCode == RemsFormPayloadValidator.Individual)
        {
            return new[] { nameof(RemsContactRole.Self), nameof(RemsContactRole.Spouse), nameof(RemsContactRole.BillingContact) };
        }
        if (RemsFormPayloadValidator.IsBusinessGroup(entityTypeCode))
        {
            return new[]
            {
                nameof(RemsContactRole.PrimaryClientContact), nameof(RemsContactRole.FinancialContact),
                nameof(RemsContactRole.TrustEstateContact), nameof(RemsContactRole.OtherContact),
                nameof(RemsContactRole.BillingContact), nameof(RemsContactRole.Banker), nameof(RemsContactRole.Lawyer),
            };
        }
        if (entityTypeCode == RemsFormPayloadValidator.Government)
        {
            return new[] { nameof(RemsContactRole.FinanceDirector), nameof(RemsContactRole.OtherContact), nameof(RemsContactRole.BillingContact) };
        }
        return Array.Empty<string>();
    }
}

internal static class RemsAddressTypes
{
    public const string Physical = "physical";
    public const string Mailing = "mailing";
    public const string Billing = "billing";

    public static string CodeOf(RemsAddressType type) => type switch
    {
        RemsAddressType.Physical => Physical,
        RemsAddressType.Mailing => Mailing,
        _ => Billing,
    };

    /// <summary>The enum name the contract hands out: Physical, Mailing or Billing.</summary>
    public static string NameOf(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        Physical => nameof(RemsAddressType.Physical),
        Mailing => nameof(RemsAddressType.Mailing),
        Billing => nameof(RemsAddressType.Billing),
        _ => code ?? string.Empty,
    };

    /// <summary>The platform Address.AddressType the intake has always filed each kind under.</summary>
    public static AddressType PlatformTypeOf(string code) => code switch
    {
        Physical => AddressType.Office,
        Mailing => AddressType.Other,
        _ => AddressType.Billing,
    };
}

internal static class RemsRelationTypes
{
    public const string Spouse = "spouse";
    public const string Child = "child";
    public const string Other = "other";
    public const string RelatedEntity = "related_entity";
}
