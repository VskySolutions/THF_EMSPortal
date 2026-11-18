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
    public Task<int> AddTableRowsAsync(MaconomyConnection connection, MaconomyTableRowsAdd add, CancellationToken cancellationToken = default)
        => _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.AddTableRowsAsync(new MaconomyEndpoint(connection.BaseUrl, connection.InstanceCode), token, add, ct),
            cancellationToken);

    public Task<IReadOnlyDictionary<string, string?>> SetTableRowAsync(
        MaconomyConnection connection, MaconomyTableRowSet set, CancellationToken cancellationToken = default)
        => _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.SetTableRowAsync(new MaconomyEndpoint(connection.BaseUrl, connection.InstanceCode), token, set, ct),
            cancellationToken);
}
