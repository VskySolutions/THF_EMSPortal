using EmsPortal.Api.Models.Rems;
using EmsPortal.Domain.Entities;
using FluentValidation;

namespace EmsPortal.Api.Validators.Rems;

/// <summary>
/// Validates a REMS request create payload (WO-111): the client name is required; at least one of
/// customer email / mobile is required (AC-REMS-004.7); the type must be a known option-set code.
/// </summary>
public sealed class CreateRemsRequestRequestValidator : AbstractValidator<CreateRemsRequestRequest>
{
    public CreateRemsRequestRequestValidator()
    {
        RuleFor(x => x.ClientName).NotEmpty().WithMessage("clientName is required.").MaximumLength(200);
        // A name particle, not a second name field — the column is nvarchar(16). Free text rather than a
        // closed set: the five offered in the picker are what most clients need, not all a client may have.
        RuleFor(x => x.ClientNameSuffix).MaximumLength(16).When(x => !string.IsNullOrWhiteSpace(x.ClientNameSuffix));
        // The partner's message is client-facing now and holds pasted correspondence; the column is
        // nvarchar(max), so nothing is capped here either.
        RuleFor(x => x.CustomerEmail).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.CustomerEmail));
        RuleFor(x => x.CustomerMobileNumber).MaximumLength(32).When(x => !string.IsNullOrWhiteSpace(x.CustomerMobileNumber));

        // AC-REMS-004.7: a customer email OR a customer mobile number is required.
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.CustomerEmail) || !string.IsNullOrWhiteSpace(x.CustomerMobileNumber))
            .WithMessage("A customer email or mobile number is required.");

        RuleFor(x => x.Type)
            .Must(RemsRequestOptionCodes.IsKnownType)
            .WithMessage($"type must be one of: {string.Join(", ", RemsRequestOptionCodes.Types)}.");
    }
}

/// <summary>Validates a REMS request edit payload (WO-111).</summary>
public sealed class UpdateRemsRequestRequestValidator : AbstractValidator<UpdateRemsRequestRequest>
{
    public UpdateRemsRequestRequestValidator()
    {
        RuleFor(x => x.ClientName).NotEmpty().MaximumLength(200).When(x => x.ClientName is not null);
        // Not NotEmpty: "" is how the suffix is CLEARED, which an omitted field cannot say.
        RuleFor(x => x.ClientNameSuffix).MaximumLength(16).When(x => x.ClientNameSuffix is not null);
        RuleFor(x => x.CustomerEmail).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.CustomerEmail));
        RuleFor(x => x.CustomerMobileNumber).MaximumLength(32).When(x => !string.IsNullOrWhiteSpace(x.CustomerMobileNumber));
        RuleFor(x => x.Type).Must(RemsRequestOptionCodes.IsKnownType)
            .WithMessage($"type must be one of: {string.Join(", ", RemsRequestOptionCodes.Types)}.")
            .When(x => x.Type is not null);
    }
}

/// <summary>Validates an attach-files payload: at least one media id, none of them empty.</summary>
public sealed class AddRemsFilesRequestValidator : AbstractValidator<AddRemsFilesRequest>
{
    public AddRemsFilesRequestValidator()
    {
        RuleFor(x => x.MediaIds).NotEmpty().WithMessage("mediaIds is required.");
        RuleForEach(x => x.MediaIds).NotEmpty().WithMessage("A mediaId cannot be empty.");
    }
}

/// <summary>The seeded <c>REMS.Type</c> option-set codes (see <c>DefaultOptionSets</c>).</summary>
internal static class RemsRequestOptionCodes
{
    // From RemsRequestTypes so the accepted set and the codes the controllers branch on cannot drift.
    public static readonly IReadOnlyList<string> Types = RemsRequestTypes.All;

    public static bool IsKnownType(string? value) => value is not null && Types.Contains(value);
}
