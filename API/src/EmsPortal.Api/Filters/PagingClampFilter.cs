using EmsPortal.Shared.Configuration;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace EmsPortal.Api.Filters;

/// <summary>
/// Pins every list request's <c>page</c> and <c>limit</c> inside sane bounds before the action sees them, so the
/// row ceiling (Limits:Paging:MaxPageSize) is one number in one place rather than a clamp in each controller.
/// </summary>
public sealed class PagingClampFilter : IActionFilter
{
    private readonly int _maxPageSize;

    public PagingClampFilter(IOptions<LimitsOptions> limits)
    {
        _maxPageSize = Math.Max(1, limits.Value.Paging.MaxPageSize);
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionArguments.TryGetValue("page", out var page) && page is int pageValue)
        {
            context.ActionArguments["page"] = Math.Max(1, pageValue);
        }

        if (context.ActionArguments.TryGetValue("limit", out var limit) && limit is int limitValue)
        {
            context.ActionArguments["limit"] = Math.Clamp(limitValue, 1, _maxPageSize);
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
