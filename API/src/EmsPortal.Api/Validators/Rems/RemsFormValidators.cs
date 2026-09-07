using EmsPortal.Api.Models.Rems;
using FluentValidation;

namespace EmsPortal.Api.Validators.Rems;

/// <summary>
/// Validates the EMS form build/save payload (WO-112, AC-REMS-007.7): a CSE and a known entity type
/// code are both required before the form can be saved.
/// </summary>
public sealed class SaveRemsFormRequestValidator : AbstractValidator<SaveRemsFormRequest>
{
    public SaveRemsFormRequestValidator()
    {
        RuleFor(x => x.CseUserId).NotEmpty().WithMessage("cseUserId is required.");
        RuleFor(x => x.EntityType)
            .Must(RemsFormOptionCodes.IsKnownEntityType)
            .WithMessage($"entityType must be one of: {string.Join(", ", RemsFormOptionCodes.EntityTypes)}.");
    }
}

/// <summary>The seeded <c>REMS.EntityType</c> option-set codes (see <c>DefaultOptionSets</c>).</summary>
internal static class RemsFormOptionCodes
{
    // "business" stays accepted though it is no longer offered: forms sent before it was split into
    // not-for-profit / insurance / commercial still carry it, and those must remain completable.
    public static readonly IReadOnlyList<string> EntityTypes =
        new[] { "individual", "not_for_profit", "insurance", "commercial", "trust_estate", "government", "business" };

    public static bool IsKnownEntityType(string? value) => value is not null && EntityTypes.Contains(value);
}
