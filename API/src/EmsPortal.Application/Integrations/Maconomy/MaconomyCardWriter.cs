using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Integrations.Maconomy;

/// <summary>Default <see cref="IMaconomyCardWriter"/>: the gateway's create, through the session manager.</summary>
public sealed class MaconomyCardWriter : IMaconomyCardWriter
{
    private readonly IMaconomySessionManager _session;
    private readonly IMaconomyGateway _gateway;

    public MaconomyCardWriter(IMaconomySessionManager session, IMaconomyGateway gateway)
    {
        _session = session;
        _gateway = gateway;
    }

    public Task<MaconomyCreatedRecord> CreateAsync(MaconomyConnection connection, MaconomyCardWrite write, CancellationToken cancellationToken = default)
        => _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.CreateAsync(new MaconomyEndpoint(connection.BaseUrl, connection.InstanceCode), token, write, ct),
            cancellationToken);
}
