using FluentValidation;
using EmsPortal.Api.Models.UniversalFeatures;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace EmsPortal.Api.Validators.UniversalFeatures;

public sealed class BulkRestoreRequestValidator : AbstractValidator<BulkRestoreRequest>
{
    public BulkRestoreRequestValidator(IOptions<LimitsOptions> limits)
    {
        var max = Math.Max(1, limits.Value.Paging.MaxBulkIds);
        RuleFor(x => x.EntityIds)
            .NotEmpty()
            .Must(ids => ids.Count <= max).WithMessage($"At most {max} records can be restored in one call.");
    }
}
