using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Abstractions.Tenancy;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EmsPortal.Api.Filters;

/// <summary>
/// Makes the tenant's Maconomy clients fit to read before the action runs. A client that is a Maconomy
/// customer has no name or email in the database: each is put on the record from memory as it is loaded,
/// and this is what sees to it that the memory is there. Costs nothing while what is held is fresh, and
/// nothing at all for a tenant with no such clients.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class LoadMaconomyClientsAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        if (services.GetRequiredService<ITenantContext>() is { IsResolved: true } tenant)
        {
            await services.GetRequiredService<IMaconomyClientDirectory>()
                .EnsureLoadedAsync(tenant.TenantId, context.HttpContext.RequestAborted);
        }

        await next();
    }
}
