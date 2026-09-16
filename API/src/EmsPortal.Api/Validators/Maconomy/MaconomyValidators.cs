using EmsPortal.Api.Models.Maconomy;
using EmsPortal.Shared.Configuration;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace EmsPortal.Api.Validators.Maconomy;

/// <summary>Validates a connection save. The password is optional here; the service requires one on create.</summary>
public sealed class SaveMaconomyConnectionRequestValidator : AbstractValidator<SaveMaconomyConnectionRequest>
{
    public SaveMaconomyConnectionRequestValidator(IOptions<MaconomyOptions> options)
    {
        var maxLimit = Math.Max(1, options.Value.MaxLimit);

        // The credentials travel in a Basic header, so the address has to be one they cannot be read from.
        RuleFor(x => x.BaseUrl)
            .NotEmpty().MaximumLength(500)
            .Must(BeAnHttpsUrl).WithMessage("baseUrl must be an absolute https:// URL, such as https://host/maconomy-api.");
        RuleFor(x => x.InstanceCode)
            .NotEmpty().MaximumLength(100)
            .Matches("^[A-Za-z0-9_.-]+$").WithMessage("instanceCode may contain only letters, digits and the separators . _ -");
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).MaximumLength(500);
        RuleFor(x => x.ContainerId).MaximumLength(100);
        RuleFor(x => x.DefaultLimit)
            .InclusiveBetween(1, maxLimit).WithMessage($"defaultLimit must be between 1 and {maxLimit}.");
    }

    private static bool BeAnHttpsUrl(string value)
        => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
