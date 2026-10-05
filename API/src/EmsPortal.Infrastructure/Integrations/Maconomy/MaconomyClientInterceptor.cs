using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EmsPortal.Infrastructure.Integrations.Maconomy;

/// <summary>
/// Puts what Maconomy says about a client on their <see cref="Person"/> as it is read from the database,
/// from memory and without a call. A Maconomy client's row holds only the customer number, so this is
/// what makes every read of one — however deep in a query it was included — carry a name and an email.
/// </summary>
internal sealed class MaconomyClientInterceptor : IMaterializationInterceptor
{
    private readonly IMaconomyClientDirectory _clients;

    public MaconomyClientInterceptor(IMaconomyClientDirectory clients)
    {
        _clients = clients;
    }

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        if (entity is Person { IsMaconomyClient: true } person)
        {
            person.Maconomy = _clients.Find(person.TenantId, person.MaconomyCustomerNumber);
        }
        return entity;
    }
}
