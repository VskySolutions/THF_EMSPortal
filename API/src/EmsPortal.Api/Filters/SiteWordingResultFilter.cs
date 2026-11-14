using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Branding;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EmsPortal.Api.Filters;

/// <summary>
/// The envelope's message and error details say "REMS" in code; on the way out the word becomes the
/// tenant's site name, the same one the web app wears. One place, so no controller words it itself.
/// </summary>
public sealed class SiteWordingResultFilter : IAsyncResultFilter
{
    private readonly ISiteNameProvider _siteName;
    private readonly ITenantContext _tenant;

    public SiteWordingResultFilter(ISiteNameProvider siteName, ITenantContext tenant)
    {
        _siteName = siteName;
        _tenant = tenant;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: { } envelope })
        {
            var tenantId = context.HttpContext.User.GetActiveTenantId() ?? (_tenant.IsResolved ? _tenant.TenantId : (Guid?)null);
            if (tenantId is not null)
            {
                var cancellationToken = context.HttpContext.RequestAborted;
                if (envelope is ApiErrorResponse error)
                {
                    await RewordAsync(error, nameof(ApiErrorResponse.Message), tenantId, cancellationToken);
                    await RewordAsync(error.Error, nameof(ApiError.Details), tenantId, cancellationToken);
                }
                else if (envelope.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(ApiResponse<>))
                {
                    await RewordAsync(envelope, nameof(ApiResponse<object>.Message), tenantId, cancellationToken);
                }
            }
        }
        await next();
    }

    // The envelope's properties are init-only; reflection is the one way to reword a built one.
    private async Task RewordAsync(object target, string propertyName, Guid? tenantId, CancellationToken cancellationToken)
    {
        var property = target.GetType().GetProperty(propertyName);
        if (property?.GetValue(target) is string text && SiteWording.Mentions(text))
        {
            property.SetValue(target, await _siteName.ApplyAsync(tenantId, text, cancellationToken));
        }
    }
}
