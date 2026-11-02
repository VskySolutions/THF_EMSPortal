using FluentValidation;
using EmsPortal.Api.Models.Tenants;

namespace EmsPortal.Api.Validators.Tenants;

public sealed class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Identifier)
            .NotEmpty()
            .MaximumLength(100)
            .Matches("^[a-z0-9-]+$")
            .WithMessage("Identifier must be a URL-safe slug (lowercase letters, digits, hyphens).");
        RuleFor(x => x.DeveloperEmails)
            .Must(emails => emails is null || emails.Count <= DeveloperEmailRules.MaxEmails)
            .WithMessage(DeveloperEmailRules.TooMany);
        RuleForEach(x => x.DeveloperEmails).SetValidator(new DeveloperEmailValidator());
    }
}

public sealed class UpdateTenantRequestValidator : AbstractValidator<UpdateTenantRequest>
{
    public UpdateTenantRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DeveloperEmails)
            .Must(emails => emails is null || emails.Count <= DeveloperEmailRules.MaxEmails)
            .WithMessage(DeveloperEmailRules.TooMany);
        RuleForEach(x => x.DeveloperEmails).SetValidator(new DeveloperEmailValidator());
    }
}

/// <summary>The developer list as both requests take it: not too many, and each one a real address.</summary>
internal static class DeveloperEmailRules
{
    public const int MaxEmails = 20;
    public const string TooMany = "Name at most 20 developer emails.";
}

/// <summary>One developer address: present, no longer than an address can be, and shaped like one.</summary>
internal sealed class DeveloperEmailValidator : AbstractValidator<string>
{
    public DeveloperEmailValidator()
    {
        RuleFor(x => x)
            .NotEmpty().WithMessage("A developer email is blank.")
            .MaximumLength(254).WithMessage("A developer email is too long.")
            .EmailAddress().WithMessage("'{PropertyValue}' is not an email address.");
    }
}
