using EmsPortal.Api.Models.Rems;
using FluentValidation;

namespace EmsPortal.Api.Validators.Rems;

/// <summary>
/// Validates a REMS settings update (WO-114): each department mapping carries a code and a director,
/// no duplicate departments.
/// </summary>
public sealed class UpdateRemsSettingsRequestValidator : AbstractValidator<UpdateRemsSettingsRequest>
{
    public UpdateRemsSettingsRequestValidator()
    {
        RuleFor(x => x.DepartmentDirectors)
            .Must(list => list.Select(d => d.Department?.Trim().ToLowerInvariant()).Distinct().Count() == list.Count)
            .WithMessage("A department may be mapped only once.")
            .When(x => x.DepartmentDirectors.Count > 0);

        RuleForEach(x => x.DepartmentDirectors).ChildRules(d =>
        {
            d.RuleFor(i => i.Department).NotEmpty().WithMessage("department is required.").MaximumLength(64);
            d.RuleFor(i => i.DirectorUserId).NotEmpty().WithMessage("directorUserId is required.");
        });
    }
}
