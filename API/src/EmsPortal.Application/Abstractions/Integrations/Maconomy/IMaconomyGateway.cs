namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>
/// The calls the portal makes to a Maconomy instance, as typed methods. Knows nothing about tenants or
/// where credentials live: it is handed an endpoint and whatever the call needs.
/// </summary>
public interface IMaconomyGateway
{
    /// <summary>
    /// <c>GET /auth/{instance}/login</c> with Basic auth, asking for a reconnect token. Returns the
    /// <c>Maconomy-Reconnect</c> response header. Throws <see cref="MaconomyException"/> with
    /// <see cref="MaconomyFailure.AuthFailed"/> when the credentials are refused.
    /// </summary>
    Task<string> LoginAsync(MaconomyEndpoint endpoint, string userName, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /containers/{instance}/{container}/filter</c> with a reconnect token. Throws
    /// <see cref="MaconomyException"/> with <see cref="MaconomyFailure.TokenRejected"/> when the token is
    /// refused, so the caller can log in and try once more.
    /// </summary>
    Task<MaconomyFilterResult> FilterAsync(
        MaconomyEndpoint endpoint,
        string reconnectToken,
        string container,
        MaconomyFilterRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a record: opens a container instance, follows its init-create link for a template, posts
    /// the values to the template's create link, each step carrying <c>Maconomy-Concurrency-Control</c>.
    /// Throws <see cref="MaconomyException"/> with <see cref="MaconomyFailure.Rejected"/> when Maconomy
    /// refuses the record, with its own message.
    /// </summary>
    Task<MaconomyCreatedRecord> CreateAsync(
        MaconomyEndpoint endpoint,
        string reconnectToken,
        MaconomyCreateRequest create,
        CancellationToken cancellationToken = default);
}
